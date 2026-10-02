using DoViFixer.App.Dialogs;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.Presentation.Application;
public sealed class DependencySetup(DependencyService dependencies, DependencyReportViewModel status, IDialogService dialogs, ILogger<DependencySetup> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private int completedEnsures;
    private bool lastEnsureResult;

    public async Task<DependencyReport> CheckAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            return await CheckCoreAsync(token);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DependencyReport> CheckCoreAsync(CancellationToken token)
    {
        try
        {
            var result = await Task.Run(() => dependencies.CheckAsync(DependencyRequirements.All, token), token);
            status.Update(result);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Dependency check failed");
            status.Fail(ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Checks the tools and offers installation when any are missing. Callers that were waiting while
    /// another caller finished this workflow reuse its result instead of prompting again.
    /// </summary>
    public async Task<bool> EnsureAsync(IProgress<OperationProgress> progress, CancellationToken token, bool reportUnavailable = true)
    {
        int observed = completedEnsures;
        await gate.WaitAsync(token);
        try
        {
            if (observed != completedEnsures)
            {
                return lastEnsureResult;
            }

            lastEnsureResult = await EnsureCoreAsync(progress, reportUnavailable, token);
            completedEnsures++;
            return lastEnsureResult;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<bool> EnsureCoreAsync(IProgress<OperationProgress> progress, bool reportUnavailable, CancellationToken token)
    {
        var report = await CheckCoreAsync(token);
        if (report.Ready)
        {
            return true;
        }

        var plan = await Task.Run(() => dependencies.PrepareAsync(report, token), token);
        token.ThrowIfCancellationRequested();
        string installed = string.Join("\n", report.Tools.Where(t => t.State == DependencyState.Ready).Select(t => $"{t.Tool}: Installed and ready — {t.Version}\n{t.Path}"));
        string missing = string.Join("\n", report.Tools.Where(t => t.State != DependencyState.Ready).Select(t => $"{t.Tool}: {t.State}\n{t.Path}\n{t.Diagnostic}"));
        string details = "DoViFixer uses these command-line tools to inspect and process media. Install the missing or unusable tools to enable those operations. You can cancel now and install them later from Settings.\n\n"
            + NativeToolDescriptions.Summary +"\n\nInstalled and ready\n" + (installed.Length == 0 ? "None" : installed) + "\n\nMissing or needs attention\n" + missing;
        details += "\n\nInstallation plan\n" + string.Join("\n\n", plan.Items.Select(i => $"{i.Id} {i.Version}\nTools: {string.Join(", ", i.Tools)}\nSource: {i.Source}\nDestination: {i.Destination}\nScope: {i.Scope}; elevation: {i.RequiresElevation}\nProvider: {i.Provider}\nSHA-256: {i.Sha256 ?? "Not supplied"}"));
        details += "\n" + string.Join("\n", plan.Unavailable);
        if (plan.Items.Count == 0)
        {
            if (reportUnavailable)
            {
                await dialogs.ReviewAsync("Dependency setup unavailable", details + "\nSet executable paths in Settings, then retry.", "Close");
            }
            return false;
        }

        if (!await dialogs.ReviewAsync("Dependency setup", details, "Install displayed tools"))
        {
            return false;
        }

        OperationLog.Audit(logger, "ApproveInstallation", details, "ApprovedByDialog", plan.Id);
        var result = await Task.Run(() => dependencies.InstallAsync(plan, progress, token), token);
        status.Update(result.Report);
        if (!result.Report.Ready)
        {
            await dialogs.ReviewAsync("Dependency setup incomplete", string.Join("\n", result.Outcomes.Select(o => $"{o.Id}: {o.Message}")) + "\n" + string.Join("\n", result.Report.Tools.Where(t => t.State != DependencyState.Ready).Select(t => $"{t.Tool}: {t.Diagnostic}")), "Close");
        }

        return result.Report.Ready;
    }

    public void Dispose() => gate.Dispose();
}
