using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Configuration;
internal sealed class AnalysisCache(StorageOptions options, ILogger<AnalysisCache> logger) : IAnalysisCache
{
    public string RootDirectory => options.CacheDirectory;
    // Bump when probing, evidence interpretation, or analysis algorithms change.
    private const int version = 1;
    private sealed record Entry(int Version, MediaAnalysis Analysis);
    private sealed record HashEntry(int Version, FileIdentity Source, string Sha256);
    private sealed record ResultEntry(int Version, FileIdentity Source, OperationItemResult Result);
    public Task<long> GetSizeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = new DirectoryInfo(Path.Combine(RootDirectory, "analysis"));
        long bytes = directory.Exists ? directory.EnumerateFiles("*.json").Sum(file =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return file.Length;
        }) : 0;
        return Task.FromResult(bytes);
    }

    public Task<int> ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.Combine(RootDirectory, "analysis");
        int removed = 0;
        if (Directory.Exists(directory))
        {
            foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(path);
                removed++;
            }
        }

        logger.LogInformation("Cleared {Count} analysis cache entries", removed);
        return Task.FromResult(removed);
    }

    public async Task<MediaAnalysis?> ReadAsync(FileIdentity source, AnalysisMethod method, CancellationToken cancellationToken)
    {
        var entry = await ReadEntryAsync<Entry>(CachePath(source, method.ToString()), "Analysis", source.Path, cancellationToken);
        if (entry?.Version != version || entry.Analysis is not
        {
            Media.Source:
            {
            }
            identity, Evidence:
            {
            }
            evidence
        }
        analysis || !SameFile(identity, source) || evidence.Method != method || !Cacheable(analysis))
        {
            return null;
        }

        // Recompute the verdict instead of trusting a serialized decision.
        return MediaClassifier.Classify(analysis.Media, evidence);
    }

    public async Task<string?> ReadBaseLayerHashAsync(FileIdentity source, CancellationToken cancellationToken)
    {
        var entry = await ReadEntryAsync<HashEntry>(CachePath(source, "base-layer"), "Base-layer hash", source.Path, cancellationToken);
        return entry?.Version == version && entry.Source is { } identity && SameFile(identity, source)
            && entry.Sha256 is { Length: 64 } && entry.Sha256.All(Uri.IsHexDigit) ? entry.Sha256 : null;
    }

    public Task WriteBaseLayerHashAsync(FileIdentity source, string sha256, CancellationToken cancellationToken) =>
        WriteEntryAsync(CachePath(source, "base-layer"), new HashEntry(version, source, sha256), source.Path, cancellationToken);

    public async Task<OperationItemResult?> ReadConversionResultAsync(FileIdentity source, CancellationToken cancellationToken)
    {
        var entry = await ReadEntryAsync<ResultEntry>(CachePath(source, "conversion"), "Conversion result", source.Path, cancellationToken);
        return entry?.Version == version && entry.Source is { } identity && SameFile(identity, source)
            && entry.Result is { Output: not null } result ? result : null;
    }

    public Task WriteConversionResultAsync(FileIdentity source, OperationItemResult result, CancellationToken cancellationToken) =>
        WriteEntryAsync(CachePath(source, "conversion"), new ResultEntry(version, source, result), source.Path, cancellationToken);

    private static bool SameFile(FileIdentity cached, FileIdentity source) =>
        string.Equals(cached.Path, source.Path, StringComparison.OrdinalIgnoreCase) && cached.Length == source.Length && cached.LastWriteUtc == source.LastWriteUtc;

    public Task WriteAsync(MediaAnalysis analysis, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Cacheable(analysis))
        {
            return Task.CompletedTask;
        }

        return WriteEntryAsync(CachePath(analysis.Media.Source, analysis.Evidence.Method.ToString()), new Entry(version, analysis), analysis.Media.Source.Path, cancellationToken);
    }

    private async Task<T?> ReadEntryAsync<T>(string path, string kind, string input, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(ex, "{Kind} cache miss for {Input}", kind, input);
            return null;
        }
    }

    private async Task WriteEntryAsync<T>(string destination, T entry, string input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception ex)when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save analysis cache for {Input}", input);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex)when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not remove temporary cache file {Path}", temporary);
            }
        }
    }

    private static bool Cacheable(MediaAnalysis analysis) => analysis.Evidence.Error is null && MediaClassifier.Classify(analysis.Media, analysis.Evidence).Verdict is AnalysisVerdict.NotApplicable or AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel or AnalysisVerdict.FelUnclassified && (analysis.Evidence.Method != AnalysisMethod.MetadataOnly || analysis.Media.Profile != DolbyVisionProfile.Profile7);
    private string CachePath(FileIdentity source, string method)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(source.Path).ToUpperInvariant())));
        return Path.Combine(RootDirectory, "analysis", $"{key}.{method}.json");
    }
}
