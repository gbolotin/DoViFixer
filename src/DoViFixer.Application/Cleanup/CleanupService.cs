using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Cleanup;

public sealed record CleanupPlan(Guid Id, IReadOnlyList<FileIdentity> Files);
public sealed class CleanupService(IFileDiscovery discovery, IFileOperations files)
{
    public CleanupPlan Plan(string input, int recursiveDepth) => new(Guid.NewGuid(),
        Array.AsReadOnly(discovery.Discover(input, recursiveDepth, cleanup: true).Select(files.Identify).ToArray()));

    public async Task<BatchResult> ExecuteAsync(CleanupPlan approvedPlan, CancellationToken cancellationToken)
    {
        var results = new List<FileResult>();
        foreach (var file in approvedPlan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await files.DeleteAsync(file, cancellationToken);
                results.Add(new(file.Path, OperationStatus.Completed, null, "Deleted approved backup."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new(file.Path, OperationStatus.Failed, null, ex.Message));
            }
        }
        return new(results.AsReadOnly());
    }
}
