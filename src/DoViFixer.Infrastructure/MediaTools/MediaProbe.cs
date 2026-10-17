using System.Globalization;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.MediaTools.DoviTool;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools;
internal sealed class MediaProbe(IToolCatalog tools, IProcessRunner processes, IFileOperations files, ILogger<MediaProbe> logger, ISettingsStore? settings = null, IHardwareInfoProvider? hardware = null) : IMediaProbe
{
    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var identity = files.Identify(path);
        var mkv = await processes.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), new[]
        {
            "-J", identity.Path
        }, AllowWarnings: true), cancellationToken);
        var mi = await processes.RunAsync(new(tools.GetPath(NativeTool.MediaInfo), new[]
        {
            "--Output=JSON", identity.Path
        }), cancellationToken);
        return MediaMetadataParser.Parse(identity, mkv.Output, mi.Output);
    }

    public async Task<RpuEvidence> AnalyzeAsync(MediaInfo media, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null)
    {
        var snapshot = settings is null ? new UserSettings() : await settings.ReadAsync(cancellationToken);
        var evidence = await AnalyzeCoreAsync(media, method, workspace, snapshot, cancellationToken, progress);
        return evidence with { Options = snapshot.AnalysisOptions };
    }

    private async Task<RpuEvidence> AnalyzeCoreAsync(MediaInfo media, AnalysisMethod method, ITemporaryWorkspace workspace, UserSettings snapshot, CancellationToken cancellationToken, IProgress<OperationProgress>? progress)
    {
        try
        {
            if (method is AnalysisMethod.FullRpu or AnalysisMethod.DeepInspection)
            {
                string raw = workspace.File("inspection.hevc");
                RpuEvidence evidence;
                long? frames = null;
                if (method == AnalysisMethod.FullRpu)
                {
                    bool streamed = false;
                    try
                    {
                        evidence = await AnalyzeStreamAsync(media, workspace, progress, cancellationToken);
                        streamed = true;
                    }
                    catch (Exception ex) when (ex is (IOException or InvalidDataException or JsonException or TimeoutException) && !cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning(ex, "Streaming RPU extraction failed; retrying once with disk extraction for {Input}", media.Source.Path);
                        File.Delete(workspace.File("inspection.rpu"));
                        File.Delete(workspace.File("rpu.json"));
                        progress?.Report(new(Guid.Empty, "Retrying with disk extraction", media.Source.Path));
                        evidence = await AnalyzeDiskAsync(media, method, raw, workspace, progress, cancellationToken);
                    }

                    if (streamed)
                    {
                        progress?.Report(new(Guid.Empty, "Checking frame coverage", media.Source.Path));
                        frames = await CountFramesAsync(media.Source.Path, cancellationToken);
                        if (evidence.Frames <= 0 || evidence.Frames != frames)
                        {
                            // A shorter RPU stream may have a valid metadata-free ending, but only the raw
                            // access units can establish its position and that no enhancement data follows it.
                            logger.LogInformation("Streaming RPU count {RpuFrames} differs from packet count {VideoFrames}; verifying with disk extraction", evidence.Frames, frames);
                            evidence = await AnalyzeDiskAsync(media, method, raw, workspace, progress, cancellationToken);
                        }
                    }
                }
                else
                {
                    evidence = await AnalyzeDiskAsync(media, method, raw, workspace, progress, cancellationToken);
                }

                progress?.Report(new(Guid.Empty, "Checking frame coverage", media.Source.Path));
                frames ??= await CountFramesAsync(media.Source.Path, cancellationToken);
                if (evidence.Frames != frames.Value)
                {
                    if (method == AnalysisMethod.DeepInspection || evidence.Frames <= 0 || evidence.Frames > frames.Value)
                    {
                        return evidence with
                        {
                            Error = $"Full RPU count {evidence.Frames} differs from video packet count {frames}. Deep inspection requires complete frame coverage."
                        };
                    }

                    var coverage = await RpuCoverage.VerifyAsync(raw, media.Source.Path, evidence.Frames, tools, processes, cancellationToken);
                    if (coverage.TotalFrames != frames.Value)
                    {
                        throw new InvalidDataException("RPU coverage frame count differs from video packet count.");
                    }

                    evidence = evidence with
                    {
                        MetadataFreeTail = coverage
                    };
                }

                if (method == AnalysisMethod.DeepInspection)
                {
                    // Demuxing rewrites the stream once; decoding and measuring every frame dominates the stage.
                    var measuring = new PhasedProgress(progress, Guid.Empty, "Measuring base-layer brightness", media.Source.Path, 5, 95);
                    measuring.Report(0, 0);
                    var brightness = await AnalyzeBrightnessAsync(raw, frames.Value, measuring, workspace, snapshot.BrightnessMarginNits, cancellationToken);
                    progress?.Report(new(Guid.Empty, "Measuring base-layer brightness", media.Source.Path, 100));
                    return brightness;
                }

                progress?.Report(new(Guid.Empty, "Checking frame coverage", media.Source.Path, 100));
                return evidence;
            }

            if (media.DurationSeconds is null or <= 0)
            {
                return new(method, EnhancementLayer.Unknown, 0, null, 0, 10, "Duration is unavailable for sample selection.");
            }

            int workers = (hardware?.Profile ?? new HardwareProfile(1, 1, false)).AnalysisWorkers(snapshot.AnalysisWorkers);
            var results = new RpuEvidence?[10];
            var diagnostics = new string?[10];
            int completed = 0;
            var progressLock = new object();
            await Parallel.ForEachAsync(Enumerable.Range(0, 10), new ParallelOptions
            {
                MaxDegreeOfParallelism = workers,
                CancellationToken = cancellationToken
            }, async (i, token) =>
            {
                string raw = workspace.File($"sample-{i}.hevc");
                string rpuName = $"sample-{i}.rpu";
                string jsonName = $"sample-{i}.json";
                lock (progressLock)
                {
                    progress?.Report(new(Guid.Empty, $"Analyzing sample {i + 1} of 10", media.Source.Path, completed * 10));
                }
                try
                {
                    string time = (media.DurationSeconds.Value * (0.05 + i * 0.1)).ToString("F3", CultureInfo.InvariantCulture);
                    await processes.RunAsync(new(tools.GetPath(NativeTool.FFmpeg), new[]
                    {
                        "-nostdin", "-v", "error", "-ss", time, "-i", media.Source.Path, "-map", "0:v:0", "-c:v", "copy", "-an", "-sn", "-dn", "-bsf:v", "hevc_mp4toannexb", "-f", "hevc", "-t", "1", raw
                    }), token);
                    results[i] = await AnalyzeRawAsync(raw, method, workspace, token, rpuName, jsonName);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
                {
                    logger.LogDebug(ex, "RPU sample {SampleIndex} failed for {Input}", i, media.Source.Path);
                    var position = TimeSpan.FromSeconds(media.DurationSeconds.Value * (0.05 + i * 0.1));
                    diagnostics[i] = $"Sample {i + 1}/10 at {position:hh\\:mm\\:ss}: {ex.Message}";
                    // Missing sample coverage remains Unknown; never turn a failed probe into complex FEL.
                }
                finally
                {
                    File.Delete(raw);
                    File.Delete(workspace.File(rpuName));
                    File.Delete(workspace.File(jsonName));
                }
                lock (progressLock)
                {
                    completed++;
                    progress?.Report(new(Guid.Empty, $"Analyzed {completed} of 10 samples", media.Source.Path, completed * 10));
                }
            });
            var samples = results.OfType<RpuEvidence>().ToList();
            var failures = diagnostics.OfType<string>().ToArray();

            progress?.Report(new(Guid.Empty, "Samples analyzed", media.Source.Path, 100));
            EnhancementLayer layer = samples.Any(s => s.Layer == EnhancementLayer.Fel) ? EnhancementLayer.Fel : samples.Count > 0 && samples.All(s => s.Layer == EnhancementLayer.Mel) ? EnhancementLayer.Mel : EnhancementLayer.Unknown;
            return new(method, layer, samples.Sum(s => s.Frames), samples.Select(s => s.PeakNits).Max(), samples.Count, 10, SampleDiagnostics: failures.Length == 0 ? null : string.Join("\n", failures));
        }
        catch (Exception ex)when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RPU analysis failed for {Input}; method {Method}", media.Source.Path, method);
            return new(method, EnhancementLayer.Unknown, 0, null, 0, 1, ex.Message);
        }
    }

    private async Task<RpuEvidence> AnalyzeDiskAsync(MediaInfo media, AnalysisMethod method, string raw, ITemporaryWorkspace workspace, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        progress?.Report(new(Guid.Empty, "Extracting video", media.Source.Path, 0));
        await processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[]
        {
            "--gui-mode", media.Source.Path, "tracks", $"{media.VideoTrackId}:{raw}"
        }, AllowWarnings: true, OutputLine: new MkvProgress(progress, Guid.Empty, "Extracting video", media.Source.Path).Report), token);
        progress?.Report(new(Guid.Empty, "Analyzing RPU metadata", media.Source.Path));
        return await AnalyzeRawAsync(raw, method, workspace, token);
    }

    private async Task<RpuEvidence> AnalyzeStreamAsync(MediaInfo media, ITemporaryWorkspace workspace, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        int lengthSize = HevcAnnexB.LengthSize(media.IdentificationJson, media.VideoTrackId);
        double streamLength = Math.Max(1, HevcAnnexB.StreamLength(media));
        var streaming = new PhasedProgress(progress, Guid.Empty, "Streaming RPU extraction", media.Source.Path, 1);
        streaming.Report(0, 0);
        string rpu = workspace.File("inspection.rpu");
        File.Delete(rpu);
        File.Delete(workspace.File("rpu.json"));
        await processes.PipeAsync(new(tools.GetPath(NativeTool.FFmpeg), new[]
        {
            "-nostdin", "-v", "error", "-i", media.Source.Path, "-map", "0:v:0", "-c:v", "copy", "-an", "-sn", "-dn", "-f", "data", "-"
        }, OutputRelay: (input, output, cancellation) => HevcAnnexB.CopyAsync(input, output, lengthSize, cancellation, read => streaming.Report(0, read / streamLength))),
            new(tools.GetPath(NativeTool.DoviTool), new[] { "extract-rpu", "-", "-o", "inspection.rpu" }, workspace.DirectoryPath), token);
        progress?.Report(new(Guid.Empty, "Analyzing RPU metadata", media.Source.Path));
        return await ExportRpuAsync(AnalysisMethod.FullRpu, workspace, token);
    }

    private async Task<RpuEvidence> AnalyzeBrightnessAsync(string raw, long frames, PhasedProgress measuring, ITemporaryWorkspace workspace, int brightnessMarginNits, CancellationToken cancellationToken)
    {
        string baseLayer = workspace.File("inspection-bl.hevc");
        string enhancementLayer = workspace.File("inspection-el.hevc");
        logger.LogInformation("Deep inspection: extracting the HDR10 base layer");
        await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[]
        {
            "demux", "-i", raw, "-b", baseLayer, "-e", enhancementLayer
        }), cancellationToken);
        File.Delete(enhancementLayer);
        measuring.Report(1, 0);
        // Require explicit HDR10 signaling; never guess range or apply a PQ transform to SDR.
        var color = await processes.RunAsync(new(tools.GetPath(NativeTool.FFprobe), new[]
        {
            "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=color_transfer,color_primaries,color_space,color_range", "-of", "json", baseLayer
        }), cancellationToken);
        string range = DeepInspectionParser.ReadHdr10Range(color.Output);
        logger.LogInformation("Deep inspection: decoding every base-layer frame for luminance measurement");
        await processes.RunAsync(new(tools.GetPath(NativeTool.FFmpeg), new[]
        {
            "-nostdin", "-v", "error", "-xerror", "-err_detect", "explode", "-i", baseLayer, "-map", "0:v:0", "-an", "-sn", "-dn", "-vf", DeepInspectionParser.Filter(range), "-fps_mode", "passthrough", "-progress", "pipe:1", "-nostats", "-f", "null", "-"
        }, workspace.DirectoryPath, OutputLine: line =>
        {
            if (FFmpegProgress.TryParseFrame(line, out long frame))
            {
                measuring.Report(1, (double)frame / frames);
            }
        }), cancellationToken);
        await using var rpu = File.OpenRead(workspace.File("rpu.json"));
        using var peaks = File.OpenText(workspace.File("brightness.txt"));
        return await DeepInspectionParser.CompareAsync(rpu, peaks, cancellationToken, brightnessMarginNits);
    }

    private async Task<RpuEvidence> AnalyzeRawAsync(string raw, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken, string rpuName = "inspection.rpu", string jsonName = "rpu.json")
    {
        string rpu = workspace.File(rpuName);
        string json = workspace.File(jsonName);
        File.Delete(rpu);
        File.Delete(json);
        await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[]
        {
            "extract-rpu", raw, "-o", rpu
        }), cancellationToken);
        return await ExportRpuAsync(method, workspace, cancellationToken, rpuName, jsonName);
    }

    private async Task<RpuEvidence> ExportRpuAsync(AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken, string rpuName = "inspection.rpu", string jsonName = "rpu.json")
    {
        string json = workspace.File(jsonName);
        // Relative export destination avoids dovi_tool's comma-separated option grammar interpreting path punctuation.
        await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[]
        {
            "export", "-i", rpuName, "-d", $"all={jsonName}"
        }, workspace.DirectoryPath), cancellationToken);
        await using var stream = File.OpenRead(json);
        return await RpuParser.ParseAsync(stream, method, cancellationToken);
    }

    internal async Task<long> CountFramesAsync(string path, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(new(tools.GetPath(NativeTool.FFprobe), new[]
        {
            "-v", "error", "-select_streams", "v:0", "-count_packets", "-show_entries", "stream=nb_read_packets", "-of", "default=noprint_wrappers=1:nokey=1", path
        }), cancellationToken);
        if (!long.TryParse(result.Output.Trim(), CultureInfo.InvariantCulture, out long count) || count <= 0)
        {
            throw new InvalidDataException("Video packet count is unavailable; verification cannot succeed.");
        }

        return count;
    }
}
