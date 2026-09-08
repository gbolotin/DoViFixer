using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.Application.Scanning;

public sealed record ScanItem(string Path, MediaAnalysis? Analysis, string? Error);
public sealed class ScanService(IFileDiscovery discovery, InspectionService inspection)
{
    public async Task<IReadOnlyList<ScanItem>> ScanAsync(string input, int recursiveDepth, string? temporaryDirectory,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var results = new List<ScanItem>();
        var id = Guid.NewGuid();
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
                results.Add(new(file, null, ex.Message));
            }
        }
        return results;
    }
}
