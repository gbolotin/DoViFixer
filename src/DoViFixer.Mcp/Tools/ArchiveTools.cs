using System.ComponentModel;
using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Restore;
using DoViFixer.Mcp.Jobs;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DoViFixer.Mcp.Tools;
[McpServerToolType]
public sealed class ArchiveTools(BackupService backup, RestoreService restore, CleanupService cleanup, JobRegistry jobs, PlanRegistry plans, ILogger<ArchiveTools> logger)
{
    [McpServerTool(Name = "plan_backup", Title = "Plan backup", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Prepare a .dovi archive of a Profile 7 file's enhancement layer and restoration metadata, without creating it. Show the user the plan and get approval before start_backup. The original is kept.")]
    public async Task<string> PlanBackup(
        [Description("Absolute path of the Profile 7 media file.")] string path,
        [Description("Folder for the archive. Defaults to beside the source.")] string? outputDirectory = null,
        [Description("Temporary working folder. Defaults to the saved setting.")] string? temporaryDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var job = await jobs.StartAsync("PlanBackup", path, async (_, token) =>
        {
            var plan = await backup.PlanAsync(path, outputDirectory, temporaryDirectory, token);
            plans.Add(plan.Id, plan);
            OperationLog.Audit(logger, "Backup", path, "PlanPresented", plan.Id);
            return new
            {
                PlanId = plan.Id,
                Input = plan.Media.Source.Path,
                Archive = plan.Output,
                plan.ScratchBytes
            };
        }, cancellationToken);
        return ToolViews.Json(job);
    }

    [McpServerTool(Name = "start_backup", Title = "Start backup", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create the archive from a plan_backup plan the user approved. Returns a job to follow with get_job.")]
    public async Task<string> StartBackup([Description("Plan id the user approved.")] string planId, CancellationToken cancellationToken = default)
    {
        var plan = plans.Take<BackupPlan>(ToolArguments.PlanIds([planId]))[0];
        OperationLog.Audit(logger, "Approval", plan.Media.Source.Path, "ApprovedByAssistantUser", plan.Id);
        var job = await jobs.StartAsync("Backup", plan.Media.Source.Path, async (progress, token) => await backup.ExecuteAsync(plan, progress, token), cancellationToken);
        return ToolViews.Json(job);
    }

    [McpServerTool(Name = "plan_restore", Title = "Plan restore", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Prepare restoring Profile 7 from a converted base file and its .dovi enhancement archive, without restoring. Show the user the plan and get approval before start_restore. Upstream EL-only archives are accepted, but their pairing cannot be verified.")]
    public async Task<string> PlanRestore(
        [Description("Absolute path of the converted base file.")] string path,
        [Description("Absolute path of the .dovi archive. Defaults to the archive beside the base file with the same name.")] string? archive = null,
        [Description("Folder for the restored file. Defaults to beside the base file.")] string? outputDirectory = null,
        [Description("Temporary working folder. Defaults to the saved setting.")] string? temporaryDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var job = await jobs.StartAsync("PlanRestore", path, async (_, token) =>
        {
            var plan = await restore.PlanAsync(path, archive ?? Path.ChangeExtension(path, ".dovi"), outputDirectory, temporaryDirectory, true, token);
            plans.Add(plan.Id, plan);
            OperationLog.Audit(logger, "Restore", path, "PlanPresented", plan.Id);
            return new
            {
                PlanId = plan.Id,
                Input = plan.Media.Source.Path,
                Archive = plan.Archive.Path,
                plan.Output,
                plan.ScratchBytes
            };
        }, cancellationToken);
        return ToolViews.Json(job);
    }

    [McpServerTool(Name = "start_restore", Title = "Start restore", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Restore Profile 7 from a plan_restore plan the user approved. Returns a job to follow with get_job.")]
    public async Task<string> StartRestore([Description("Plan id the user approved.")] string planId, CancellationToken cancellationToken = default)
    {
        var plan = plans.Take<RestorePlan>(ToolArguments.PlanIds([planId]))[0];
        OperationLog.Audit(logger, "Approval", plan.Media.Source.Path, "ApprovedByAssistantUser", plan.Id);
        var job = await jobs.StartAsync("Restore", plan.Media.Source.Path, async (progress, token) => await restore.ExecuteAsync(plan, progress, token), cancellationToken);
        return ToolViews.Json(job);
    }

    [McpServerTool(Name = "list_backups", Title = "List backups", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List .dovi archives and .bak.dovi_convert backups in a file or folder. Returns a plan id that delete_backups accepts if the user wants exactly these files deleted.")]
    public string ListBackups(
        [Description("Absolute path of a file or folder.")] string path,
        [Description("Subfolder depth to include, 0 to 100.")] int recursiveDepth = 0)
    {
        ToolArguments.Depth(recursiveDepth);
        var plan = cleanup.Plan(path, recursiveDepth);
        plans.Add(plan.Id, plan);
        foreach (var file in plan.Files)
        {
            OperationLog.Audit(logger, "DeleteBackup", file.Path, "PlanPresented", plan.Id);
        }

        return ToolViews.Json(new
        {
            PlanId = plan.Id,
            plan.Files
        });
    }

    [McpServerTool(Name = "delete_backups", Title = "Delete backups", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Permanently delete exactly the files a list_backups plan showed. Only call this when the user explicitly asked to delete those backups; deleted archives cannot restore Profile 7 any more.")]
    public async Task<string> DeleteBackups([Description("Plan id from list_backups.")] string planId, CancellationToken cancellationToken = default)
    {
        var plan = plans.Take<CleanupPlan>(ToolArguments.PlanIds([planId]))[0];
        OperationLog.Audit(logger, "DeleteApproval", "Backup cleanup", "ApprovedByAssistantUser", plan.Id);
        return ToolViews.Json(await cleanup.ExecuteAsync(plan, cancellationToken));
    }
}
