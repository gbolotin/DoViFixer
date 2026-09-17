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
public sealed record RpuEvidence(AnalysisMethod Method, EnhancementLayer Layer, long Frames, double? PeakNits, int SuccessfulSamples, int RequestedSamples, string? Error = null, BrightnessComparison? Brightness = null, string? SampleDiagnostics = null, MetadataFreeTail? MetadataFreeTail = null);
public sealed record BrightnessComparison(long ComparedFrames, long ExpandedFrames, double BaseLayerPeakNits, double MaximumDeltaNits, long MaximumDeltaFrame, double ThresholdNits = 50);
public sealed record MediaAnalysis(MediaInfo Media, RpuEvidence Evidence, AnalysisVerdict Verdict, string Reason);
public static class MediaClassifier
{
    public static MediaAnalysis Classify(MediaInfo media, RpuEvidence evidence)
    {
        var result = ClassifyCore(media, evidence);
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

        if (evidence.Frames <= 0 || evidence.SuccessfulSamples < evidence.RequestedSamples)
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

        if (evidence.Layer != EnhancementLayer.Fel || evidence.PeakNits is null || media.MaxCll is null or <= 0)
        {
            if (evidence.Layer == EnhancementLayer.Fel && HasVerifiedTail(evidence))
            {
                return new(media, evidence, AnalysisVerdict.FelUnclassified, "FEL detected with verified frame coverage, but brightness metadata is insufficient to classify Simple versus Complex FEL. Discarding the enhancement layer loses its picture data.");
            }

            return new(media, evidence, AnalysisVerdict.Unknown, "FEL classification requires layer type, L1 metadata and measured MaxCLL metadata.");
        }

        bool complex = evidence.PeakNits > media.MaxCll + 50;
        return new(media, evidence, complex ? AnalysisVerdict.ComplexFel : AnalysisVerdict.SimpleFel, $"Metadata heuristic: L1 peak {evidence.PeakNits:F0} nits; MaxCLL {media.MaxCll:F0} nits. This is not a base-layer frame measurement or a playback guarantee.");
    }

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
