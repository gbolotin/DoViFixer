using DoViFixer.Application.Conversion;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;

namespace DoViFixer.App.ViewModels;

/// <summary>How the current job card describes a conversion: its target, what happens to the original, and its steps.</summary>
public static class ConversionJob
{
    public static IReadOnlyList<string> Details(ConversionPlan plan)
    {
        string target = plan.Target == ConversionTarget.Profile81 ? "Profile 8.1" : "HDR10";
        string? source = plan.Analysis.Verdict switch
        {
            AnalysisVerdict.Mel => "MEL",
            AnalysisVerdict.SimpleFel => "Simple FEL",
            AnalysisVerdict.ComplexFel => "Complex FEL",
            AnalysisVerdict.FelUnclassified => "FEL",
            _ => null
        };
        string original = plan.DeleteBackup ? "Original: Replace" : "Original: Keep";
        return
        [
            source is null ? $"Converting to {target}" : $"Converting to {target} ({source})",
            plan.Archive is null ? original : $"{original} · EL archive"
        ];
    }

    /// <summary>
    /// The stages each step reports. Streaming conversion extracts and converts the video in one pass; safe mode
    /// extracts it to disk first. Both count as Convert.
    /// </summary>
    public static IEnumerable<JobStep> Steps(ConversionPlan plan)
    {
        if (plan.Archive is not null)
        {
            yield return new("Back up EL", "Backing up enhancement layer");
        }

        yield return new("Convert", "Streaming conversion", "Extracting video", "Converting metadata", "Retrying with safe disk extraction");
        yield return new("Remux", "Remuxing");
        yield return new("Verify", "Verifying");
    }
}
