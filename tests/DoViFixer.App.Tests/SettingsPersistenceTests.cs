using DoViFixer.App.ViewModels;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class SettingsPersistenceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NavigationWaitsForPageReadAndRejectsOverlappingRequests(bool settingsPage)
    {
        using var runtime = new TestRuntime();
        var store = new DelayedReadStore(runtime);
        runtime.Services.AddSingleton<ISettingsStore>(store);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        var archive = shell.Pages.OfType<ArchiveViewModel>().Single();
        shell.CurrentPage = archive;
        shell.CurrentPage = settingsPage ? shell.Settings : media;
        var navigating = shell.NavigationTask;
        Task closing = Task.CompletedTask;
        try
        {
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(navigating.IsCompleted);
            Assert.IsFalse(shell.CanNavigate);
            var currentPage = shell.CurrentPage;
            shell.CurrentPage = archive;
            Assert.AreSame(currentPage, shell.CurrentPage);
            Assert.AreSame(navigating, shell.NavigationTask);
            if (!settingsPage)
            {
                closing = shell.CancelAndWaitAsync();
                Assert.IsFalse(closing.IsCompleted, "Shutdown must also wait for a media summary refresh.");
            }
        }
        finally
        {
            store.Release.TrySetResult();
            await navigating.WaitAsync(TimeSpan.FromSeconds(10));
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.IsTrue(shell.CanNavigate);
        Assert.AreEqual(1, store.Reads);
        shell.CurrentPage = shell.CurrentPage;
        Assert.AreEqual(1, store.Reads, "Selecting the current page must not reload it.");
    }

    [TestMethod]
    public async Task FailedPageLoadReportsErrorAndReenablesNavigation()
    {
        using var runtime = new TestRuntime();
        var store = new DelayedReadStore(runtime);
        runtime.Services.AddSingleton<ISettingsStore>(store);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        shell.CurrentPage = shell.Settings;
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        store.Release.SetException(new IOException("Settings unavailable"));
        await shell.NavigationTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(ViewStatus.Error, shell.Settings.Status);
        Assert.AreEqual("Settings unavailable", shell.Settings.StatusMessage);
        Assert.IsTrue(shell.CanNavigate);
        var archive = shell.Pages.OfType<ArchiveViewModel>().Single();
        shell.CurrentPage = archive;
        await shell.NavigationTask;
        Assert.AreSame(archive, shell.CurrentPage);
    }

    [TestMethod]
    public async Task InitializationWaitsForTheInitialSummaryAndReadsOnlyOnce()
    {
        using var runtime = new TestRuntime();
        var store = new DelayedReadStore(runtime);
        runtime.Services.AddSingleton<ISettingsStore>(store);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        Assert.AreEqual(0, store.Reads, "Constructing the shell must not launch asynchronous work.");
        var initializing = shell.InitializeAsync();
        try
        {
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(initializing.IsCompleted);
        }
        finally
        {
            store.Release.TrySetResult();
            await initializing.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.IsFalse(string.IsNullOrEmpty(shell.Pages.OfType<MediaViewModel>().Single().OutputSummaryToolTip));
        await shell.InitializeAsync();
        Assert.AreEqual(1, store.Reads);
    }

    [TestMethod]
    public async Task ConversionWaitsForAutosaveAndKeepsOriginalAfterReplacementIsDisabled()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { ReplaceOriginal = true }, default);
        var store = new DelayedStore(runtime);
        runtime.Services.AddSingleton<ISettingsStore>(store);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        await shell.Settings.LoadCommand.ExecuteAsync();
        await media.AddAsync([@"C:\Media\Mountain.mkv"]);

        shell.Settings.ReplaceOriginal = false;
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var converting = media.ConvertDv81Command.ExecuteAsync();
        try
        {
            Assert.IsFalse(shell.CanNavigate);
            Assert.IsTrue(shell.Settings.IsSaving);
            Assert.IsFalse(converting.IsCompleted);
            Assert.AreEqual(0, runtime.Conversions);
        }
        finally
        {
            store.Release.TrySetResult();
            await converting.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.IsFalse(runtime.Settings.ReplaceOriginal);
        Assert.AreEqual(@"C:\Media\Mountain - DV P8.1.mkv", media.Files[0].Result!.Output);
        Assert.IsTrue(shell.CanNavigate);
    }

    [TestMethod]
    public async Task ShutdownWaitsForAllQueuedSaves()
    {
        using var runtime = new TestRuntime();
        var store = new DelayedStore(runtime);
        runtime.Services.AddSingleton<ISettingsStore>(store);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.Settings.LoadCommand.ExecuteAsync();

        shell.Settings.IncludeSimple = true;
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var closing = shell.CancelAndWaitAsync();
        shell.Settings.ForceComplex = true;
        try
        {
            Assert.IsFalse(closing.IsCompleted);
            Assert.IsFalse(shell.CanNavigate);
        }
        finally
        {
            store.Release.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.IsTrue(runtime.Settings.IncludeSimple);
        Assert.IsTrue(runtime.Settings.ForceComplex);
        Assert.IsFalse(shell.Settings.IsSaving);
    }

    [TestMethod]
    public async Task FailedAutosaveBlocksConversionAndClosingUntilRetrySucceeds()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { ReplaceOriginal = true, TemporaryDirectory = Path.GetTempPath() }, default);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        await shell.Settings.LoadCommand.ExecuteAsync();
        await media.AddAsync([@"C:\Media\Mountain.mkv"]);

        runtime.FailDirectoryValidation = true;
        shell.Settings.ReplaceOriginal = false;
        await shell.Settings.SaveTask;
        Assert.AreEqual("Directory unavailable", shell.Settings.SaveError);
        Assert.AreEqual(ViewStatus.Error, shell.Settings.Status);
        Assert.AreEqual(shell.Settings.SaveError, shell.Settings.StatusMessage);
        Assert.IsFalse(shell.CanNavigate);

        await media.ConvertDv81Command.ExecuteAsync();
        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual(ViewStatus.Error, media.Status);
        Assert.AreEqual("Directory unavailable", media.StatusMessage);
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.CancelAndWaitAsync());

        runtime.FailDirectoryValidation = false;
        await shell.Settings.SaveCommand.ExecuteAsync();
        Assert.IsNull(shell.Settings.SaveError);
        Assert.IsFalse(runtime.Settings.ReplaceOriginal);
        Assert.IsTrue(shell.CanNavigate);
        await shell.CancelAndWaitAsync();
    }

    [TestMethod]
    public async Task DiscardRestoresLastSuccessfulSaveAndAllowsNavigationAndShutdownWithoutStorage()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        await shell.Settings.LoadCommand.ExecuteAsync();
        shell.Settings.IncludeSimple = true;
        await shell.Settings.SaveTask;
        var saved = runtime.Settings;

        runtime.FailDirectoryValidation = true;
        shell.Settings.Temporary = @"C:\Unavailable";
        await shell.Settings.SaveTask;
        shell.Settings.SelectedTheme = AppTheme.Dark;
        await shell.Settings.SaveTask;
        shell.Settings.ReplaceOriginal = true;
        await shell.Settings.SaveTask;
        Assert.IsFalse(shell.CanNavigate);
        Assert.IsTrue(shell.Settings.DiscardChangesCommand.CanExecute());

        shell.Settings.DiscardChangesCommand.Execute();

        Assert.IsNull(shell.Settings.SaveError);
        Assert.AreEqual(saved, runtime.Settings);
        Assert.AreEqual("", shell.Settings.Temporary);
        Assert.IsTrue(shell.Settings.IncludeSimple);
        Assert.IsFalse(shell.Settings.ReplaceOriginal);
        Assert.AreEqual(saved.Theme, runtime.AppliedTheme);
        Assert.AreEqual("FEL: Simple only", media.FelSummary);
        Assert.IsFalse(shell.Settings.DiscardChangesCommand.CanExecute());
        Assert.IsTrue(shell.CanNavigate);
        await shell.CancelAndWaitAsync();
    }

    private sealed class DelayedReadStore(TestRuntime runtime) : ISettingsStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }

        public async Task<UserSettings> ReadAsync(CancellationToken token)
        {
            Reads++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            return await runtime.ReadAsync(token);
        }

        public Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken token) => runtime.UpdateAsync(update, token);
    }

    private sealed class DelayedStore(TestRuntime runtime) : ISettingsStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<UserSettings> ReadAsync(CancellationToken token) => runtime.ReadAsync(token);

        public async Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken token)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            await runtime.UpdateAsync(update, token);
        }
    }
}
