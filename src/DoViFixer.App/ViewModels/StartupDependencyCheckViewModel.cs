using DoViFixer.App.Presentation.Application;

namespace DoViFixer.App.ViewModels;
/// <summary>
/// Runs the startup dependency check as its own cancellable operation so it neither occupies a page
/// nor blocks navigation.
/// </summary>
public sealed class StartupDependencyCheckViewModel(DependencySetup setup) : OperationViewModel
{
    public Task CheckAsync() => RunAsync(async (token, progress) =>
    {
        SetStatus(ViewStatus.Progress, "Checking dependencies…");
        // The status bar warning already reports tools that cannot be installed automatically.
        bool ready = await setup.EnsureAsync(progress, token, reportUnavailable: false);
        SetStatus(ready ? ViewStatus.ToolsReady : ViewStatus.DependencySetupIncomplete);
    });
}
