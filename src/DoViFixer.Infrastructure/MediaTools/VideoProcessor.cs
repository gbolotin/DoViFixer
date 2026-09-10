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
        IProgress<OperationProgress>? progress, Guid operationId, CancellationToken cancellationToken, bool safe = false)
    {
        string raw = workspace.File("convert-input.hevc");
        string processed = workspace.File("converted.hevc");
        File.Delete(processed);
        if (!safe)
        {
            try
            {
                progress?.Report(new(operationId, "Streaming conversion", media.Source.Path));
                await processes.PipeAsync(new(tools.GetPath(NativeTool.FFmpeg), new[]
                {
                    "-nostdin", "-v", "error", "-i", media.Source.Path, "-map", "0:v:0", "-c:v", "copy",
                    "-an", "-sn", "-dn", "-bsf:v", "hevc_mp4toannexb", "-f", "hevc", "-"
                }), new(tools.GetPath(NativeTool.DoviTool), ConversionArguments("-")), cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogWarning(ex, "Streaming failed; retrying with disk extraction");
                progress?.Report(new(operationId, "Retrying with safe disk extraction", media.Source.Path));
                File.Delete(processed);
                safe = true;
            }
        }
        if (safe)
        {
            progress?.Report(new(operationId, "Extracting video", media.Source.Path));
            await ExtractVideoAsync(media, raw, cancellationToken, new MkvProgress(progress, operationId, "Extracting video", media.Source.Path).Report);
            progress?.Report(new(operationId, "Extracting video", media.Source.Path, 100));
            progress?.Report(new(operationId, "Converting metadata", media.Source.Path));
            await RunDoviAsync(ConversionArguments(raw), cancellationToken);
            File.Delete(raw);
            progress?.Report(new(operationId, "Converting metadata", media.Source.Path, 100));
        }
        string[] ConversionArguments(string input) => target == ConversionTarget.Profile81
            ? new[] { "-m", "2", "convert", "--discard", input, "-o", processed }
            : new[] { "remove", input, "-o", processed };
        progress?.Report(new(operationId, "Remuxing", media.Source.Path));
        logger.LogDebug("Stage {Stage}", "Remuxing");
        await RemuxAsync(media, processed, stagedOutput, workspace, cancellationToken, new MkvProgress(progress, operationId, "Remuxing", media.Source.Path).Report);
        progress?.Report(new(operationId, "Remuxing", media.Source.Path, 100));
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

    internal Task ExtractVideoAsync(MediaInfo media, string output, CancellationToken cancellationToken, Action<string>? outputLine = null) =>
        processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { "--gui-mode", media.Source.Path, "tracks", $"{media.VideoTrackId}:{output}" }, AllowWarnings: true, OutputLine: outputLine), cancellationToken);

    internal Task RunDoviAsync(string[] arguments, CancellationToken cancellationToken) =>
        processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), arguments), cancellationToken);

    private async Task RemuxAsync(MediaInfo media, string video, string output, ITemporaryWorkspace workspace, CancellationToken cancellationToken, Action<string>? outputLine = null)
    {
        string timestamps = workspace.File("source-video-timestamps.txt");
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { media.Source.Path, "timestamps_v2", $"{media.VideoTrackId}:{timestamps}" }, AllowWarnings: true), cancellationToken);
        var original = media.Tracks.Single(t => t.Id == media.VideoTrackId);
        using var identification = JsonDocument.Parse(media.IdentificationJson);
        var videoProperties = identification.RootElement.GetProperty("tracks").EnumerateArray()
            .Single(t => t.GetProperty("id").GetInt32() == media.VideoTrackId).GetProperty("properties");
        string tags = workspace.File("source-tags.xml");
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { media.Source.Path, "tags", tags }, AllowWarnings: true), cancellationToken);
        var document = File.Exists(tags) && new FileInfo(tags).Length > 0 ? XDocument.Load(tags) : new XDocument();
        // Preserve descriptive tags explicitly: MakeMKV lists SOURCE_ID as a statistic,
        // so mkvmerge's automatic statistics handling can discard or rewrite it.
        foreach (var simple in document.Descendants("Simple").Where(s =>
            s.Element("Name")?.Value is "BPS" or "DURATION" or "NUMBER_OF_FRAMES" or "NUMBER_OF_BYTES" ||
            (s.Element("Name")?.Value.StartsWith("_STATISTICS_", StringComparison.Ordinal) ?? false)).ToArray())
        {
            simple.Remove();
        }
        // Microsecond timecodes avoid millisecond rounding of high-rate TrueHD packets.
        var arguments = new List<string> { "--gui-mode", "--disable-track-statistics-tags", "--timestamp-scale", "1000", "-o", output, "--track-order",
            string.Join(",", media.Tracks.Select(t => t.Type == "video" ? "1:0" : $"0:{t.Id}")), "--no-video", "--no-track-tags" };
        foreach (var track in media.Tracks.Where(t => t.Type != "video"))
        {
            AppendTags(track, track.Id);
        }
        arguments.Add(media.Source.Path);
        arguments.AddRange(new[] { "--timestamps", $"0:{timestamps}", "--language", $"0:{original.Language}", "--track-name", $"0:{original.Name}",
            "--default-track-flag", $"0:{(original.Default ? 1 : 0)}", "--forced-display-flag", $"0:{(original.Forced ? 1 : 0)}" });
        MkvVideoMetadata.AppendOptions(videoProperties, arguments);
        AppendTags(original, 0);
        void AppendTags(MediaTrack track, int inputId)
        {
            var trackTags = document.Root?.Elements("Tag").Where(t => t.Elements("Simple").Any() &&
                t.Element("Targets")?.Elements("TrackUID").Any(u => u.Value == track.Uid) == true).ToArray() ?? [];
            if (trackTags.Length > 0)
            {
                string path = workspace.File($"track-{track.Id}-tags.xml");
                new XDocument(new XElement("Tags", trackTags.Select(t => new XElement(t)))).Save(path);
                arguments.AddRange(new[] { "--tags", $"{inputId}:{path}" });
            }
        }
        arguments.Add(video);
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), arguments, AllowWarnings: true, OutputLine: outputLine), cancellationToken);
    }

    internal static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
