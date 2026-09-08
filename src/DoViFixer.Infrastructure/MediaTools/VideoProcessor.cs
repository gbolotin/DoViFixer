using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools;

internal sealed class VideoProcessor(IToolCatalog tools, IProcessRunner processes, TimeProvider time, ILogger<VideoProcessor> logger) : IVideoProcessor
{
    public async Task ConvertAsync(MediaInfo media, ConversionTarget target, ITemporaryWorkspace workspace, string stagedOutput,
        IProgress<OperationProgress>? progress, Guid operationId, CancellationToken cancellationToken)
    {
        progress?.Report(new(operationId, "Extracting video", media.Source.Path));
        logger.LogDebug("Stage {Stage}", "Extracting video");
        string raw = workspace.File("convert-input.hevc");
        string processed = workspace.File("converted.hevc");
        await ExtractVideoAsync(media, raw, cancellationToken);
        progress?.Report(new(operationId, "Converting metadata", media.Source.Path));
        logger.LogDebug("Stage {Stage}", "Converting metadata");
        await RunDoviAsync(target == ConversionTarget.Profile81
            ? new[] { "-m", "2", "convert", "--discard", raw, "-o", processed }
            : new[] { "remove", raw, "-o", processed }, cancellationToken);
        File.Delete(raw);
        progress?.Report(new(operationId, "Remuxing", media.Source.Path));
        logger.LogDebug("Stage {Stage}", "Remuxing");
        await RemuxAsync(media, processed, stagedOutput, workspace, cancellationToken);
    }

    public async Task<ArchiveManifest> ExtractBackupAsync(MediaInfo media, ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        string raw = workspace.File("backup-input.hevc");
        string bl = workspace.File("backup-bl.hevc");
        string clean = workspace.File("backup-clean.hevc");
        string el = workspace.File("el.hevc");
        await ExtractVideoAsync(media, raw, cancellationToken);
        await RunDoviAsync(new[] { "demux", "-i", raw, "-b", bl, "-e", el }, cancellationToken);
        File.Delete(raw);
        await RunDoviAsync(new[] { "remove", bl, "-o", clean }, cancellationToken);
        var manifest = new ArchiveManifest(1, Path.GetFileName(media.Source.Path), await HashAsync(clean, cancellationToken),
            await HashAsync(el, cancellationToken), new FileInfo(el).Length, media.FrameCount, time.GetUtcNow());
        File.Delete(bl);
        File.Delete(clean);
        return manifest;
    }

    public async Task RestoreAsync(MediaInfo media, ArchiveManifest? manifest, ITemporaryWorkspace workspace, string stagedOutput, CancellationToken cancellationToken)
    {
        string raw = workspace.File("restore-input.hevc");
        string clean = workspace.File("restore-clean.hevc");
        string restored = workspace.File("restored.hevc");
        await ExtractVideoAsync(media, raw, cancellationToken);
        await RunDoviAsync(new[] { "remove", raw, "-o", clean }, cancellationToken);
        File.Delete(raw);
        if (manifest is not null && !string.Equals(await HashAsync(clean, cancellationToken), manifest.BaseLayerSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Archive does not belong to this base-layer video (SHA-256 mismatch).");
        }
        await RunDoviAsync(new[] { "mux", "--no-add-aud", "--bl", clean, "--el", workspace.File("el.hevc"), "-o", restored }, cancellationToken);
        File.Delete(clean);
        // Verify reconstructed enhancement payload before container publication.
        string verifiedEl = workspace.File("restored-el.hevc");
        await RunDoviAsync(new[] { "demux", "--el-only", "-i", restored, "-e", verifiedEl }, cancellationToken);
        if (await HashAsync(verifiedEl, cancellationToken) != await HashAsync(workspace.File("el.hevc"), cancellationToken))
        {
            throw new InvalidDataException("Reconstructed enhancement-layer payload differs from the archive.");
        }
        File.Delete(verifiedEl);
        await RemuxAsync(media, restored, stagedOutput, workspace, cancellationToken);
    }

    internal Task ExtractVideoAsync(MediaInfo media, string output, CancellationToken cancellationToken) =>
        processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { media.Source.Path, "tracks", $"{media.VideoTrackId}:{output}" }, AllowWarnings: true), cancellationToken);

    internal Task RunDoviAsync(string[] arguments, CancellationToken cancellationToken) =>
        processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), arguments), cancellationToken);

    private async Task RemuxAsync(MediaInfo media, string video, string output, ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        string timestamps = workspace.File("source-video-timestamps.txt");
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { media.Source.Path, "timestamps_v2", $"{media.VideoTrackId}:{timestamps}" }, AllowWarnings: true), cancellationToken);
        var original = media.Tracks.Single(t => t.Id == media.VideoTrackId);
        using var identification = JsonDocument.Parse(media.IdentificationJson);
        var videoProperties = identification.RootElement.GetProperty("tracks").EnumerateArray()
            .Single(t => t.GetProperty("id").GetInt32() == media.VideoTrackId).GetProperty("properties");
        var arguments = new List<string> { "--disable-track-statistics-tags", "-o", output, "--track-order",
            string.Join(",", media.Tracks.Select(t => t.Type == "video" ? "1:0" : $"0:{t.Id}")), "--no-video", media.Source.Path,
            "--timestamps", $"0:{timestamps}", "--language", $"0:{original.Language}", "--track-name", $"0:{original.Name}",
            "--default-track-flag", $"0:{(original.Default ? 1 : 0)}", "--forced-display-flag", $"0:{(original.Forced ? 1 : 0)}" };
        MkvVideoMetadata.AppendOptions(videoProperties, arguments);
        string tags = workspace.File("source-tags.xml");
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { media.Source.Path, "tags", tags }, AllowWarnings: true), cancellationToken);
        var document = File.Exists(tags) && new FileInfo(tags).Length > 0 ? XDocument.Load(tags) : new XDocument();
        var videoTags = document.Root?.Elements("Tag").Where(t => t.Element("Targets")?.Elements("TrackUID").Any(u => u.Value == original.Uid) == true).ToArray() ?? [];
        if (videoTags.Length > 0)
        {
            string videoTagsPath = workspace.File("video-tags.xml");
            new XDocument(new XElement("Tags", videoTags.Select(t => new XElement(t)))).Save(videoTagsPath);
            arguments.AddRange(new[] { "--tags", $"0:{videoTagsPath}" });
        }
        arguments.Add(video);
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), arguments, AllowWarnings: true), cancellationToken);
    }

    internal static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
