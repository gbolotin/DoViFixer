using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Configuration;

internal sealed class AnalysisCache(StorageOptions options, ILogger<AnalysisCache> logger) : IAnalysisCache
{
    // Bump when probing, evidence interpretation, or analysis algorithms change.
    private const int version = 1;
    private sealed record Entry(int Version, MediaAnalysis Analysis);

    public async Task<MediaAnalysis?> ReadAsync(FileIdentity source, AnalysisMethod method, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(CachePath(source, method), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, true);
            var entry = await JsonSerializer.DeserializeAsync<Entry>(stream, cancellationToken: cancellationToken);
            if (entry?.Version != version || entry.Analysis is not { Media.Source: { } identity, Evidence: { } evidence } analysis ||
                !string.Equals(identity.Path, source.Path, StringComparison.OrdinalIgnoreCase) ||
                identity.Length != source.Length || identity.LastWriteUtc != source.LastWriteUtc ||
                evidence.Method != method || !Cacheable(analysis))
            {
                return null;
            }
            // Recompute the verdict instead of trusting a serialized decision.
            return MediaClassifier.Classify(analysis.Media, evidence);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(ex, "Analysis cache miss for {Input}", source.Path);
            return null;
        }
    }

    public async Task WriteAsync(MediaAnalysis analysis, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Cacheable(analysis))
        {
            return;
        }
        string destination = CachePath(analysis.Media.Source, analysis.Evidence.Method);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, new Entry(version, analysis), cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save analysis cache for {Input}", analysis.Media.Source.Path);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not remove temporary cache file {Path}", temporary);
            }
        }
    }

    private static bool Cacheable(MediaAnalysis analysis) =>
        analysis.Evidence.Error is null &&
        MediaClassifier.Classify(analysis.Media, analysis.Evidence).Verdict is
            AnalysisVerdict.NotApplicable or AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel &&
        (analysis.Evidence.Method != AnalysisMethod.MetadataOnly || analysis.Media.Profile != DolbyVisionProfile.Profile7);

    private string CachePath(FileIdentity source, AnalysisMethod method)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(source.Path).ToUpperInvariant())));
        return Path.Combine(options.RootDirectory, "cache", "analysis", $"{key}.{method}.json");
    }
}
