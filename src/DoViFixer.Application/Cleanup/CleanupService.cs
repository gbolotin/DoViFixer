using System.Diagnostics;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Activity;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Media;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Cleanup;
public sealed record CleanupPlan(Guid Id, IReadOnlyList<FileIdentity> Files);
/// <summary>Deletes approved backups. Activity shows one summary entry, plus an entry for each file it could not remove.</summary>
public sealed class CleanupService(IFileDiscovery discovery, IFileOperations files, ILogger<CleanupService> logger, IActivityFeed activity, ISettingsStore? settings = null)
{
    public CleanupPlan Plan(string input, int recursiveDepth) => new(Guid.NewGuid(), Array.AsReadOnly(discovery.Discover(input, recursiveDepth, cleanup: true).Select(files.Identify).ToArray()));
    public Task<BatchResult> ExecuteAsync(CleanupPlan approvedPlan, CancellationToken cancellationToken) => OperationLog.RunAsync(logger, "Cleanup", approvedPlan.Id, null, () => ExecuteCoreAsync(approvedPlan, cancellationToken), cancellationToken, result => result.Status);
    private async Task<BatchResult> ExecuteCoreAsync(CleanupPlan approvedPlan, CancellationToken cancellationToken)
    {
        var results = new List<OperationItemResult>();
        long started = Stopwatch.GetTimestamp();
        ActivityEntry Summary() => ActivityText.BatchFinished(approvedPlan.Id, ActivityOperation.Cleanup, approvedPlan.Files.Count, results, Stopwatch.GetElapsedTime(started));
        activity.Report(ActivityText.BatchStarted(approvedPlan.Id, ActivityOperation.Cleanup, approvedPlan.Files.Count));
        try
        {
            await DeleteAsync(approvedPlan, results, cancellationToken);
        }
        catch
        {
            activity.Report(Summary());
            throw;
        }

        var summary = Summary();
        activity.Report(summary);
        return new(results.AsReadOnly()) { Activity = summary };
    }

    private async Task DeleteAsync(CleanupPlan approvedPlan, List<OperationItemResult> results, CancellationToken cancellationToken)
    {
        bool recycle = settings is not null && (await settings.ReadAsync(cancellationToken)).RecycleDeletedOriginalsAndBackups;
        foreach (var file in approvedPlan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long started = Stopwatch.GetTimestamp();
            try
            {
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Started", approvedPlan.Id);
                await files.DeleteAsync(file, cancellationToken, recycle);
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Completed", approvedPlan.Id);
                results.Add(new(file.Path, OperationStatus.Completed, null, recycle ? "Moved approved backup to Recycle Bin." : "Deleted approved backup."));
            }
            catch (OperationCanceledException)
            {
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Interrupted", approvedPlan.Id);
                throw;
            }
            catch (Exception ex)when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Backup deletion failed for {Input}", file.Path);
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Failed", approvedPlan.Id);
                results.Add(new(file.Path, OperationStatus.Failed, null, ex.Message));
                activity.Report(ActivityText.Finished(Guid.NewGuid(), ActivityOperation.Cleanup, results[^1], Stopwatch.GetElapsedTime(started)));
            }

            OperationLog.Result(logger, results[^1]);
        }
    }
}
