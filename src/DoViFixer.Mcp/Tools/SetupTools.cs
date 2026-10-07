using System.ComponentModel;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;
using DoViFixer.Mcp.Jobs;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DoViFixer.Mcp.Tools;
[McpServerToolType]
public sealed class SetupTools(DependencyService dependencies, SettingsService settings, UpdateService updates, JobRegistry jobs, PlanRegistry plans, ILogger<SetupTools> logger)
{
    [McpServerTool(Name = "check_dependencies", Title = "Check dependencies", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Check whether the native tools DoViFixer needs (FFmpeg, FFprobe, MkvMerge, MkvExtract, MediaInfo, DoviTool) are installed and compatible.")]
    public async Task<string> CheckDependencies([Description("Tools to check. Omit to check all.")] string[]? tools = null, CancellationToken cancellationToken = default) => ToolViews.Json(await dependencies.CheckAsync(ToolArguments.Tools(tools), cancellationToken));

    [McpServerTool(Name = "plan_dependency_install", Title = "Plan dependency installation", ReadOnly = true, Destructive = false, OpenWorld = true)]
    [Description("Prepare an installation plan for missing, unusable or incompatible native tools, without installing anything. Show the user each item's tools, source, version, destination, scope and elevation requirement, and get their approval before start_dependency_install.")]
    public async Task<string> PlanDependencyInstall([Description("Tools to install. Omit for all tools DoViFixer needs.")] string[]? tools = null, CancellationToken cancellationToken = default)
    {
        var report = await dependencies.CheckAsync(ToolArguments.Tools(tools), cancellationToken);
        if (report.Ready)
        {
            return ToolViews.Json(new
            {
                Status = "AllReady",
                Dependencies = report
            });
        }

        var plan = await dependencies.PrepareAsync(report, cancellationToken);
        plans.Add(plan.Id, plan);
        foreach (var item in plan.Items)
        {
            OperationLog.Audit(logger, "InstallDependency", item.Id, "PlanPresented", plan.Id);
        }

        return ToolViews.Json(new
        {
            PlanId = plan.Id,
            plan.Items,
            plan.Unavailable,
            plan.Replacements,
            Dependencies = report
        });
    }

    [McpServerTool(Name = "start_dependency_install", Title = "Install dependencies", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description("Download and install the tools in a plan_dependency_install plan the user approved, then save the validated tool paths. Returns a job to follow with get_job.")]
    public async Task<string> StartDependencyInstall([Description("Plan id the user approved.")] string planId, CancellationToken cancellationToken = default)
    {
        var plan = plans.Take<InstallationPlan>(ToolArguments.PlanIds([planId]))[0];
        OperationLog.Audit(logger, "Approval", "Install this exact dependency plan", "ApprovedByAssistantUser", plan.Id);
        var job = await jobs.StartAsync("InstallDependencies", null, async (progress, token) => await dependencies.InstallAsync(plan, progress, token), cancellationToken);
        return ToolViews.Json(job);
    }

    [McpServerTool(Name = "get_settings", Title = "Get settings", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Show DoViFixer's saved settings: temporary and output folders, conversion preferences and native tool paths.")]
    public async Task<string> GetSettings(CancellationToken cancellationToken = default) => ToolViews.Json(await settings.ReadAsync(cancellationToken));

    [McpServerTool(Name = "set_tool_path", Title = "Set tool path", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Use a specific executable for a native tool. The path is validated before it is saved.")]
    public async Task<string> SetToolPath(
        [Description("FFmpeg, FFprobe, MkvMerge, MkvExtract, MediaInfo or DoviTool.")] string tool,
        [Description("Absolute path of the executable.")] string path,
        CancellationToken cancellationToken = default)
    {
        await settings.SetToolAsync(ToolArguments.Tool(tool), path, cancellationToken);
        return "Validated tool path saved.";
    }

    [McpServerTool(Name = "set_temporary_directory", Title = "Set temporary folder", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Save the folder DoViFixer uses for temporary files. Missing folders are created and write access is checked.")]
    public async Task<string> SetTemporaryDirectory([Description("Absolute folder path.")] string path, CancellationToken cancellationToken = default)
    {
        await settings.SetTemporaryDirectoryAsync(path, cancellationToken);
        return "Temporary folder saved.";
    }

    [McpServerTool(Name = "check_for_update", Title = "Check for update", ReadOnly = true, Destructive = false, OpenWorld = true)]
    [Description("Compare this DoViFixer version with the latest release on GitHub. Nothing is downloaded or installed.")]
    public async Task<string> CheckForUpdate(CancellationToken cancellationToken = default) => ToolViews.Json(await updates.CheckAsync(ApplicationVersion.Of(typeof(SetupTools).Assembly), cancellationToken));
}
