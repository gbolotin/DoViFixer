using System.Globalization;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.MediaTools.DoviTool;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.MediaTools;

internal sealed class MediaProbe(IToolCatalog tools, IProcessRunner processes, IFileOperations files, ILogger<MediaProbe> logger) : IMediaProbe
{
    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var identity = files.Identify(path);
        var mkv = await processes.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), new[] { "-J", identity.Path }, AllowWarnings: true), cancellationToken);
        var mi = await processes.RunAsync(new(tools.GetPath(NativeTool.MediaInfo), new[] { "--Output=JSON", identity.Path }), cancellationToken);
        return MediaMetadataParser.Parse(identity, mkv.Output, mi.Output);
    }

    public async Task<RpuEvidence> AnalyzeAsync(MediaInfo media, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        try
        {
            if (method == AnalysisMethod.FullRpu)
            {
                string raw = workspace.File("inspection.hevc");
                await processes.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { media.Source.Path, "tracks", $"{media.VideoTrackId}:{raw}" }, AllowWarnings: true), cancellationToken);
                var evidence = await AnalyzeRawAsync(raw, method, workspace, cancellationToken);
                long frames = await CountFramesAsync(media.Source.Path, cancellationToken);
                return evidence.Frames == frames ? evidence : evidence with { Error = $"Full RPU count {evidence.Frames} differs from video packet count {frames}." };
            }
            if (media.DurationSeconds is null or <= 0)
            {
                return new(method, EnhancementLayer.Unknown, 0, null, 0, 10, "Duration is unavailable for sample selection.");
            }
            var samples = new List<RpuEvidence>();
            for (int i = 0; i < 10; i++)
            {
                string raw = workspace.File($"sample-{i}.hevc");
                try
                {
                    string time = (media.DurationSeconds.Value * (0.05 + i * 0.1)).ToString("F3", CultureInfo.InvariantCulture);
                    await processes.RunAsync(new(tools.GetPath(NativeTool.FFmpeg), new[] { "-nostdin", "-v", "error", "-ss", time,
                        "-i", media.Source.Path, "-map", "0:v:0", "-c:v", "copy", "-an", "-sn", "-dn", "-bsf:v", "hevc_mp4toannexb",
                        "-f", "hevc", "-t", "1", raw }), cancellationToken);
                    samples.Add(await AnalyzeRawAsync(raw, method, workspace, cancellationToken));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
                {
                    logger.LogDebug(ex, "RPU sample {SampleIndex} failed for {Input}", i, media.Source.Path);
                    // Missing sample coverage remains Unknown; never turn a failed probe into complex FEL.
                }
                finally
                {
                    File.Delete(raw);
                }
            }
            EnhancementLayer layer = samples.Any(s => s.Layer == EnhancementLayer.Fel) ? EnhancementLayer.Fel
                : samples.Count > 0 && samples.All(s => s.Layer == EnhancementLayer.Mel) ? EnhancementLayer.Mel : EnhancementLayer.Unknown;
            return new(method, layer, samples.Sum(s => s.Frames), samples.Select(s => s.PeakNits).Max(), samples.Count, 10);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RPU analysis failed for {Input}; method {Method}", media.Source.Path, method);
            return new(method, EnhancementLayer.Unknown, 0, null, 0, 1, ex.Message);
        }
    }

    private async Task<RpuEvidence> AnalyzeRawAsync(string raw, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        string rpu = workspace.File("inspection.rpu");
        string json = workspace.File("rpu.json");
        File.Delete(rpu);
        File.Delete(json);
        await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "extract-rpu", raw, "-o", rpu }), cancellationToken);
        // Relative export destination avoids dovi_tool's comma-separated option grammar interpreting path punctuation.
        await processes.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "export", "-i", "inspection.rpu", "-d", "all=rpu.json" }, workspace.DirectoryPath), cancellationToken);
        await using var stream = File.OpenRead(json);
        return await RpuParser.ParseAsync(stream, method, cancellationToken);
    }

    internal async Task<long> CountFramesAsync(string path, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(new(tools.GetPath(NativeTool.FFprobe), new[] { "-v", "error", "-select_streams", "v:0", "-count_packets",
            "-show_entries", "stream=nb_read_packets", "-of", "default=noprint_wrappers=1:nokey=1", path }), cancellationToken);
        if (!long.TryParse(result.Output.Trim(), CultureInfo.InvariantCulture, out long count) || count <= 0)
        {
            throw new InvalidDataException("Video packet count is unavailable; verification cannot succeed.");
        }
        return count;
    }
}
