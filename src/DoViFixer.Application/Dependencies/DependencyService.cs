using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;

namespace DoViFixer.Application.Dependencies;

public sealed class DependencyService(IDependencyDetector detector, IDependencyInstaller installer,
    ISettingsStore settings, IToolCatalog catalog)
{
    public async Task<DependencyReport> CheckAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken)
    {
        var report = await detector.DetectAsync(tools, cancellationToken);
        catalog.Refresh(report.Tools);
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

    public Task<InstallationPlan> PrepareAsync(DependencyReport report, bool allowRepair, CancellationToken cancellationToken)
        => installer.PrepareAsync(report, allowRepair, cancellationToken);

    public async Task<DependencyInstallationResult> InstallAsync(InstallationPlan approvedPlan,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var before = await detector.DetectAsync(DependencyRequirements.All, cancellationToken);
        var outcomes = await installer.InstallAsync(approvedPlan, progress, cancellationToken);
        // Remove only explicitly approved replacement paths before independently rediscovering tools.
        var preserved = before.Tools.Where(t => t.State == DependencyState.Ready && t.Path is not null).ToArray();
        var successful = approvedPlan.Items.Where(i => outcomes.Any(o => o.Id == i.Id && o.Succeeded)).SelectMany(i => i.Tools)
            .Except(preserved.Select(t => t.Tool)).ToArray();
        await settings.UpdateAsync(s => s with
        {
            ToolPaths = s.ToolPaths.RemoveRange(successful).SetItems(preserved
                .Where(t => !s.ToolPaths.ContainsKey(t.Tool)).Select(t => KeyValuePair.Create(t.Tool, t.Path!)))
        }, cancellationToken);
        var report = await CheckAsync(DependencyRequirements.All, cancellationToken);
        var ready = report.Tools.Where(t => t.State == DependencyState.Ready && t.Path is not null).ToArray();
        await settings.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.SetItems(ready.Select(t => KeyValuePair.Create(t.Tool, t.Path!))) }, cancellationToken);
        return new(outcomes, report);
    }
}
