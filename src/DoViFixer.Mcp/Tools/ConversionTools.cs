using System.ComponentModel;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Conversion;
using DoViFixer.Mcp.Jobs;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DoViFixer.Mcp.Tools;
[McpServerToolType]
public sealed class ConversionTools(ConversionPlanner planner, BatchConversionService conversion, JobRegistry jobs, PlanRegistry plans, ILogger<ConversionTools> logger)
{
    [McpServerTool(Name = "plan_conversion", Title = "Plan conversion", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Analyze files or folders and prepare exact conversion plans from Dolby Vision Profile 7 to Profile 8.1 (default) or HDR10, without converting anything. Show the user every plan (input, output, verdict, decision, warning) and get their approval before calling start_conversion with the plan ids. FEL files are planned only when includeSimpleFel or forceComplexFel allow them; their enhancement-layer picture data is lost. Runs as a job like scan_media.")]
    public async Task<string> PlanConversion(
        [Description("Absolute paths of media files or folders.")] string[] paths,
        [Description("Subfolder depth to include, 0 to 100.")] int recursiveDepth = 0,
        [Description("Produce HDR10 instead of Dolby Vision Profile 8.1.")] bool hdr10 = false,
        [Description("Folder for the converted files. By default output goes beside the source as \" - DV P8.1.mkv\" or \" - HDR10.mkv\".")] string? outputDirectory = null,
        [Description("Temporary working folder. Defaults to the saved setting.")] string? temporaryDirectory = null,
        [Description("Include Simple FEL files.")] bool includeSimpleFel = false,
        [Description("Permit detected Complex FEL files. Failed or unknown analysis stays blocked.")] bool forceComplexFel = false,
        [Description("Save a .dovi enhancement-layer archive before converting, so Profile 7 can be restored later.")] bool createBackup = false,
        [Description("Force disk extraction instead of streaming.")] bool safe = false,
        [Description("Replace the original: rename it to .bak.dovi_convert, reuse its file name for the output, and delete that backup after verification.")] bool replaceOriginal = false,
        CancellationToken cancellationToken = default)
    {
        if (paths.Length == 0)
        {
            throw new McpException("Pass at least one path.");
        }

        ToolArguments.Depth(recursiveDepth);
        var request = new ConversionRequest(paths[0], recursiveDepth, hdr10 ? ConversionTarget.Hdr10 : ConversionTarget.Profile81, outputDirectory, temporaryDirectory, includeSimpleFel, forceComplexFel, createBackup, safe, replaceOriginal, paths[1..]);
        var job = await jobs.StartAsync("PlanConversion", paths[0], async (progress, token) =>
        {
            var planned = await planner.PlanAsync(request, progress, token);
            foreach (var plan in planned.Plans)
            {
                plans.Add(plan.Id, plan);
                OperationLog.Audit(logger, "Convert", plan.Analysis.Media.Source.Path, "PlanPresented", plan.Id);
            }

            return new
            {
                Plans = planned.Plans.Select(ToolViews.Plan).ToArray(),
                planned.Skipped
            };
        }, cancellationToken);
        return ToolViews.Json(job);
    }

    [McpServerTool(Name = "start_conversion", Title = "Start conversion", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Convert files using plans from plan_conversion, after the user approved them. Each plan runs once. Conversion can take a long time: this returns a job to follow with get_job, or stop with cancel_job.")]
    public async Task<string> StartConversion(
        [Description("Plan ids the user approved.")] string[] planIds,
        CancellationToken cancellationToken = default)
    {
        var approved = plans.Take<ConversionPlan>(ToolArguments.PlanIds(planIds));
        foreach (var plan in approved)
        {
            OperationLog.Audit(logger, "Approval", plan.Analysis.Media.Source.Path, "ApprovedByAssistantUser", plan.Id);
        }

        var job = await jobs.StartAsync("Convert", approved[0].Analysis.Media.Source.Path, async (progress, token) => await conversion.ExecuteAsync(approved, progress, token), cancellationToken);
        return ToolViews.Json(job);
    }
}
