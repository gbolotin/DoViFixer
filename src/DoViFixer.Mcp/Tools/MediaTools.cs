using System.ComponentModel;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Scanning;
using DoViFixer.Domain.Analysis;
using DoViFixer.Mcp.Jobs;
using ModelContextProtocol.Server;

namespace DoViFixer.Mcp.Tools;
[McpServerToolType]
public sealed class MediaTools(ScanService scan, InspectionService inspection, JobRegistry jobs)
{
    [McpServerTool(Name = "scan_media", Title = "Scan media", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Find media files in a file or folder, identify their Dolby Vision profile and classify Profile 7 files (MEL, Simple FEL, Complex FEL) as conversion candidates. Runs as a job: returns the result when it finishes quickly, otherwise a running job to follow with get_job.")]
    public async Task<string> ScanMedia(
        [Description("Absolute path of a media file or folder.")] string path,
        [Description("Subfolder depth to include, 0 to 100. 0 scans only the folder itself.")] int recursiveDepth = 0,
        [Description("Return only conversion candidates and failures.")] bool candidatesOnly = false,
        [Description("Deep-inspect every Simple FEL candidate after scanning. Decodes every base-layer frame; slow and needs temporary disk space.")] bool inspectSimple = false,
        [Description("Temporary working folder. Defaults to the saved setting.")] string? temporaryDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ToolArguments.Depth(recursiveDepth);
        var job = await jobs.StartAsync("Scan", path, async (progress, token) =>
        {
            var results = await scan.ScanAsync(path, recursiveDepth, temporaryDirectory, progress, token, inspectSimple: inspectSimple);
            return results.Where(item => !candidatesOnly || ToolViews.IsCandidate(item)).Select(ToolViews.Scan).ToArray();
        }, cancellationToken);
        return ToolViews.Json(job);
    }

    [McpServerTool(Name = "inspect_media", Title = "Inspect media", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Analyze one media file in detail: Dolby Vision profile, enhancement layer, RPU evidence and verdict. Deep inspection compares every decoded base-layer frame with the RPU brightness metadata to look for FEL expansion; it is slow. Runs as a job like scan_media.")]
    public async Task<string> InspectMedia(
        [Description("Absolute path of the media file.")] string path,
        [Description("Run the full frame-by-frame deep inspection.")] bool deep = false,
        [Description("Temporary working folder. Defaults to the saved setting.")] string? temporaryDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var job = await jobs.StartAsync("Inspect", path, async (progress, token) => ToolViews.Analysis(await inspection.InspectAsync(path, deep ? AnalysisMethod.DeepInspection : AnalysisMethod.FullRpu, temporaryDirectory, token, progress)), cancellationToken);
        return ToolViews.Json(job);
    }
}
