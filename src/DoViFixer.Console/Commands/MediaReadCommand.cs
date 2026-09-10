using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Scanning;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;
using DoViFixer.Console.Interaction;
using DoViFixer.Console.Rendering;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;

namespace DoViFixer.Console.Commands;

public sealed class MediaReadCommand(ScanService scan, InspectionService inspection, ConsoleRenderer renderer) : IConsoleCommand
{
    public bool Handles(string command) => command is "scan" or "inspect";
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        string input = command.Arguments.FirstOrDefault() ?? Environment.CurrentDirectory;
        switch (command.Command)
        {
            case "scan":
                if (command.Has("inspect-simple") && !command.Has("json"))
                {
                    renderer.Write("After scanning, Simple FEL candidates will receive deep inspection of every base-layer frame. This may take a long time and requires temporary disk space.");
                }
                var scanRenderer = command.Has("json") ? null : new ScanRenderer(renderer, command.Has("candidates-only"));
                var results = await scan.ScanAsync(input, command.Depth, command.Value("temp"), renderer, cancellationToken, scanRenderer, command.Has("inspect-simple"));
                var visible = command.Has("candidates-only") ? results.Where(r => r.InitialAnalysis is not null || r.Error is not null || r.Analysis?.Verdict is AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel or AnalysisVerdict.AnalysisFailed).ToArray() : results;
                if (command.Has("json"))
                {
                    renderer.Json(visible);
                }
                else
                {
                    scanRenderer!.Summary(results);
                }
                return results.Any(r => r.Error is not null || r.Analysis?.Verdict == AnalysisVerdict.AnalysisFailed) ? 1 : 0;
            case "inspect":
                if (command.Has("deep") && !command.Has("json"))
                {
                    renderer.Write("Deep inspection decodes every HDR10 base-layer frame and may take a long time.");
                }
                var analysis = await inspection.InspectAsync(input, command.Has("deep") ? AnalysisMethod.DeepInspection : AnalysisMethod.FullRpu, command.Value("temp"), cancellationToken);
                if (command.Has("json"))
                {
                    renderer.Json(analysis);
                }
                else
                {
                    renderer.Write($"{analysis.Media.Source.Path}\n{analysis.Media.Profile}; {analysis.Verdict}\n{analysis.Reason}\n" +
                        $"Evidence: {analysis.Evidence.Method}; {analysis.Evidence.Frames:N0} RPUs; L1 peak: {analysis.Evidence.PeakNits?.ToString("F0") ?? "unknown"} nits.");
                }
                return analysis.Verdict == AnalysisVerdict.AnalysisFailed ? 1 : 0;
            default:
                throw new ArgumentException("Unsupported command.");
        }
    }
}
