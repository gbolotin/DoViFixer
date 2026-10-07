using System.Text.Json;
using System.Text.Json.Serialization;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Scanning;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.Mcp.Tools;
/// <summary>Compact JSON views of application results; the assistant reads them, so they leave out raw tool output such as MediaInfo JSON.</summary>
internal static class ToolViews
{
    private static readonly JsonSerializerOptions json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static string Json(object value) => JsonSerializer.Serialize(value, json);

    public static object Analysis(MediaAnalysis analysis) => new
    {
        Path = analysis.Media.Source.Path,
        analysis.Media.Profile,
        analysis.Verdict,
        analysis.Reason,
        Video = $"{analysis.Media.VideoCodec} {analysis.Media.Width}x{analysis.Media.Height}",
        analysis.Media.DurationSeconds,
        SizeBytes = analysis.Media.Source.Length,
        Evidence = new
        {
            analysis.Evidence.Method,
            analysis.Evidence.Layer,
            RpuFrames = analysis.Evidence.Frames,
            analysis.Evidence.PeakNits,
            analysis.Media.MaxCll,
            analysis.Evidence.Error
        }
    };

    public static object Scan(ScanItem item) => new
    {
        item.Path,
        Analysis = item.Analysis is null ? null : Analysis(item.Analysis),
        InitialVerdict = item.InitialAnalysis?.Verdict,
        item.Error
    };

    public static bool IsCandidate(ScanItem item) => item.InitialAnalysis is not null || item.Error is not null || item.Analysis?.Verdict is AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel or AnalysisVerdict.AnalysisFailed;

    public static object Plan(ConversionPlan plan) => new
    {
        PlanId = plan.Id,
        Input = plan.Analysis.Media.Source.Path,
        plan.Output,
        plan.Archive,
        plan.Target,
        plan.Analysis.Verdict,
        plan.Decision,
        plan.ScratchBytes,
        OriginalHandling = plan.DeleteBackup ? $"Rename the original to {plan.Analysis.Media.Source.Path}.bak.dovi_convert and delete that backup after verified publication." : "Keep the original unchanged at its existing path.",
        Warning = plan.Analysis.Verdict is AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel or AnalysisVerdict.FelUnclassified ? "Enhancement-layer picture data will be lost." : null
    };
}
