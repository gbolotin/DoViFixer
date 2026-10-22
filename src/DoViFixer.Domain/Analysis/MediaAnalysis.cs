using DoViFixer.Domain.Media;

namespace DoViFixer.Domain.Analysis;
public enum AnalysisMethod
{
    MetadataOnly,
    SampledRpu,
    FullRpu,
    DeepInspection
}

public enum AnalysisVerdict
{
    NotApplicable,
    Mel,
    SimpleFel,
    ComplexFel,
    Unknown,
    AnalysisFailed,
    FelUnclassified
}

public sealed record MetadataFreeTail(long TotalFrames, long RpuFrames, string PositionHash)
{
    public long TailFrames => TotalFrames - RpuFrames;
}
public sealed record AnalysisOptions(double BrightnessMarginNits = 50, double? AssumedPeakNits = null, int ScanSampleCount = 10, double ScanSampleSeconds = 1, int MinimumScanCoveragePercent = 100, double InspectionPeakPercentile = 100)
{
    public int RequiredScanSamples(int requested) => Math.Max(1, (int)Math.Ceiling(requested * MinimumScanCoveragePercent / 100d));

    public void ValidateSampling()
    {
        if (ScanSampleCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ScanSampleCount));
        }
        if (!double.IsFinite(ScanSampleSeconds) || ScanSampleSeconds is < 0.1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(ScanSampleSeconds));
        }
        if (MinimumScanCoveragePercent is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumScanCoveragePercent));
        }
        if (!double.IsFinite(InspectionPeakPercentile) || InspectionPeakPercentile is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(InspectionPeakPercentile));
        }
    }
}
public sealed record RpuEvidence(AnalysisMethod Method, EnhancementLayer Layer, long Frames, double? PeakNits, int SuccessfulSamples, int RequestedSamples, string? Error = null, BrightnessComparison? Brightness = null, string? SampleDiagnostics = null, MetadataFreeTail? MetadataFreeTail = null, AnalysisOptions? Options = null, IReadOnlyList<ScanSample>? Samples = null);
/// <summary>One clip a scan read, at <see cref="PositionSeconds"/> into the video; <see cref="PeakNits"/> is null when the clip could not be read.</summary>
public sealed record ScanSample(double PositionSeconds, double? PeakNits);
/// <summary>The base-layer brightness the metadata heuristic compares the enhancement layer with: MaxCLL, or the configured assumption when MaxCLL is missing or implausible.</summary>
public sealed record BasePicturePeak(double? Nits, bool Assumed);
public sealed record BrightnessComparison(long ComparedFrames, long ExpandedFrames, double BaseLayerPeakNits, double MaximumDeltaNits, long MaximumDeltaFrame, double ThresholdNits = 50);
public sealed record MediaAnalysis(MediaInfo Media, RpuEvidence Evidence, AnalysisVerdict Verdict, string Reason);
public static class MediaClassifier
{
    public static MediaAnalysis Classify(MediaInfo media, RpuEvidence evidence)
    {
        var result = ClassifyCore(media, evidence);
        if (evidence.Method == AnalysisMethod.SampledRpu && evidence.SuccessfulSamples < evidence.RequestedSamples)
        {
            result = result with { Reason = result.Reason + $" Scan coverage: {evidence.SuccessfulSamples}/{evidence.RequestedSamples} clips succeeded. Missing clips can hide brightness expansion." };
        }
        if (HasVerifiedTail(evidence))
        {
            result = result with
            {
                Reason = result.Reason + $" Verified metadata-free ending: {evidence.MetadataFreeTail!.TailFrames:N0} of {evidence.MetadataFreeTail.TotalFrames:N0} frames. Preserve these base-layer frames without adding Dolby Vision metadata."
            };
        }

        return result;
    }

    public static bool HasVerifiedTail(RpuEvidence evidence) => evidence.Method == AnalysisMethod.FullRpu
        && evidence.Error is null
        && evidence.SuccessfulSamples == evidence.RequestedSamples
        && evidence.Frames > 0
        && evidence.MetadataFreeTail is { } tail
        && tail.RpuFrames == evidence.Frames
        && tail.TotalFrames > tail.RpuFrames
        && tail.PositionHash is { Length: 64 }
        && tail.PositionHash.All(Uri.IsHexDigit);

