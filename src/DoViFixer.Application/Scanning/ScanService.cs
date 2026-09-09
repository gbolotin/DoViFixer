using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Scanning;

public sealed record ScanItem(string Path, MediaAnalysis? Analysis, string? Error);
public sealed class ScanService(IFileDiscovery discovery, InspectionService inspection, ILogger<ScanService> logger)
{
    public Task<IReadOnlyList<ScanItem>> ScanAsync(string input, int recursiveDepth, string? temporaryDirectory,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken, IProgress<ScanItem>? completed = null)
    {
        var id = Guid.NewGuid();
        return OperationLog.RunAsync(logger, "Scan", id, input,
            () => ScanCoreAsync(input, recursiveDepth, temporaryDirectory, progress, id, cancellationToken, completed), cancellationToken,
            results => results.Count == 0 ? OperationStatus.Skipped
                : results.All(Failed) ? OperationStatus.Failed
                : results.Any(Failed) ? OperationStatus.Partial : OperationStatus.Completed);
    }

    private static bool Failed(ScanItem item) => item.Error is not null || item.Analysis?.Verdict == AnalysisVerdict.AnalysisFailed;

    private async Task<IReadOnlyList<ScanItem>> ScanCoreAsync(string input, int recursiveDepth, string? temporaryDirectory,
        IProgress<OperationProgress>? progress, Guid id, CancellationToken cancellationToken, IProgress<ScanItem>? completed)
    {
        var results = new List<ScanItem>();
        foreach (string file in discovery.Discover(input, recursiveDepth))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(id, "Analyzing", file));
            try
            {
                results.Add(new(file, await inspection.InspectAsync(file, AnalysisMethod.SampledRpu, temporaryDirectory, cancellationToken), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not DependencyNotReadyException)
            {
                logger.LogError(ex, "Scan failed for {Input}", file);
                results.Add(new(file, null, ex.Message));
            }
            completed?.Report(results[^1]);
        }
        return results;
    }
}
