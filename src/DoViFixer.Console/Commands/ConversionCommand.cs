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

public sealed class ConversionCommand(ConversionPlanner planner, BatchConversionService conversion, ConsoleRenderer renderer, ConsoleInteraction interaction) : IConsoleCommand
{
    public bool Handles(string command) => command == "convert";
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        var request = new ConversionRequest(command.Arguments.FirstOrDefault() ?? Environment.CurrentDirectory, command.Depth, command.Has("hdr10") ? ConversionTarget.Hdr10 : ConversionTarget.Profile81,
            command.Value("output"), command.Value("temp"), command.Has("include-simple"), command.Has("force"), command.Has("backup"), command.Has("safe"), command.Has("delete"), command.Arguments.Skip(1).ToArray());
        bool interactiveApproval = !command.Has("yes") && !command.Has("plan") && interaction.IsInteractive;
        // Prepare eligible FEL plans for review; execution still requires approval below.
        var planningRequest = interactiveApproval ? request with { IncludeSimple = true, ForceComplex = true } : request;
        var planned = await planner.PlanAsync(planningRequest, renderer, cancellationToken);
        foreach (var skipped in planned.Skipped)
        {
            renderer.Result(skipped);
        }
        foreach (var plan in planned.Plans)
        {
            interaction.RecordPlan(plan.Id, "Convert", new
            {
                plan.Analysis.Media.Source, plan.Output, plan.Archive, plan.Target, plan.ScratchBytes,
                plan.TemporaryDirectory, plan.Decision, plan.Safe, plan.DeleteBackup, request.ForceComplex, request.IncludeSimple
            });
            renderer.Write($"Input: {plan.Analysis.Media.Source.Path}\nOutput: {plan.Output}\nTarget: {plan.Target}; scratch estimate: {ConsoleRenderer.FormatSize(plan.ScratchBytes)}\n{plan.Decision}\nOriginal backup: {plan.Analysis.Media.Source.Path}.bak.dovi_convert\n{(plan.DeleteBackup ? "Delete original backup after verified publication." : "Keep original backup.")}");
            if (plan.Archive is not null)
            {
                renderer.Write($"Enhancement archive: {plan.Archive}");
            }
        }
        if (planned.Plans.Count == 0)
        {
            return planned.Skipped.Any(f => f.Status == OperationStatus.Failed) ? 1 : 4;
        }
        if (command.Has("plan"))
        {
            return planned.Skipped.Any(f => f.Status == OperationStatus.Failed) ? 1 : 0;
        }
        var approval = new FelConversionApproval(renderer, interaction.ConfirmAsync);
        var approvedPlans = await approval.ConfirmAsync(planned.Plans, command.Has("yes"), cancellationToken);
        if (approvedPlans.Count == 0)
        {
            return 4;
        }
        var executed = await conversion.ExecuteAsync(approvedPlans, renderer, cancellationToken);
        foreach (var file in executed.Files)
        {
            renderer.Result(file);
        }
        var result = new BatchResult(planned.Skipped.Where(f => f.Status == OperationStatus.Failed).Concat(executed.Files).ToArray());
        int partial = result.Files.Count(f => f.Status == OperationStatus.Partial);
        renderer.Write($"{result.Status.ToString().ToUpperInvariant()}: {result.Files.Count(f => f.Status == OperationStatus.Completed)} completed, " +
            (partial > 0 ? $"{partial} partial, " : "") + $"{result.Files.Count(f => f.Status == OperationStatus.Failed)} failed.");
        return result.Status == OperationStatus.Completed && planned.Skipped.Any(f => f.Status == OperationStatus.Failed) ? 1 : CommandDispatcher.ExitCode(result.Status);
    }

}
