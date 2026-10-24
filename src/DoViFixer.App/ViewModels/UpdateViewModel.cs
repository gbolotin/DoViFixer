using DoViFixer.App.Dialogs;
using DoViFixer.App.Startup;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.ViewModels;
/// <summary>
/// Looks for a newer release: at startup and every <see cref="CheckInterval"/> while Settings › About allows it, and
/// whenever the user asks. A newer release shows a button at the right of the status bar that opens its download page.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject, IDisposable
{
    private readonly UpdateService updates;
    private readonly SettingsService settings;
    private readonly IWebBrowser browser;
    private readonly TimeProvider time;
    private readonly ILogger<UpdateViewModel> logger;
    private readonly CancellationTokenSource stopping = new();
    // Kept apart from the source, so a check the user starts while the app closes needs no disposed source.
    private readonly CancellationToken stopToken;
    private Task periodicChecks = Task.CompletedTask;

    public UpdateViewModel(UpdateService updates, SettingsService settings, IWebBrowser browser, TimeProvider time, ILogger<UpdateViewModel> logger)
    {
        this.updates = updates;
        this.settings = settings;
        this.browser = browser;
        this.time = time;
        this.logger = logger;
        stopToken = stopping.Token;
    }

    /// <summary>How often a running app looks for a release published since it started.</summary>
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromHours(6);

    /// <summary>The Microsoft Store updates its own installs, so the Store app neither checks nor offers updates.</summary>
    public bool IsSupported { get; } = !AppPackage.IsStoreApp;

    public string CurrentVersion { get; init; } = ApplicationVersion.Of(typeof(UpdateViewModel).Assembly);

    /// <summary>The newer release the last check found, or <see langword="null"/> when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable), nameof(ButtonText), nameof(ToolTip))]
    [NotifyCanExecuteChangedFor(nameof(OpenReleaseCommand))]
    public partial ReleaseInfo? AvailableRelease { get; private set; }

    public bool IsUpdateAvailable => AvailableRelease is not null;

    public string ButtonText => AvailableRelease is { } release ? $"Update to {Display(release.Version)}" : "";

    public string ToolTip => AvailableRelease is { } release
        ? $"{ApplicationTitle.Name} {Display(release.Version)} is available. You have {CurrentVersion}.\nOpens the release page, where you can download it."
        : "";

    /// <summary>What the last check the user started found, shown in Settings › About.</summary>
    [ObservableProperty]
    public partial string CheckResult { get; private set; } = "";

    /// <summary>Starts the checks at startup and every <see cref="CheckInterval"/>. They stop when the app closes.</summary>
    public void Start()
    {
        if (IsSupported && periodicChecks.IsCompleted && !stopToken.IsCancellationRequested)
        {
            periodicChecks = CheckPeriodicallyAsync(stopToken);
        }
    }

    private async Task CheckPeriodicallyAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(CheckInterval, time);
        try
        {
            do
            {
                await CheckInBackgroundAsync(token);
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    // The setting is read at each check, so turning it off stops the next one.
    private async Task CheckInBackgroundAsync(CancellationToken token)
    {
        try
        {
            if ((await settings.ReadAsync(token)).CheckForUpdates)
            {
                Apply(await updates.CheckAsync(CurrentVersion, token));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
        {
            // Offline or rate limited: nothing interrupts the user, and the next check tries again.
            logger.LogInformation(ex, "Could not check for updates");
        }
    }

    [RelayCommand(CanExecute = nameof(IsSupported))]
    private async Task Check(CancellationToken cancellationToken)
    {
        CheckResult = "Checking for updates…";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);
        try
        {
            var result = await updates.CheckAsync(CurrentVersion, linked.Token);
            Apply(result);
            CheckResult = result switch
            {
                { Latest: null } => $"No {ApplicationTitle.Name} release has been published yet.",
                { Latest: { } latest, IsUpdateAvailable: true } => $"{ApplicationTitle.Name} {Display(latest.Version)} is available. You have {CurrentVersion}.",
                { Latest: { } latest } => $"{ApplicationTitle.Name} is up to date. The latest release is {Display(latest.Version)}.",
            };
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            CheckResult = "";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check for updates");
            CheckResult = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsUpdateAvailable))]
    private void OpenRelease()
    {
        if (AvailableRelease is { } release)
        {
            browser.Open(new Uri(release.Url));
        }
    }

    // Only a web address is opened, whatever the release reports.
    private void Apply(UpdateCheckResult result) => AvailableRelease =
        result is { IsUpdateAvailable: true, Latest: { } latest } && Uri.TryCreate(latest.Url, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps
            ? latest
            : null;

    private static string Display(string version) => version.StartsWith('v') || version.StartsWith('V') ? version[1..] : version;

    /// <summary>Stops the checks and waits for those running, including one the user started.</summary>
    public async Task StopAsync()
    {
        stopping.Cancel();
        await periodicChecks;
        await (CheckCommand.ExecutionTask ?? Task.CompletedTask);
    }

    public void Dispose()
    {
        stopping.Cancel();
        stopping.Dispose();
    }
}
