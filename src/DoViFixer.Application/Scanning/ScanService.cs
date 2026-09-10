using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Scanning;

public sealed record ScanItem(string Path, MediaAnalysis? Analysis, string? Error, MediaAnalysis? InitialAnalysis = null);
public sealed class ScanService(IFileDiscovery discovery, InspectionService inspection, ILogger<ScanService> logger)
{
    public Task<IReadOnlyList<ScanItem>> ScanAsync(string input, int recursiveDepth, string? temporaryDirectory,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken, IProgress<ScanItem>? completed = null,
        bool inspectSimple = false)
    {
        var id = Guid.NewGuid();
        return OperationLog.RunAsync(logger, "Scan", id, input,
            () => ScanCoreAsync(input, recursiveDepth, temporaryDirectory, progress, id, cancellationToken, completed, inspectSimple), cancellationToken,
            results => results.Count == 0 ? OperationStatus.Skipped
                : results.All(Failed) ? OperationStatus.Failed
                : results.Any(Failed) ? OperationStatus.Partial : OperationStatus.Completed);
    }

    private static bool Failed(ScanItem item) => item.Error is not null || item.Analysis?.Verdict == AnalysisVerdict.AnalysisFailed;

    private async Task<IReadOnlyList<ScanItem>> ScanCoreAsync(string input, int recursiveDepth, string? temporaryDirectory,
        IProgress<OperationProgress>? progress, Guid id, CancellationToken cancellationToken, IProgress<ScanItem>? completed, bool inspectSimple)
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
        if (inspectSimple)
        {
            var candidates = results.Select((item, index) => (item, index))
                .Where(candidate => candidate.item.Error is null && candidate.item.Analysis?.Verdict == AnalysisVerdict.SimpleFel).ToArray();
            for (int i = 0; i < candidates.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (item, index) = candidates[i];
                progress?.Report(new(id, $"Deep inspecting Simple FEL {i + 1}/{candidates.Length}", item.Path));
                try
                {
                    var analysis = await inspection.InspectAsync(item.Path, AnalysisMethod.DeepInspection, temporaryDirectory, cancellationToken);
                    results[index] = new(item.Path, analysis, null, item.Analysis);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not DependencyNotReadyException)
                {
                    logger.LogError(ex, "Automatic deep inspection failed for {Input}", item.Path);
                    results[index] = new(item.Path, null, ex.Message, item.Analysis);
                }
                completed?.Report(results[index]);
            }
        }
        return results;
    }
}
