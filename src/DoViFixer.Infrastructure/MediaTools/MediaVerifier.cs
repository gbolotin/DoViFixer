using DoViFixer.Application.Operations;
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
    public async Task<IReadOnlyList<string>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null, Guid operationId = default)
    {
        int completed = 0;
        // Checkpoints represent completed checks, not elapsed time. Reserve completion for the caller.
        const int total = 9;
        void ReportCheckpoint() => progress?.Report(new(operationId, "Verifying", source.Source.Path, 100.0 * completed++ / total));
        ReportCheckpoint();
        var target = await probe.ProbeAsync(output, cancellationToken);
        var failures = CompareMetadata(source, target, expectedProfile).ToList();
        ReportCheckpoint();
        long outputFrames = await probe.CountFramesAsync(output, cancellationToken);
        if (await probe.CountFramesAsync(source.Source.Path, cancellationToken) != outputFrames)
        {
            failures.Add("Video packet/frame count changed.");
        }

        ReportCheckpoint();
        if (failures.Count != 0)
        {
            return failures;
        }

        await VerifyTracksAsync(source, target, workspace, failures, cancellationToken);
        ReportCheckpoint();
        bool compareSourceRpu = source.Profile == DolbyVisionProfile.Profile7 && expectedProfile == DolbyVisionProfile.Profile81;
        string sourceVideo = workspace.File("before.hevc");
        await processor.ExtractVideoAsync(source, sourceVideo, cancellationToken);
        string beforeBaseHash = await HashBaseLayerAsync(sourceVideo, workspace, cancellationToken, compareSourceRpu);
        ReportCheckpoint();
        // Extract the output video once; the RPU check and the base-layer comparison both read it.
        string outputVideo = workspace.File("after.hevc");
        await processor.ExtractVideoAsync(target, outputVideo, cancellationToken);
        await VerifyRpuAsync(source, target, outputVideo, expectedProfile, outputFrames, workspace, failures, cancellationToken);
        // Delete the source stream before the output's base layer is cleaned, so verification never holds
        // more than two video streams at once (see ConversionPolicy.RequiredScratchBytes).
        File.Delete(sourceVideo);
        ReportCheckpoint();
        if (beforeBaseHash != await HashBaseLayerAsync(outputVideo, workspace, cancellationToken))
        {
            failures.Add("Base-layer video payload changed.");
        }

        ReportCheckpoint();
        await VerifyAttachmentsAsync(source, target, workspace, failures, cancellationToken);
        ReportCheckpoint();
        await VerifyXmlAsync(source, target, "chapters", workspace, failures, cancellationToken);
        ReportCheckpoint();
        await VerifyXmlAsync(source, target, "tags", workspace, failures, cancellationToken);
        return failures;
    }

    // Verify all retained track payloads and packet timestamps, including subtitles.
    // Each file is read once, instead of once per track and extraction mode.
    internal async Task VerifyTracksAsync(MediaInfo source, MediaInfo target, ITemporaryWorkspace workspace, List<string> failures, CancellationToken cancellationToken)
    {
        try
        {
            var beforeHashes = await ExtractTracksAsync(source, "before", workspace, cancellationToken);
            var afterHashes = await ExtractTracksAsync(target, "after", workspace, cancellationToken);
            for (int i = 0; i < source.Tracks.Count; i++)
            {
                if (beforeHashes[i] != afterHashes[i])
                {
                    failures.Add($"Track {source.Tracks[i].Id} payload changed.");
                }

                if (!await TimestampsMatchAsync(TimestampsFile(workspace, "before", i), TimestampsFile(workspace, "after", i), cancellationToken))
                {
                    failures.Add($"Track {source.Tracks[i].Id} timestamps changed or verification is unavailable.");
                }
            }
        }
        finally
        {
            // A failed pass can leave files behind; a retry in the same workspace must not inherit them.
            for (int i = 0; i < source.Tracks.Count; i++)
            {
                File.Delete(PayloadFile(workspace, "before", i));
                File.Delete(PayloadFile(workspace, "after", i));
                File.Delete(TimestampsFile(workspace, "before", i));
                File.Delete(TimestampsFile(workspace, "after", i));
            }
        }
    }

    // One mkvextract pass writes every non-video payload and every track's timestamps. Payloads are hashed and
    // deleted straight away, so scratch space holds one file's non-video tracks at most; timestamp files stay
    // for comparison with the other file. Returns the payload hash per track position, null for video tracks.
    private async Task<string?[]> ExtractTracksAsync(MediaInfo media, string prefix, ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        int[] payloads = Enumerable.Range(0, media.Tracks.Count).Where(i => media.Tracks[i].Type != "video").ToArray();
        var arguments = new List<string>
        {
            media.Source.Path
        };
        if (payloads.Length > 0)
        {
            arguments.Add("tracks");
            arguments.AddRange(payloads.Select(i => $"{media.Tracks[i].Id}:{PayloadFile(workspace, prefix, i)}"));
        }

        arguments.Add("timestamps_v2");
        arguments.AddRange(media.Tracks.Select((track, i) => $"{track.Id}:{TimestampsFile(workspace, prefix, i)}"));
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), arguments, AllowWarnings: true), cancellationToken);
        var hashes = new string?[media.Tracks.Count];
        foreach (int i in payloads)
        {
            string payload = PayloadFile(workspace, prefix, i);
            hashes[i] = await VideoProcessor.HashAsync(payload, cancellationToken);
            File.Delete(payload);
        }

        return hashes;
    }

    private static string PayloadFile(ITemporaryWorkspace workspace, string prefix, int position) => workspace.File($"verify-{prefix}-{position}.bin");
    private static string TimestampsFile(ITemporaryWorkspace workspace, string prefix, int position) => workspace.File($"verify-{prefix}-{position}-timestamps.txt");

    private async Task VerifyRpuAsync(MediaInfo source, MediaInfo target, string raw, DolbyVisionProfile expectedProfile, long frames, ITemporaryWorkspace workspace, List<string> failures, CancellationToken cancellationToken)
    {
        string rpu = workspace.File("verify.rpu");
        var extraction = await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[]
        {
            "extract-rpu", raw, "-o", rpu
        }, AcceptedExitCodes: new[]
        {
            0, 1
        }), cancellationToken);
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

        await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[]
        {
            "export", "-i", "verify.rpu", "-d", "all=verify-rpu.json"
        }, workspace.DirectoryPath), cancellationToken);
        await using var stream = File.OpenRead(workspace.File("verify-rpu.json"));
        long count = 0;
        bool wrongProfile = false;
        int expected = expectedProfile == DolbyVisionProfile.Profile7 ? 7 : 8;
        await foreach (var frame in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken))
        {
            count++;
            wrongProfile |= !frame.TryGetProperty("dovi_profile", out var profile) || profile.GetInt32() != expected;
        }

        if (source.Profile == DolbyVisionProfile.Profile7 && expectedProfile == DolbyVisionProfile.Profile81)
        {
            await using var originalStream = File.OpenRead(workspace.File("before.hevc"));
            long originalCount = (await RpuCoverage.ReadAsync(originalStream, cancellationToken, countRpuOnly: true)).Count;
            if (originalCount != count)
            {
                failures.Add("RPU metadata count changed during conversion; metadata must not be added or removed.");
            }
        }

        if (count != frames || wrongProfile)
        {
            if (wrongProfile || expectedProfile != DolbyVisionProfile.Profile81 || source.Profile != DolbyVisionProfile.Profile7 || count <= 0 || count >= frames)
            {
                failures.Add($"Output RPU profile/count does not match Profile {expected} and {frames} video frames.");
            }
            else
            {
                string originalRaw = workspace.File("before.hevc");
                try
                {
                    var after = await RpuCoverage.VerifyAsync(raw, target.Source.Path, count, tools, processes, cancellationToken, requireNoEnhancementLayer: true);
                    var before = await RpuCoverage.VerifyAsync(originalRaw, source.Source.Path, count, tools, processes, cancellationToken);
                    if (before != after || after.TotalFrames != frames)
                    {
                        failures.Add("Metadata-free ending or per-frame RPU positions changed during conversion.");
                    }
                }
                catch (InvalidDataException ex)
                {
                    failures.Add($"Metadata-free ending verification failed: {ex.Message}");
                }
                finally
                {
                    File.Delete(originalRaw);
                }
            }
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

        if (source.Tracks.Count != target.Tracks.Count || source.AttachmentCount != target.AttachmentCount || source.ChapterCount != target.ChapterCount || source.Title != target.Title)
        {
            failures.Add("Tracks, attachments, chapters or title changed.");
        }

        foreach (var pair in source.Tracks.Zip(target.Tracks))
        {
            var a = pair.First;
            var b = pair.Second;
            if (a.Type != b.Type || a.Codec != b.Codec || a.Name != b.Name || a.Default != b.Default || a.Forced != b.Forced)
            {
                failures.Add($"Track {a.Id} metadata or order changed.");
            }

            if (!TrackLanguage.Equivalent(a.Language, b.Language))
            {
                failures.Add($"Track {a.Id} language changed: '{a.Language}' -> '{b.Language}'.");
            }
        }

        using var sourceJson = JsonDocument.Parse(source.IdentificationJson);
        using var targetJson = JsonDocument.Parse(target.IdentificationJson);
        var sourceVideo = sourceJson.RootElement.GetProperty("tracks").EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == source.VideoTrackId).GetProperty("properties");
        var targetVideo = targetJson.RootElement.GetProperty("tracks").EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == target.VideoTrackId).GetProperty("properties");
        failures.AddRange(MkvVideoMetadata.Differences(sourceVideo, targetVideo).Select(p => $"Video container metadata changed: {p}."));
        return failures;
    }

    // Deletes the cleaned copy as soon as it is hashed so it never shares scratch space with a later extraction.
    private async Task<string> HashBaseLayerAsync(string raw, ITemporaryWorkspace workspace, CancellationToken cancellationToken, bool preserveRaw = false)
    {
        string clean = workspace.File(Path.GetFileNameWithoutExtension(raw) + "-clean.hevc");
        await processor.RunDoviAsync(new[]
        {
            "remove", raw, "-o", clean
        }, cancellationToken);
        if (!preserveRaw)
        {
            File.Delete(raw);
        }

        string hash = await VideoProcessor.HashAsync(clean, cancellationToken);
        File.Delete(clean);
        return hash;
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
            if (await VideoProcessor.HashAsync(before, cancellationToken) != await VideoProcessor.HashAsync(after, cancellationToken) || new[]
            {
                "file_name",
                "content_type",
                "description"
            }.Any(n => MediaMetadataParser.Text(pair.First, n) != MediaMetadataParser.Text(pair.Second, n)))
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

                foreach (var simple in document.Descendants("Simple").Where(s => s.Element("Name")?.Value is "BPS" or "DURATION" or "NUMBER_OF_FRAMES" or "NUMBER_OF_BYTES" || (s.Element("Name")?.Value.StartsWith("_STATISTICS_", StringComparison.Ordinal) ?? false)).ToArray())
                {
                    simple.Remove();
                }

                foreach (var tag in document.Descendants("Tag").Where(t => !t.Elements("Simple").Any()).ToArray())
                {
                    tag.Remove();
                }

                CanonicalizeTags(document);
                return string.Join("\n", (document.Root?.Elements() ?? []).Select(e => e.ToString(SaveOptions.DisableFormatting)).Order(StringComparer.Ordinal));
            }

            return document.Root?.ToString(SaveOptions.DisableFormatting) ?? "";
        }

        if (Normalize(before, source) != Normalize(after, target))
        {
            failures.Add($"{mode} metadata changed.");
        }
    }

    private Task ExtractAsync(string source, string mode, string destination, CancellationToken cancellationToken) => processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[]
    {
        source, mode, destination
    }, AllowWarnings: true), cancellationToken);
    internal static void CanonicalizeTags(XDocument document)
    {
        foreach (var simple in document.Descendants("Simple"))
        {
            var ietf = simple.Element("TagLanguageIETF");
            if (ietf is not null)
            {
                try
                {
                    // Only ignore a redundant primary-language annotation; retain regional variants.
                    // "und" resolves to the invariant culture, whose code is "ivl", so it is matched literally.
                    string language = ietf.Value == "und" ? "und" : CultureInfo.GetCultureInfo(ietf.Value).ThreeLetterISOLanguageName;
                    if (!ietf.Value.Contains('-') && language == (simple.Element("TagLanguage")?.Value ?? "und"))
                    {
                        ietf.Remove();
                    }
                }
                catch (CultureNotFoundException)
                {
                // Unknown language tags must still compare exactly.
                }
            }
        }

        foreach (var element in document.Descendants().Reverse().Where(e => e.HasElements).ToArray())
        {
            element.ReplaceNodes(element.Elements().OrderBy(e => e.Name.ToString(), StringComparer.Ordinal).ThenBy(e => e.ToString(SaveOptions.DisableFormatting), StringComparer.Ordinal).ToArray());
        }
    }

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

            if (!double.TryParse(x, CultureInfo.InvariantCulture, out double first) || !double.TryParse(y, CultureInfo.InvariantCulture, out double second) || !double.IsFinite(first) || !double.IsFinite(second) || Math.Abs(first - second) > 1)
            {
                return false;
            }

            count++;
        }
    }
}
