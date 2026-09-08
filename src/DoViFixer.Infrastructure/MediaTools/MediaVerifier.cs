using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.MediaTools.Processes;

namespace DoViFixer.Infrastructure.MediaTools;

internal sealed class MediaVerifier(MediaProbe probe, VideoProcessor processor, IToolCatalog tools, IProcessRunner processes) : IMediaVerifier
{
    public async Task<IReadOnlyList<string>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile,
        ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        var target = await probe.ProbeAsync(output, cancellationToken);
        var failures = CompareMetadata(source, target, expectedProfile).ToList();
        long outputFrames = await probe.CountFramesAsync(output, cancellationToken);
        if (await probe.CountFramesAsync(source.Source.Path, cancellationToken) != outputFrames)
        {
            failures.Add("Video packet/frame count changed.");
        }
        if (failures.Count != 0)
        {
            return failures;
        }
        // Verify all retained track payloads and packet timestamps, including subtitles.
        for (int i = 0; i < source.Tracks.Count; i++)
        {
            var original = source.Tracks[i];
            var converted = target.Tracks[i];
            string before = workspace.File("verify-before.bin");
            string after = workspace.File("verify-after.bin");
            if (original.Type != "video")
            {
                await ExtractAsync(source.Source.Path, "tracks", $"{original.Id}:{before}", cancellationToken);
                await ExtractAsync(output, "tracks", $"{converted.Id}:{after}", cancellationToken);
                if (await VideoProcessor.HashAsync(before, cancellationToken) != await VideoProcessor.HashAsync(after, cancellationToken))
                {
                    failures.Add($"Track {original.Id} payload changed.");
                }
                File.Delete(before);
                File.Delete(after);
            }
            await ExtractAsync(source.Source.Path, "timestamps_v2", $"{original.Id}:{before}", cancellationToken);
            await ExtractAsync(output, "timestamps_v2", $"{converted.Id}:{after}", cancellationToken);
            if (!await TimestampsMatchAsync(before, after, cancellationToken))
            {
                failures.Add($"Track {original.Id} timestamps changed or verification is unavailable.");
            }
            File.Delete(before);
            File.Delete(after);
        }
        string beforeBase = await CleanBaseAsync(source, workspace, "before", cancellationToken);
        await VerifyRpuAsync(target, expectedProfile, outputFrames, workspace, failures, cancellationToken);
        string afterBase = await CleanBaseAsync(target, workspace, "after", cancellationToken);
        if (await VideoProcessor.HashAsync(beforeBase, cancellationToken) != await VideoProcessor.HashAsync(afterBase, cancellationToken))
        {
            failures.Add("Base-layer video payload changed.");
        }
        File.Delete(beforeBase);
        File.Delete(afterBase);
        await VerifyAttachmentsAsync(source, target, workspace, failures, cancellationToken);
        await VerifyXmlAsync(source, target, "chapters", workspace, failures, cancellationToken);
        await VerifyXmlAsync(source, target, "tags", workspace, failures, cancellationToken);
        return failures;
    }

    private async Task VerifyRpuAsync(MediaInfo target, DolbyVisionProfile expectedProfile, long frames,
        ITemporaryWorkspace workspace, List<string> failures, CancellationToken cancellationToken)
    {
        string raw = workspace.File("verify-rpu.hevc");
        string rpu = workspace.File("verify.rpu");
        await processor.ExtractVideoAsync(target, raw, cancellationToken);
        var extraction = await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "extract-rpu", raw, "-o", rpu }, AcceptedExitCodes: new[] { 0, 1 }), cancellationToken);
        File.Delete(raw);
        if (expectedProfile == DolbyVisionProfile.None)
        {
            if (extraction.ExitCode != 1 || extraction.Error.Trim() != "Error: No RPU was found in input file")
            {
                failures.Add("HDR10 output still contains RPU data or its absence could not be verified.");
            }
            return;
        }
        if (extraction.ExitCode != 0 || !File.Exists(rpu) || new FileInfo(rpu).Length == 0)
        {
            failures.Add("Output RPU data is absent or unreadable.");
            return;
        }
        await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "export", "-i", "verify.rpu", "-d", "all=verify-rpu.json" }, workspace.DirectoryPath), cancellationToken);
        await using var stream = File.OpenRead(workspace.File("verify-rpu.json"));
        long count = 0;
        bool wrongProfile = false;
        int expected = expectedProfile == DolbyVisionProfile.Profile7 ? 7 : 8;
        await foreach (var frame in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken))
        {
            count++;
            wrongProfile |= !frame.TryGetProperty("dovi_profile", out var profile) || profile.GetInt32() != expected;
        }
        if (count != frames || wrongProfile)
        {
            failures.Add($"Output RPU profile/count does not match Profile {expected} and {frames} video frames.");
        }
    }

    internal static IReadOnlyList<string> CompareMetadata(MediaInfo source, MediaInfo target, DolbyVisionProfile profile)
    {
        var failures = new List<string>();
        if (target.Profile != profile)
        {
            failures.Add($"Expected {profile}; detected {target.Profile}.");
        }
        if (target.Width <= 0 || target.Height <= 0 || source.Width != target.Width || source.Height != target.Height || source.VideoCodec != target.VideoCodec)
        {
            failures.Add("Video codec/dimensions changed or are unavailable.");
        }
        if (source.DurationSeconds is null or <= 0 || target.DurationSeconds is null or <= 0 || Math.Abs(source.DurationSeconds.Value - target.DurationSeconds.Value) > 0.05)
        {
            failures.Add("Video duration differs by more than 50 ms or is unavailable.");
        }
        if (source.Tracks.Count != target.Tracks.Count || source.AttachmentCount != target.AttachmentCount ||
            source.ChapterCount != target.ChapterCount || source.Title != target.Title)
        {
            failures.Add("Tracks, attachments, chapters or title changed.");
        }
        foreach (var pair in source.Tracks.Zip(target.Tracks))
        {
            var a = pair.First;
            var b = pair.Second;
            if (a.Type != b.Type || a.Codec != b.Codec || a.Language != b.Language || a.Name != b.Name || a.Default != b.Default || a.Forced != b.Forced)
            {
                failures.Add($"Track {a.Id} metadata or order changed.");
            }
        }
        using var sourceJson = JsonDocument.Parse(source.IdentificationJson);
        using var targetJson = JsonDocument.Parse(target.IdentificationJson);
        var sourceVideo = sourceJson.RootElement.GetProperty("tracks").EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == source.VideoTrackId).GetProperty("properties");
        var targetVideo = targetJson.RootElement.GetProperty("tracks").EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == target.VideoTrackId).GetProperty("properties");
        failures.AddRange(MkvVideoMetadata.Differences(sourceVideo, targetVideo).Select(p => $"Video container metadata changed: {p}."));
        return failures;
    }

    private async Task<string> CleanBaseAsync(MediaInfo media, ITemporaryWorkspace workspace, string prefix, CancellationToken cancellationToken)
    {
        string raw = workspace.File(prefix + ".hevc");
        string clean = workspace.File(prefix + "-clean.hevc");
        await processor.ExtractVideoAsync(media, raw, cancellationToken);
        await processor.RunDoviAsync(new[] { "remove", raw, "-o", clean }, cancellationToken);
        File.Delete(raw);
        return clean;
    }

    private async Task VerifyAttachmentsAsync(MediaInfo source, MediaInfo target, ITemporaryWorkspace workspace, List<string> failures, CancellationToken cancellationToken)
    {
        using var a = JsonDocument.Parse(source.IdentificationJson);
        using var b = JsonDocument.Parse(target.IdentificationJson);
        if (source.AttachmentCount == 0)
        {
            return;
        }
        foreach (var pair in a.RootElement.GetProperty("attachments").EnumerateArray().Zip(b.RootElement.GetProperty("attachments").EnumerateArray()))
        {
            string before = workspace.File("attachment-before.bin");
            string after = workspace.File("attachment-after.bin");
            await ExtractAsync(source.Source.Path, "attachments", $"{pair.First.GetProperty("id").GetInt32()}:{before}", cancellationToken);
            await ExtractAsync(target.Source.Path, "attachments", $"{pair.Second.GetProperty("id").GetInt32()}:{after}", cancellationToken);
            if (await VideoProcessor.HashAsync(before, cancellationToken) != await VideoProcessor.HashAsync(after, cancellationToken) ||
                new[] { "file_name", "content_type", "description" }.Any(n => MediaMetadataParser.Text(pair.First, n) != MediaMetadataParser.Text(pair.Second, n)))
            {
                failures.Add("Attachment data or metadata changed.");
            }
            File.Delete(before);
            File.Delete(after);
        }
    }

    private async Task VerifyXmlAsync(MediaInfo source, MediaInfo target, string mode, ITemporaryWorkspace workspace, List<string> failures, CancellationToken cancellationToken)
    {
        string before = workspace.File(mode + "-before.xml");
        string after = workspace.File(mode + "-after.xml");
        await ExtractAsync(source.Source.Path, mode, before, cancellationToken);
        await ExtractAsync(target.Source.Path, mode, after, cancellationToken);
        string Normalize(string path, MediaInfo media)
        {
            // mkvextract succeeds without creating a file when this metadata is absent.
            if (!File.Exists(path))
            {
                return "";
            }
            string text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }
            var document = XDocument.Parse(text);
            if (mode == "tags")
            {
                foreach (var uid in document.Descendants("TrackUID"))
                {
                    int index = media.Tracks.ToList().FindIndex(t => t.Uid == uid.Value);
                    uid.Value = "track-" + index;
                }
                foreach (var simple in document.Descendants("Simple").Where(s =>
                    s.Element("Name")?.Value is "BPS" or "DURATION" or "NUMBER_OF_FRAMES" or "NUMBER_OF_BYTES" ||
                    (s.Element("Name")?.Value.StartsWith("_STATISTICS_", StringComparison.Ordinal) ?? false)).ToArray())
                {
                    simple.Remove();
                }
                foreach (var tag in document.Descendants("Tag").Where(t => !t.Elements("Simple").Any()).ToArray())
                {
                    tag.Remove();
                }
                return string.Join("\n", (document.Root?.Elements() ?? []).Select(e => e.ToString(SaveOptions.DisableFormatting)).Order(StringComparer.Ordinal));
            }
            return document.Root?.ToString(SaveOptions.DisableFormatting) ?? "";
        }
        if (Normalize(before, source) != Normalize(after, target))
        {
            failures.Add($"{mode} metadata changed.");
        }
    }

    private Task ExtractAsync(string source, string mode, string destination, CancellationToken cancellationToken) =>
        processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { source, mode, destination }, AllowWarnings: true), cancellationToken);

    internal static async Task<bool> TimestampsMatchAsync(string before, string after, CancellationToken cancellationToken)
    {
        using var a = File.OpenText(before);
        using var b = File.OpenText(after);
        long count = 0;
        while (true)
        {
            string? x = await a.ReadLineAsync(cancellationToken);
            string? y = await b.ReadLineAsync(cancellationToken);
            if (x is null || y is null)
            {
                return x is null && y is null && count > 0;
            }
            if (x.StartsWith('#') && y.StartsWith('#'))
            {
                continue;
            }
            if (!double.TryParse(x, CultureInfo.InvariantCulture, out double first) || !double.TryParse(y, CultureInfo.InvariantCulture, out double second) ||
                !double.IsFinite(first) || !double.IsFinite(second) || Math.Abs(first - second) > 1)
            {
                return false;
            }
            count++;
        }
    }
}
