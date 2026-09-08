using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Domain.Conversion;

public enum ConversionTarget { Profile81, Hdr10 }
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
            AnalysisVerdict.SimpleFel => new(false, "Simple FEL requires --include-simple."),
            AnalysisVerdict.ComplexFel => new(false, "Complex FEL requires --force."),
            _ => new(false, "Analysis is unknown or failed. Inspect the file and resolve missing evidence before converting.")
        };
    }

    public static long RequiredScratchBytes(long sourceLength) => checked(sourceLength * 8 + (1L << 30));
}
