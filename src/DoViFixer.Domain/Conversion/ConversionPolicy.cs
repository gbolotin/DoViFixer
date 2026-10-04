using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Domain.Conversion;
public enum ConversionTarget
{
    Profile81,
    Hdr10
}

public sealed record ConversionDecision(bool Allowed, string Reason);
public static class ConversionPolicy
{
    public static ConversionDecision Evaluate(MediaAnalysis analysis, bool includeSimple, bool forceComplex)
    {
        if (analysis.Media.Profile != DolbyVisionProfile.Profile7 || !analysis.Media.VideoCodec.Contains("HEVC", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "Only HEVC Dolby Vision Profile 7 MKV inputs are supported.");
        }

        return analysis.Verdict switch
        {
            AnalysisVerdict.Mel => new(true, analysis.Reason),
            AnalysisVerdict.SimpleFel when includeSimple => new(true, analysis.Reason),
            AnalysisVerdict.ComplexFel when forceComplex => new(true, "Explicit complex-FEL override: enhancement-layer picture data will be lost."),
            AnalysisVerdict.FelUnclassified when forceComplex && MediaClassifier.HasVerifiedTail(analysis.Evidence) && analysis.Evidence.Layer == EnhancementLayer.Fel => new(true, analysis.Reason),
            AnalysisVerdict.FelUnclassified => new(false, "Unclassified FEL with a verified metadata-free ending requires --force or explicit FEL approval; enhancement-layer picture data will be lost."),
            AnalysisVerdict.SimpleFel => new(false, "Simple FEL requires --include-simple."),
            AnalysisVerdict.ComplexFel => new(false, "Complex FEL requires --force."),
            _ => new(false, "Analysis is unknown or failed. Inspect the file and resolve missing evidence before converting.")
        };
    }

    public static bool ShouldAutoSelectAfterAnalysis(MediaAnalysis? analysis, bool includeSimple = false, bool forceComplex = false)
    {
        if (analysis is null)
        {
            return false;
        }

        return Evaluate(analysis, includeSimple, forceComplex).Allowed;
    }

    /// <summary>
    /// Scratch space a conversion, backup or restore needs. At most three video-sized files exist at once:
    /// the enhancement-layer backup plus an extracted track and its processed copy, or during verification
    /// an extracted track and its cleaned base layer. Each is bounded by the source size. Track verification
    /// stays below that peak: it holds one file's non-video tracks, together bounded by the source size,
    /// and the per-frame timestamp lists of both files, which are far smaller than the tracks they describe.
    /// </summary>
    public static long RequiredScratchBytes(long sourceLength) => checked(sourceLength * 3 + (2L << 30));
}
