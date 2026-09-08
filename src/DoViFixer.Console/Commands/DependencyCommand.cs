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

public sealed class DependencyCommand(DependencyService dependencies, ConsoleRenderer renderer, ConsoleInteraction interaction) : IConsoleCommand
{
    public bool Handles(string command) => command == "dependencies";
    public async Task<bool> PreflightAsync(CommandLine command, CancellationToken cancellationToken)
    {
        var report = await dependencies.CheckAsync(DependencyRequirements.All, cancellationToken);
        if (report.Ready)
        {
            return true;
        }
        if (command.Has("json"))
        {
            renderer.Json(new { Status = "DependenciesNotReady", Dependencies = report, Recovery = "Run dependencies install or settings tool." });
            return false;
        }
        PrintDependencies(report);
        bool explicitInstall = command.Has("install-dependencies");
        if (!explicitInstall && (command.Has("yes") || !interaction.IsInteractive || command.Has("json")))
        {
            renderer.Write("Missing requirements. Run 'dependencies install', or explicitly use --install-dependencies.");
            return false;
        }
        var plan = await dependencies.PrepareAsync(report, false, cancellationToken);
        return await InstallAsync(plan, explicitInstall, cancellationToken);
    }

    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        var requested = command.Arguments.Count > 1 ? command.Arguments.Skip(1).Select(s => Enum.Parse<NativeTool>(s, true)).ToArray() : DependencyRequirements.All;
        var report = await dependencies.CheckAsync(requested, cancellationToken);
        if (command.Has("json"))
        {
            renderer.Json(report);
        }
        else
        {
            PrintDependencies(report);
        }
        if (command.Arguments[0] == "check" || report.Ready)
        {
            return report.Ready ? 0 : 3;
        }
        var plan = await dependencies.PrepareAsync(report, command.Has("repair"), cancellationToken);
        await InstallAsync(plan, command.Has("yes"), cancellationToken);
        return (await dependencies.CheckAsync(requested, cancellationToken)).Ready ? 0 : 3;
    }

    private async Task<bool> InstallAsync(InstallationPlan plan, bool approved, CancellationToken cancellationToken)
    {
        foreach (string diagnostic in plan.Unavailable)
        {
            renderer.Write(diagnostic);
        }
        foreach (var item in plan.Items)
        {
            interaction.RecordPlan(plan.Id, "InstallDependency", item);
            renderer.Write($"Install {item.Id} {item.Version} ({string.Join(", ", item.Tools)})\nProvider: {item.Provider}; source: {item.Source}\nDestination: {item.Destination}\nScope: {item.Scope}; elevation: {(item.RequiresElevation ? "required" : "not required")}\nSHA-256: {item.Sha256}");
        }
        if (plan.Items.Count == 0 || !await interaction.ConfirmAsync("Install this exact dependency plan?", approved, cancellationToken, plan.Id))
        {
            renderer.Write("Dependencies remain unmet. Configure official CLI paths with 'settings tool' or retry dependencies install.");
            return false;
        }
        var result = await dependencies.InstallAsync(plan, renderer, cancellationToken);
        foreach (var outcome in result.Outcomes)
        {
            renderer.Write($"{outcome.Id}: {outcome.Message}");
        }
        PrintDependencies(result.Report);
        return result.Report.Ready;
    }

    private void PrintDependencies(DependencyReport report)
    {
        foreach (var tool in report.Tools)
        {
            renderer.Write($"{tool.Tool}: {tool.State} {tool.Version} {tool.Path}\n  {tool.Diagnostic}");
        }
    }

}
