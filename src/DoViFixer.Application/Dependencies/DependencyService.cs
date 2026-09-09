using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Dependencies;

public sealed class DependencyService(IDependencyDetector detector, IDependencyInstaller installer,
    ISettingsStore settings, IToolCatalog catalog, ILogger<DependencyService> logger)
{
    public async Task<DependencyReport> CheckAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken)
    {
        var report = await detector.DetectAsync(tools, cancellationToken);
        catalog.Refresh(report.Tools);
        foreach (var tool in report.Tools)
        {
            logger.LogDebug("Dependency {Tool} {State}; path {ToolPath}; version {ToolVersion}; {Diagnostic}",
                tool.Tool, tool.State, tool.Path, tool.Version, tool.Diagnostic);
        }
        return report;
    }

    public async Task RequireAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken)
    {
        var report = await CheckAsync(tools, cancellationToken);
        if (!report.Ready)
        {
            throw new DependencyNotReadyException(string.Join(Environment.NewLine,
                report.Tools.Where(t => t.State != DependencyState.Ready).Select(t => $"{t.Tool}: {t.Diagnostic}"))
                + Environment.NewLine + "Run 'dependencies install', or set validated executable paths with 'settings tool'.");
        }
    }

    public Task<InstallationPlan> PrepareAsync(DependencyReport report, CancellationToken cancellationToken)
        => installer.PrepareAsync(report, cancellationToken);

    public Task<DependencyInstallationResult> InstallAsync(InstallationPlan approvedPlan,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "InstallDependencies", approvedPlan.Id, null,
            () => InstallCoreAsync(approvedPlan, progress, cancellationToken), cancellationToken,
            result => result.Report.Ready && result.Outcomes.All(o => o.Succeeded) ? OperationStatus.Completed
                : result.Outcomes.Any(o => o.Succeeded) ? OperationStatus.Partial : OperationStatus.Failed);

    private async Task<DependencyInstallationResult> InstallCoreAsync(InstallationPlan approvedPlan,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var before = await detector.DetectAsync(DependencyRequirements.All, cancellationToken);
        var outcomes = await installer.InstallAsync(approvedPlan, progress, cancellationToken);
        foreach (var outcome in outcomes)
        {
            OperationLog.Audit(logger, "InstallDependency", outcome.Id, outcome.Succeeded ? "InstalledPendingValidation" : "Failed", approvedPlan.Id);
            logger.LogInformation("Installer result {Package}: {Succeeded}; {Reason}", outcome.Id, outcome.Succeeded, outcome.Message);
        }
        // Rediscover approved tools without changing saved paths until validation succeeds.
        var preserved = before.Tools.Where(t => t.State == DependencyState.Ready && t.Path is not null).ToArray();
        var successful = approvedPlan.Items.Where(i => outcomes.Any(o => o.Id == i.Id && o.Succeeded)).SelectMany(i => i.Tools)
            .Except(preserved.Select(t => t.Tool)).ToArray();
        var discovered = await detector.DetectAsync(successful, cancellationToken, skipConfiguredPaths: true);
        var validated = discovered.Tools.Where(t => t.State == DependencyState.Ready && t.Path is not null).ToArray();
        await settings.UpdateAsync(s => s with
        {
            ToolPaths = s.ToolPaths.SetItems(preserved.Concat(validated)
                .Select(t => KeyValuePair.Create(t.Tool, t.Path!)))
        }, cancellationToken);
        var report = await CheckAsync(DependencyRequirements.All, cancellationToken);
        var ready = report.Tools.Where(t => t.State == DependencyState.Ready && t.Path is not null).ToArray();
        await settings.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.SetItems(ready.Select(t => KeyValuePair.Create(t.Tool, t.Path!))) }, cancellationToken);
        foreach (var tool in ready)
        {
            logger.LogInformation("Validated and applied {Tool} {ToolVersion} at {ToolPath}", tool.Tool, tool.Version, tool.Path);
        }
        return new(outcomes, report);
    }
}
