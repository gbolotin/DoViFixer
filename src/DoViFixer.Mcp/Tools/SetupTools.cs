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
public sealed class SetupTools(DependencyService dependencies, SettingsService settings, PresetService presetService, UpdateService updates, JobRegistry jobs, PlanRegistry plans, ILogger<SetupTools> logger)
{
    [McpServerTool(Name = "check_dependencies", Title = "Check dependencies", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Check whether the native tools DoViFixer needs (FFmpeg, FFprobe, MkvMerge, MkvExtract, MkvPropEdit, MediaInfo, DoviTool) are installed and compatible.")]
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

    [McpServerTool(Name = "list_presets", Title = "List presets", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List built-in and saved presets with every value each one sets, and name the preset the current settings match. A preset sets scan, conversion and safety settings; folders, worker counts, tool paths and appearance are not part of it.")]
    public async Task<string> ListPresets(CancellationToken cancellationToken = default)
    {
        var list = await presetService.ListAsync(cancellationToken);
        var current = await settings.ReadAsync(cancellationToken);
        return ToolViews.Json(new
        {
            ActivePreset = PresetService.FindActive(list, current)?.Name,
            current.LastAppliedPreset,
            Presets = list
        });
    }

    [McpServerTool(Name = "plan_preset", Title = "Plan applying a preset", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List every setting a preset would change, without changing anything. Show the user each change, especially those marked IncreasesRisk, and get their approval before apply_preset. Changed settings apply to later scans and conversions in DoViFixer and its app.")]
    public async Task<string> PlanPreset([Description("Preset name, as list_presets shows it.")] string name, CancellationToken cancellationToken = default)
    {
        var preset = await presetService.FindAsync(name, cancellationToken);
        var changes = await presetService.PreviewAsync(preset, cancellationToken);
        if (changes.Count == 0)
        {
            return ToolViews.Json(new
            {
                Status = "AlreadyApplied",
                Preset = preset.Name
            });
        }

        var plan = new PresetPlan(Guid.NewGuid(), preset, changes);
        plans.Add(plan.Id, plan);
        OperationLog.Audit(logger, "ApplyPreset", preset.Name, "PlanPresented", plan.Id);
        return ToolViews.Json(new
        {
            PlanId = plan.Id,
            Preset = preset.Name,
            preset.Description,
            Changes = changes
        });
    }

    [McpServerTool(Name = "apply_preset", Title = "Apply preset", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Apply the preset in a plan_preset plan the user approved. Only settings change; no file is converted or deleted.")]
    public async Task<string> ApplyPreset([Description("Plan id the user approved.")] string planId, CancellationToken cancellationToken = default)
    {
        var plan = plans.Take<PresetPlan>(ToolArguments.PlanIds([planId]))[0];
        OperationLog.Audit(logger, "Approval", "Apply this preset", "ApprovedByAssistantUser", plan.Id);
        var changes = await presetService.ApplyAsync(plan.Preset, cancellationToken);
        return ToolViews.Json(new
        {
            Status = "Applied",
            Preset = plan.Preset.Name,
            Changes = changes
        });
    }

    /// <summary>A preset change shown to the user, waiting for approval.</summary>
    public sealed record PresetPlan(Guid Id, SettingsPreset Preset, IReadOnlyList<PresetChange> Changes);

    [McpServerTool(Name = "set_tool_path", Title = "Set tool path", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Use a specific executable for a native tool. The path is validated before it is saved.")]
    public async Task<string> SetToolPath(
        [Description("FFmpeg, FFprobe, MkvMerge, MkvExtract, MkvPropEdit, MediaInfo or DoviTool.")] string tool,
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
