using DoViFixer.Application.Scanning;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Console.Rendering;

public sealed class ScanRenderer(ConsoleRenderer renderer, bool candidatesOnly) : IProgress<ScanItem>
{
    public void Report(ScanItem item)
    {
        if (candidatesOnly && item.InitialAnalysis is null && item.Error is null && item.Analysis?.Verdict is not
            (AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel or AnalysisVerdict.AnalysisFailed))
        {
            return;
        }
        ConsoleColor? color = item.Error is not null ? ConsoleColor.Red : item.Analysis?.Verdict switch
        {
            AnalysisVerdict.Mel => ConsoleColor.Green,
            AnalysisVerdict.SimpleFel => ConsoleColor.Blue,
            AnalysisVerdict.ComplexFel or AnalysisVerdict.AnalysisFailed => ConsoleColor.Red,
            AnalysisVerdict.Unknown => ConsoleColor.Yellow,
            _ => null
        };
        renderer.Write(Format(item), color);
    }

    public void Summary(IReadOnlyList<ScanItem> results)
    {
        int failed = results.Count(r => r.Error is not null || r.Analysis?.Verdict == AnalysisVerdict.AnalysisFailed);
        renderer.Write($"Scanned {results.Count} file(s); {failed} failed.");
        if (results.Any(r => r.Analysis is { Evidence.Method: not AnalysisMethod.DeepInspection, Verdict: AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel }))
        {
            renderer.Write("Simple/complex FEL is estimated from Dolby Vision metadata, not decoded video pixels. It does not guarantee playback quality.");
        }
    }

    public static string Format(ScanItem item)
    {
        if (item.Error is not null)
        {
            return $"{item.Path}\n  {(item.InitialAnalysis is null ? "FAILED" : "AUTO-INSPECTION FAILED")}: {item.Error}\n";
        }
        var analysis = item.Analysis!;
        string profile = analysis.Media.Profile switch
        {
            DolbyVisionProfile.Profile7 => "Dolby Vision Profile 7",
            DolbyVisionProfile.Profile5 => "Dolby Vision Profile 5",
            DolbyVisionProfile.Profile81 => "Dolby Vision Profile 8.1",
            DolbyVisionProfile.None => "No Dolby Vision",
            DolbyVisionProfile.Other => "Other Dolby Vision profile",
            _ => "Unknown Dolby Vision profile"
        };
        string verdict = analysis.Verdict switch
        {
            AnalysisVerdict.SimpleFel => "Simple FEL",
            AnalysisVerdict.ComplexFel => "Complex FEL",
            AnalysisVerdict.Mel => "MEL",
            AnalysisVerdict.AnalysisFailed => "FAILED",
            AnalysisVerdict.NotApplicable => "Not applicable",
            _ => "Unknown"
        };
        string details = analysis.Evidence.Method != AnalysisMethod.DeepInspection && analysis.Verdict is AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel
            ? $"Metadata L1 peak: {analysis.Evidence.PeakNits:N0} nits | MaxCLL: {analysis.Media.MaxCll:N0} nits"
            : analysis.Reason;
        string update = item.InitialAnalysis is null ? "" : "Auto-inspected: Simple FEL -> ";
        return $"{item.Path}\n  {profile} | {update}{verdict}\n  Evidence: {analysis.Evidence.Method}\n  {details}\n";
    }
}
