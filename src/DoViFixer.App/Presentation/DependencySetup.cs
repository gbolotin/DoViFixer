using DoViFixer.App.Dialogs;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.Presentation;
public sealed class DependencySetup(DependencyService dependencies, IUserDialogs dialogs, ILogger<DependencySetup> logger)
{
    public async Task<bool> EnsureAsync(IProgress<OperationProgress> progress, CancellationToken token)
    {
        var report = await dependencies.CheckAsync(DependencyRequirements.All, token);
        if (report.Ready)
        {
            return true;
        }

        var plan = await dependencies.PrepareAsync(report, token);
        string details = string.Join("\n", report.Tools.Select(t => $"{t.Tool}: {t.State} — {t.Version}\n{t.Path}\n{t.Diagnostic}"));
        details += "\n\nInstallation plan\n" + string.Join("\n\n", plan.Items.Select(i => $"{i.Id} {i.Version}\nTools: {string.Join(", ", i.Tools)}\nSource: {i.Source}\nDestination: {i.Destination}\nScope: {i.Scope}; elevation: {i.RequiresElevation}\nProvider: {i.Provider}\nSHA-256: {i.Sha256 ?? "Not supplied"}"));
        details += "\n" + string.Join("\n", plan.Unavailable);
        if (plan.Items.Count == 0)
        {
            dialogs.Review("Dependency setup unavailable", details + "\nSet executable paths in Settings, then retry.", "Close");
            return false;
        }

        if (!dialogs.Review("Dependency setup", details, "Install displayed tools"))
        {
            return false;
        }

        OperationLog.Audit(logger, "ApproveInstallation", details, "ApprovedByDialog", plan.Id);
        var result = await dependencies.InstallAsync(plan, progress, token);
        if (!result.Report.Ready)
        {
            dialogs.Review("Dependency setup incomplete", string.Join("\n", result.Outcomes.Select(o => $"{o.Id}: {o.Message}")) + "\n" + string.Join("\n", result.Report.Tools.Where(t => t.State != DependencyState.Ready).Select(t => $"{t.Tool}: {t.Diagnostic}")), "Close");
        }

        return result.Report.Ready;
    }
}
