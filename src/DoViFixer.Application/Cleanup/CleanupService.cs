using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Cleanup;

public sealed record CleanupPlan(Guid Id, IReadOnlyList<FileIdentity> Files);
public sealed class CleanupService(IFileDiscovery discovery, IFileOperations files, ILogger<CleanupService> logger)
{
    public CleanupPlan Plan(string input, int recursiveDepth) => new(Guid.NewGuid(),
        Array.AsReadOnly(discovery.Discover(input, recursiveDepth, cleanup: true).Select(files.Identify).ToArray()));

    public Task<BatchResult> ExecuteAsync(CleanupPlan approvedPlan, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "Cleanup", approvedPlan.Id, null,
            () => ExecuteCoreAsync(approvedPlan, cancellationToken), cancellationToken, result => result.Status);

    private async Task<BatchResult> ExecuteCoreAsync(CleanupPlan approvedPlan, CancellationToken cancellationToken)
    {
        var results = new List<FileResult>();
        foreach (var file in approvedPlan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Started", approvedPlan.Id);
                await files.DeleteAsync(file, cancellationToken);
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Completed", approvedPlan.Id);
                results.Add(new(file.Path, OperationStatus.Completed, null, "Deleted approved backup."));
            }
            catch (OperationCanceledException)
            {
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Interrupted", approvedPlan.Id);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Backup deletion failed for {Input}", file.Path);
                OperationLog.Audit(logger, "DeleteBackup", file.Path, "Failed", approvedPlan.Id);
                results.Add(new(file.Path, OperationStatus.Failed, null, ex.Message));
            }
            OperationLog.Result(logger, results[^1]);
        }
        return new(results.AsReadOnly());
    }
}
