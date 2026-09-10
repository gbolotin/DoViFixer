using DoViFixer.Application.Conversion;
using DoViFixer.Console.Rendering;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.Console.Interaction;

public sealed class FelConversionApproval(ConsoleRenderer renderer,
    Func<string, bool, CancellationToken, Guid?, Task<bool>> confirm)
{
    public async Task<IReadOnlyList<ConversionPlan>> ConfirmAsync(
        IReadOnlyList<ConversionPlan> plans, bool yes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plans.Count == 0)
        {
            return [];
        }
        if (yes)
        {
            return await confirm($"Execute these {plans.Count} conversion(s)?", true, cancellationToken, null) ? plans : [];
        }

        var approved = new HashSet<Guid>();
        foreach (var group in plans.GroupBy(plan => plan.Analysis.Verdict))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string category = group.Key switch
            {
                AnalysisVerdict.SimpleFel => "Simple FEL",
                AnalysisVerdict.ComplexFel => "Complex FEL",
                AnalysisVerdict.Mel => "MEL",
                _ => group.Key.ToString()
            };
            renderer.Write($"{category} conversion plans:");
            foreach (var plan in group)
            {
                renderer.Write($"  {plan.Analysis.Media.Source.Path}");
            }
            string warning = group.Key is AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel
                ? " Enhancement-layer picture data will be lost." : "";
            bool accepted = await confirm($"Execute these {group.Count()} {category} conversion(s) as planned?{warning}",
                false, cancellationToken, group.Count() == 1 ? group.First().Id : null);
            foreach (var plan in group)
            {
                if (accepted)
                {
                    approved.Add(plan.Id);
                }
                else
                {
                    renderer.Write($"Skipped: {plan.Analysis.Media.Source.Path} (conversion declined).");
                }
            }
        }
        return plans.Where(plan => approved.Contains(plan.Id)).ToArray();
    }
}
