using DoViFixer.App.ViewModels;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prism.Ioc;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class SettingsPersistenceTests
{
    [TestMethod]
    public async Task ConversionWaitsForAutosaveAndKeepsOriginalAfterReplacementIsDisabled()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { ReplaceOriginal = true }, default);
        var store = new DelayedStore(runtime);
        ((IContainerRegistry)runtime.Container).RegisterInstance<ISettingsStore>(store);
        var shell = runtime.Container.Resolve<ShellViewModel>();
        await shell.Settings.LoadCommand.ExecuteAsync();
        await shell.Media.AddAsync([@"C:\Media\Mountain.mkv"]);

        shell.Settings.ReplaceOriginal = false;
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var converting = shell.Media.ConvertDv81Command.ExecuteAsync();
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
        Assert.AreEqual(@"C:\Media\Mountain - DV P8.1.mkv", shell.Media.Files[0].Result!.Output);
        Assert.IsTrue(shell.CanNavigate);
    }

    [TestMethod]
    public async Task ShutdownWaitsForAllQueuedSaves()
    {
        using var runtime = new TestRuntime();
        var store = new DelayedStore(runtime);
        ((IContainerRegistry)runtime.Container).RegisterInstance<ISettingsStore>(store);
        var shell = runtime.Container.Resolve<ShellViewModel>();
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
        var shell = runtime.Container.Resolve<ShellViewModel>();
        await shell.Settings.LoadCommand.ExecuteAsync();
        await shell.Media.AddAsync([@"C:\Media\Mountain.mkv"]);

        runtime.FailDirectoryValidation = true;
        shell.Settings.ReplaceOriginal = false;
        await shell.Settings.SaveTask;
        Assert.AreEqual("Directory unavailable", shell.Settings.SaveError);
        Assert.AreEqual(shell.Settings.SaveError, shell.Settings.Status);
        Assert.IsFalse(shell.CanNavigate);

        await shell.Media.ConvertDv81Command.ExecuteAsync();
        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual("Directory unavailable", shell.Media.Status);
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
        var shell = runtime.Container.Resolve<ShellViewModel>();
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
        Assert.AreEqual("FEL: Simple only", shell.Media.FelSummary);
        Assert.IsFalse(shell.Settings.DiscardChangesCommand.CanExecute());
        Assert.IsTrue(shell.CanNavigate);
        await shell.CancelAndWaitAsync();
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
