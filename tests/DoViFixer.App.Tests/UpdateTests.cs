using System.Net.Http;
using DoViFixer.App.ViewModels;
using DoViFixer.Application.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class UpdateTests
{
    private const string ReleasePage = "https://github.com/gbolotin/DoViFixer/releases/tag/v99.0.0";

    [TestMethod]
    public async Task ANewerReleaseIsOfferedInTheStatusBarAndOpensItsPage()
    {
        using var runtime = new TestRuntime();
        runtime.LatestRelease = _ => Task.FromResult<ReleaseInfo?>(new("v99.0.0", ReleasePage));
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        Assert.IsFalse(shell.Updates.IsUpdateAvailable, "Nothing is offered before a check.");
        Assert.IsFalse(shell.Updates.OpenReleaseCommand.CanExecute(null));

        await shell.Updates.CheckCommand.InvokeAsync();

        Assert.IsTrue(shell.Updates.IsUpdateAvailable);
        Assert.AreEqual("Update to 99.0.0", shell.Updates.ButtonText);
        StringAssert.Contains(shell.Updates.CheckResult, "99.0.0 is available");
        shell.Updates.OpenReleaseCommand.Invoke();
        Assert.AreEqual(new Uri(ReleasePage), runtime.OpenedAddress);
    }

    [TestMethod]
    public async Task TheHelpMenuOpensAboutAndChecks()
    {
        using var runtime = new TestRuntime();
        runtime.LatestRelease = _ => Task.FromResult<ReleaseInfo?>(new("v0.0.1", "https://github.com/gbolotin/DoViFixer/releases/tag/v0.0.1"));
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();

        await shell.CheckForUpdatesCommand.InvokeAsync();

        Assert.IsTrue(shell.IsSettingsOpen);
        Assert.IsInstanceOfType<AboutSettingsSection>(shell.Settings.Sections.CurrentPage);
        Assert.AreEqual(1, runtime.UpdateChecks);
        Assert.IsFalse(shell.Updates.IsUpdateAvailable, "An older release is not offered.");
        StringAssert.Contains(shell.Updates.CheckResult, "is up to date");
    }

    [TestMethod]
    public async Task AFailedCheckSaysWhyAndKeepsWhatWasFound()
    {
        using var runtime = new TestRuntime();
        runtime.LatestRelease = _ => Task.FromResult<ReleaseInfo?>(new("v99.0.0", ReleasePage));
        var updates = runtime.Container.GetRequiredService<UpdateViewModel>();
        await updates.CheckCommand.InvokeAsync();

        runtime.LatestRelease = _ => Task.FromException<ReleaseInfo?>(new HttpRequestException("Could not reach GitHub to check for updates: offline"));
        await updates.CheckCommand.InvokeAsync();

        Assert.AreEqual("Could not reach GitHub to check for updates: offline", updates.CheckResult);
        Assert.IsTrue(updates.IsUpdateAvailable, "A failed check does not withdraw the release found earlier.");
    }

    [TestMethod]
    public async Task OnlyAWebPageIsOpened()
    {
        using var runtime = new TestRuntime();
        runtime.LatestRelease = _ => Task.FromResult<ReleaseInfo?>(new("v99.0.0", @"file:///C:/Windows/System32/calc.exe"));
        var updates = runtime.Container.GetRequiredService<UpdateViewModel>();

        await updates.CheckCommand.InvokeAsync();

        Assert.IsFalse(updates.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task ChecksAtStartAndAgainEachInterval()
    {
        using var runtime = new TestRuntime();
        var time = new ManualTimeProvider();
        runtime.Services.AddSingleton<TimeProvider>(time);
        var checks = new SemaphoreSlim(0);
        ReleaseInfo? published = null;
        runtime.LatestRelease = _ =>
        {
            checks.Release();
            return Task.FromResult(published);
        };
        var updates = runtime.Container.GetRequiredService<UpdateViewModel>();

        updates.Start();
        Assert.IsTrue(await checks.WaitAsync(TimeSpan.FromSeconds(10)), "The first check runs at start.");
        Assert.AreEqual(UpdateViewModel.CheckInterval, time.Timer?.Period);
        Assert.IsFalse(updates.IsUpdateAvailable);

        published = new("v99.0.0", ReleasePage);
        time.Timer!.Fire();
        Assert.IsTrue(await checks.WaitAsync(TimeSpan.FromSeconds(10)), "The interval brings another check.");
        await WaitUntilAsync(() => updates.IsUpdateAvailable);

        await updates.StopAsync();
        time.Timer.Fire();
        Assert.IsFalse(await checks.WaitAsync(TimeSpan.FromMilliseconds(200)), "No check runs after the app closes.");
    }

    [TestMethod]
    public async Task TurningTheSettingOffStopsAutomaticChecks()
    {
        using var runtime = new TestRuntime();
        var time = new ManualTimeProvider();
        runtime.Services.AddSingleton<TimeProvider>(time);
        await runtime.UpdateAsync(settings => settings with { CheckForUpdates = false }, default);
        var updates = runtime.Container.GetRequiredService<UpdateViewModel>();

        updates.Start();
        await WaitUntilAsync(() => time.Timer is not null);
        time.Timer!.Fire();
        await Task.Delay(100);
        await updates.StopAsync();

        Assert.AreEqual(0, runtime.UpdateChecks);
        await updates.CheckCommand.InvokeAsync();
        Assert.AreEqual(1, runtime.UpdateChecks, "Asking still checks.");
    }

    [TestMethod]
    public async Task TheSettingIsSaved()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<ShellViewModel>().Settings;
        await settings.LoadCommand.InvokeAsync();
        Assert.IsTrue(settings.CheckForUpdates, "Update checks are on by default.");

        settings.CheckForUpdates = false;
        await settings.FlushAsync();

        Assert.IsFalse(runtime.Settings.CheckForUpdates);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(50);
        }

        Assert.IsTrue(condition());
    }

    /// <summary>Hands out one timer that fires only when a test says so.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        public ManualTimer? Timer { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            Timer = new ManualTimer(callback, state, period);
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan period) : ITimer
    {
        private bool disposed;
        public TimeSpan Period { get; private set; } = period;
        public void Fire()
        {
            if (!disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Period = period;
            return !disposed;
        }

        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
