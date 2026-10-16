using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Reports;

public sealed record ReportInput(string Path, string Status, MediaAnalysis? Analysis = null, string? Error = null, OperationItemResult? Operation = null);

public sealed record ReportEntry(
    string Path, string Status, string? Error, long? SourceBytes, DateTime? SourceLastWriteUtc,
    DolbyVisionProfile? Profile, EnhancementLayer? Layer, AnalysisMethod? Method, AnalysisVerdict? Verdict,
    string? Explanation, bool? EvidenceComplete, long? RpuFrames, int? SuccessfulSamples, int? RequestedSamples,
    double? MaxCllNits, double? RpuPeakNits, double? BaseLayerPeakNits, long? ComparedFrames,
    long? ExpandedFrames, long? MetadataFreeTailFrames, string? EvidenceLimitations,
    OperationStatus? OperationStatus, string? OperationMessage, string? OutputPath);

public sealed record ReportSnapshot(int SchemaVersion, DateTimeOffset CreatedAtUtc, IReadOnlyList<ReportEntry> Entries);

public sealed class ReportService(TimeProvider time, Abstractions.IReportWriter writer)
{
    public ReportSnapshot CreateSnapshot(IEnumerable<ReportInput> inputs) => new(1, time.GetUtcNow(), Array.AsReadOnly(inputs.Select(CreateEntry).ToArray()));

    public Task ExportAsync(string destination, ReportSnapshot snapshot, CancellationToken cancellationToken) => writer.WriteAsync(destination, snapshot, cancellationToken);

    private static ReportEntry CreateEntry(ReportInput input)
    {
        var analysis = input.Analysis;
        var evidence = analysis?.Evidence;
        bool? complete = evidence is null ? null : evidence.Error is null && evidence.Frames > 0
            && evidence.SuccessfulSamples == evidence.RequestedSamples
            && (evidence.Method != AnalysisMethod.DeepInspection || evidence.Brightness?.ComparedFrames == evidence.Frames)
            && (evidence.MetadataFreeTail is null || MediaClassifier.HasVerifiedTail(evidence));
        string? limits = evidence?.Method switch
        {
            AnalysisMethod.SampledRpu => "Sampled RPU metadata; unsampled frames were not inspected. Brightness classification uses metadata, not decoded base-layer frames.",
            AnalysisMethod.FullRpu => "Full RPU metadata; brightness classification uses metadata, not decoded base-layer frames.",
            AnalysisMethod.DeepInspection => "Decoded base-layer brightness was compared with RPU metadata; this does not prove playback appearance.",
            _ => null
        };
        return new(input.Path, input.Status, input.Error ?? evidence?.Error, analysis?.Media.Source.Length,
            analysis?.Media.Source.LastWriteUtc, analysis?.Media.Profile, evidence?.Layer, evidence?.Method,
            analysis?.Verdict, analysis?.Reason, complete, evidence?.Frames, evidence?.SuccessfulSamples,
            evidence?.RequestedSamples, analysis?.Media.MaxCll, evidence?.PeakNits, evidence?.Brightness?.BaseLayerPeakNits,
            evidence?.Brightness?.ComparedFrames, evidence?.Brightness?.ExpandedFrames,
            evidence is not null && MediaClassifier.HasVerifiedTail(evidence) ? evidence.MetadataFreeTail!.TailFrames : null,
            limits, input.Operation?.Status, input.Operation?.Message, input.Operation?.Output);
    }
}