    private static MediaAnalysis ClassifyCore(MediaInfo media, RpuEvidence evidence)
    {
        if (media.Profile != DolbyVisionProfile.Profile7)
        {
            return new(media, evidence, AnalysisVerdict.NotApplicable, "Input is not Dolby Vision Profile 7.");
        }

        if (evidence.Error is not null)
        {
            return new(media, evidence, AnalysisVerdict.AnalysisFailed, evidence.Error);
        }

        if (evidence.MetadataFreeTail is not null && !HasVerifiedTail(evidence))
        {
            return new(media, evidence, AnalysisVerdict.AnalysisFailed, "Metadata-free ending evidence is invalid or incomplete.");
        }

        int requiredSamples = evidence.Method == AnalysisMethod.SampledRpu
            ? (evidence.Options ?? new AnalysisOptions()).RequiredScanSamples(evidence.RequestedSamples)
            : evidence.RequestedSamples;
        if (evidence.Frames <= 0 || evidence.SuccessfulSamples < requiredSamples)
        {
            return new(media, evidence, AnalysisVerdict.Unknown, "RPU evidence is incomplete.");
        }

        if (evidence.Method == AnalysisMethod.DeepInspection && (evidence.Brightness is null || evidence.Brightness.ComparedFrames != evidence.Frames))
        {
            return new(media, evidence, AnalysisVerdict.Unknown, "Deep inspection frame coverage is incomplete.");
        }

        if (evidence.Layer == EnhancementLayer.Mel)
        {
            return new(media, evidence, AnalysisVerdict.Mel, "Minimal enhancement layer detected in analyzed RPU metadata.");
        }

        if (evidence.Method == AnalysisMethod.DeepInspection && evidence.Layer == EnhancementLayer.Fel && evidence.Brightness is
        {
        }
        brightness)
        {
            return new(media, evidence, brightness.ExpandedFrames > 0 ? AnalysisVerdict.ComplexFel : AnalysisVerdict.SimpleFel, $"Deep inspection: {brightness.ExpandedFrames:N0}/{brightness.ComparedFrames:N0} frames exceed base-layer luminance by more than {brightness.ThresholdNits:F0} nits. " + $"Base-layer peak {brightness.BaseLayerPeakNits:F1} nits; maximum RPU minus base-layer difference {brightness.MaximumDeltaNits:F1} nits at frame {brightness.MaximumDeltaFrame} (zero-based). " + "RPU L1 may describe a scene rather than individual frame content; this indicates possible expansion, not proof of FEL picture contribution or safe removal.");
        }

        var options = evidence.Options ?? new AnalysisOptions();
        var (basePeak, assumed) = BasePeak(media, options);
        if (evidence.Layer != EnhancementLayer.Fel || evidence.PeakNits is null || basePeak is null or <= 0)
        {
            if (evidence.Layer == EnhancementLayer.Fel && HasVerifiedTail(evidence))
            {
                return new(media, evidence, AnalysisVerdict.FelUnclassified, "FEL detected with verified frame coverage, but brightness metadata is insufficient to classify Simple versus Complex FEL. Discarding the enhancement layer loses its picture data.");
            }

            return new(media, evidence, AnalysisVerdict.Unknown, "FEL classification requires layer type, L1 metadata and measured MaxCLL metadata.");
        }

        bool complex = evidence.PeakNits > basePeak + options.BrightnessMarginNits;
        string peakLabel = evidence.Method == AnalysisMethod.FullRpu && options.InspectionPeakPercentile < 100
            ? $"L1 {options.InspectionPeakPercentile:0.##}th-percentile brightness" : "L1 peak";
        string peakSource = assumed ? "assumed base-layer peak (MaxCLL unavailable or below 100 nits)" : "MaxCLL";
        return new(media, evidence, complex ? AnalysisVerdict.ComplexFel : AnalysisVerdict.SimpleFel, $"Metadata heuristic: {peakLabel} {evidence.PeakNits:F0} nits; {peakSource} {basePeak:F0} nits; margin {options.BrightnessMarginNits:F0} nits. This is not a base-layer frame measurement or a playback guarantee.");
    }

    public static BasePicturePeak BasePeak(MediaInfo media, AnalysisOptions options) => media.MaxCll is null or < 100
        ? new(options.AssumedPeakNits, true)
        : new(media.MaxCll, false);

    public static double PqToNits(int value)
    {
        if (value is < 0 or > 4095)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        double p = Math.Pow(value / 4095d, 1d / (2523d / 32));
        return 10000 * Math.Pow(Math.Max(p - 3424d / 4096, 0) / (2413d / 128 - 2392d / 128 * p), 1d / (2610d / 16384));
    }
}
