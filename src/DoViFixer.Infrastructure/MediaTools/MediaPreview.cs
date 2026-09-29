using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Infrastructure.MediaTools.Processes;
using DoViFixer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools;

internal sealed class MediaPreview(DependencyService dependencies, IToolCatalog tools, IProcessRunner processes, ITemporaryWorkspaceFactory workspaces, StorageOptions options, ILogger<MediaPreview> logger) : IMediaPreview
{
    // ponytail: serialize previews and clearing; use per-file gates if concurrent previews become necessary.
    private readonly SemaphoreSlim gate = new(1, 1);
    private string CacheDirectory => Path.Combine(options.CacheDirectory, "frames");

    public Task<long> GetCacheSizeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = new DirectoryInfo(CacheDirectory);
        long bytes = directory.Exists ? directory.EnumerateFiles("*.png").Sum(file =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return file.Length;
        }) : 0;
        return Task.FromResult(bytes);
    }

    public async Task<byte[]> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await gate.WaitAsync(cancellationToken);
        try
        {
            path = Path.GetFullPath(path);
            string destination = CachePath(path);
            try
            {
                byte[] cached = await File.ReadAllBytesAsync(destination, cancellationToken);
                if (cached.Length > 32 && cached.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    && cached.AsSpan().EndsWith(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }))
                {
                    return cached;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Frame cache miss for {Input}", path);
            }

            byte[] image = await ExtractAsync(path, cancellationToken);
            // A file replaced during decoding must not populate the old identity's cache entry.
            if (destination == CachePath(path))
            {
                await SaveCacheAsync(destination, image, cancellationToken);
            }
            return image;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> ClearCacheAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            int removed = 0;
            if (Directory.Exists(CacheDirectory))
            {
                foreach (string path in Directory.EnumerateFiles(CacheDirectory, "*.png", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(path);
                    removed++;
                }
            }
            return removed;
        }
        finally
        {
            gate.Release();
        }
    }

    private string CachePath(string path)
    {
        var source = new FileInfo(path);
        // Bump the version when frame selection, dimensions, or tone mapping changes.
        string identity = FormattableString.Invariant($"1\n{path.ToUpperInvariant()}\n{source.Length}\n{source.LastWriteTimeUtc.Ticks}");
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return Path.Combine(CacheDirectory, key + ".png");
    }

    private async Task SaveCacheAsync(string destination, byte[] image, CancellationToken cancellationToken)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(temporary, image, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save frame cache at {Path}", destination);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Could not remove temporary frame cache at {Path}", temporary);
            }
        }
    }

    private async Task<byte[]> ExtractAsync(string path, CancellationToken cancellationToken)
    {
        await dependencies.RequireAsync([NativeTool.FFmpeg, NativeTool.FFprobe], cancellationToken);
        var probe = await processes.RunAsync(new(tools.GetPath(NativeTool.FFprobe), new[]
        {
            "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=color_transfer:format=duration", "-of", "json", path
        }, Timeout: TimeSpan.FromSeconds(15)), cancellationToken);
        using var metadata = JsonDocument.Parse(probe.Output);
        var streams = metadata.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0)
        {
            throw new InvalidDataException("No video stream is available for preview.");
        }

        double position = 0;
        if (metadata.RootElement.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var duration)
            && double.TryParse(duration.GetString(), CultureInfo.InvariantCulture, out double seconds)
            && double.IsFinite(seconds) && seconds > 0)
        {
            position = Math.Min(seconds * 0.1, 30);
        }

        string filter = "scale=640:360:force_original_aspect_ratio=decrease,setsar=1";
        if (streams[0].TryGetProperty("color_transfer", out var transfer)
            && transfer.GetString() is "smpte2084" or "arib-std-b67")
        {
            filter += ",zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=full";
        }

        await using var workspace = await workspaces.CreateAsync(1024 * 1024, null, cancellationToken);
        string image = workspace.File("preview.png");
        await processes.RunAsync(new(tools.GetPath(NativeTool.FFmpeg), new[]
        {
            "-nostdin", "-v", "error", "-ss", position.ToString("F3", CultureInfo.InvariantCulture), "-i", path,
            "-map", "0:v:0", "-frames:v", "1", "-an", "-sn", "-dn", "-vf", filter + ",format=rgb24", "-update", "1", image
        }, Timeout: TimeSpan.FromSeconds(20)), cancellationToken);
        return await File.ReadAllBytesAsync(image, cancellationToken);
    }
}
