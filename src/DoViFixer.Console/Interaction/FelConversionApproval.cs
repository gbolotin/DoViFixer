using DoViFixer.Console.Rendering;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.Console.Interaction;

public sealed class FelConversionApproval(ConsoleRenderer renderer,
    Func<string, bool, CancellationToken, Guid?, Task<bool>> confirm)
{
    private readonly Dictionary<AnalysisVerdict, bool> answers = new();

    public async Task<bool> ConfirmAsync(MediaAnalysis analysis, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (answers.TryGetValue(analysis.Verdict, out bool approved))
        {
            return approved;
        }
        string category = analysis.Verdict switch
        {
            AnalysisVerdict.SimpleFel => "Simple FEL",
            AnalysisVerdict.ComplexFel => "Complex FEL",
            _ => throw new ArgumentException("Only detected FEL can be approved.", nameof(analysis))
        };
        renderer.Write($"{category}: {analysis.Media.Source.Path}\n{analysis.Reason}");
        string warning = analysis.Verdict == AnalysisVerdict.ComplexFel
            ? " Enhancement-layer picture data will be lost." : "";
        approved = await confirm($"Include {category} files in this conversion batch?{warning}", false, cancellationToken, null);
        answers.Add(analysis.Verdict, approved);
        return approved;
    }
}
