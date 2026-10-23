using DoViFixer.App.ViewModels;
using DoViFixer.Application.Activity;
using DoViFixer.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

/// <summary>Files that finish arriving in the watch folder are added and converted without asking.</summary>
[TestClass]
public sealed class WatchFolderTests
{
    private const string Folder = @"C:\Downloads";

    private static async Task<(TestRuntime Runtime, MediaViewModel Model)> WatchingAsync()
    {
        var runtime = new TestRuntime();
        await runtime.UpdateAsync(value => value with { WatchFolder = Folder }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.RefreshSettingsSummaryAsync();
        return (runtime, model);
    }

    [TestMethod]
    public async Task AFileTheSettingsWouldConvertIsAddedAndConvertedWithoutAsking()
    {
        var (runtime, model) = await WatchingAsync();
        using var _ = runtime;
        var feed = runtime.Container.GetRequiredService<IActivityFeed>();

        runtime.RaiseFileArrived(@"C:\Downloads\Mountain.mkv");
        await model.AutoConversion;

        // The converted file joins the list after its source, as for any conversion.
        var row = model.Files.First();
        Assert.AreEqual(@"C:\Downloads\Mountain.mkv", row.Path);
        Assert.IsFalse(row.IsSelected, "The new row leaves the user's selection as it is.");
        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual(0, runtime.Reviews.Count, "Automatic conversion asks nothing.");
        Assert.IsTrue(feed.Entries.Any(entry => entry.Title.StartsWith("Auto-converted Mountain.mkv in ", StringComparison.Ordinal)), "Activity marks the conversion as automatic.");
        Assert.IsFalse(model.IsAutoConverting);
        Assert.AreEqual("Automatic conversion", runtime.Feedback.Activities.Last().Title);
    }

    [TestMethod]
    public async Task AFileTheSettingsWouldNotConvertStaysOutOfTheList()
    {
        var (runtime, model) = await WatchingAsync();
        using var _ = runtime;
        var feed = runtime.Container.GetRequiredService<IActivityFeed>();

        runtime.RaiseFileArrived(@"C:\Downloads\Sdr.mkv");
        await model.AutoConversion;

        Assert.AreEqual(0, model.Files.Count);
        Assert.AreEqual(0, runtime.Conversions);
        var entry = feed.Entries.Single(entry => entry.FilePath == @"C:\Downloads\Sdr.mkv");
        Assert.AreEqual("Didn't auto-convert Sdr.mkv", entry.Title);
        Assert.AreEqual(ActivitySeverity.Info, entry.Severity);
    }

    [TestMethod]
    public async Task FilesWaitWhileSettingsAreOpenAndTheStatusBarCountsThem()
    {
        var (runtime, model) = await WatchingAsync();
        using var _ = runtime;
        Assert.IsTrue(model.StatusItems.Any(item => item.Text == @"Watching: C:\Downloads"), "The status bar shows the watched folder.");

        model.HoldAutomaticWork(true);
        runtime.RaiseFileArrived(@"C:\Downloads\Mountain.mkv");

        Assert.AreEqual(0, model.Files.Count);
        Assert.IsTrue(model.StatusItems.Any(item => item.Text == @"Watching: C:\Downloads · 1 waiting"));

        model.HoldAutomaticWork(false);
        await model.AutoConversion;
        Assert.AreEqual(1, runtime.Conversions);
        Assert.IsTrue(model.StatusItems.Any(item => item.Text == @"Watching: C:\Downloads"));
    }

    [TestMethod]
    public async Task TheStatusBarShowsAutomaticConversionWhileItRuns()
    {
        var (runtime, model) = await WatchingAsync();
        using var _ = runtime;
        string[]? during = null;
        runtime.DuringConversion = _ =>
        {
            during = model.StatusItems.Select(item => item.Text).ToArray();
            return Task.CompletedTask;
        };

        runtime.RaiseFileArrived(@"C:\Downloads\Mountain.mkv");
        await model.AutoConversion;

        CollectionAssert.Contains(during, @"Auto-converting from C:\Downloads");
        Assert.IsTrue(model.StatusItems.Single(item => item.Text == @"Watching: C:\Downloads").Command == model.OpenWatchFolderSettingsCommand);
    }

    [TestMethod]
    public async Task MissingToolsAreReportedAndNeverInstalled()
    {
        var (runtime, model) = await WatchingAsync();
        using var _ = runtime;
        var feed = runtime.Container.GetRequiredService<IActivityFeed>();
        runtime.Ready = false;

        runtime.RaiseFileArrived(@"C:\Downloads\Mountain.mkv");
        await model.AutoConversion;

        Assert.AreEqual(0, runtime.Installations);
        Assert.AreEqual(0, runtime.Reviews.Count);
        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual(ActivitySeverity.Error, feed.Entries.Single(entry => entry.FilePath == @"C:\Downloads\Mountain.mkv").Severity);
    }

    [TestMethod]
    public async Task FilesTheListAlreadyShowsAreNotNewArrivals()
    {
        var (runtime, model) = await WatchingAsync();
        using var _ = runtime;
        await model.AddAsync([@"C:\Downloads\Mountain.mkv"]);

        runtime.RaiseFileArrived(@"C:\Downloads\Mountain.mkv");
        await model.AutoConversion;

        Assert.AreEqual(1, model.Files.Count);
        Assert.AreEqual(0, runtime.Conversions);
    }

    [TestMethod]
    public async Task AnUnavailableFolderIsShownAndNoFolderShowsNothing()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.RefreshSettingsSummaryAsync();
        Assert.IsFalse(model.StatusItems.Any(item => item.Text.Contains("Watch", StringComparison.Ordinal)));

        runtime.WatchFolderAvailable = false;
        await runtime.UpdateAsync(value => value with { WatchFolder = Folder }, default);
        await model.RefreshSettingsSummaryAsync();
        var item = model.StatusItems.Single(item => item.Text == @"⚠ Watch folder unavailable: C:\Downloads");
        Assert.IsTrue(item.IsEmphasized);

        runtime.WatchFolderAvailable = true;
        model.CheckSourceFiles();
        Assert.IsTrue(model.StatusItems.Any(item => item.Text == @"Watching: C:\Downloads"), "Activating the window retries the folder.");
    }

    [TestMethod]
    public async Task SettingsSaveTheWatchFolderAndTheShellHoldsAutomaticWorkWhileOpen()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync();
        var watchPage = shell.Settings.Sections.Pages.OfType<WatchFolderSettingsSection>().Single();
        await shell.OpenSettingsCommand.InvokeAsync(watchPage);
        shell.Settings.WatchFolder = Folder;
        shell.Settings.WatchSubfolders = false;
        shell.Settings.WatchSettleSecondsText = "60";
        await shell.Settings.FlushAsync();
        Assert.AreEqual(Folder, runtime.Settings.WatchFolder);
        Assert.IsFalse(runtime.Settings.WatchSubfolders);
        Assert.AreEqual(60, runtime.Settings.WatchSettleSeconds);
        Assert.AreEqual(Folder, runtime.WatchedFolder);

        runtime.RaiseFileArrived(@"C:\Downloads\Mountain.mkv");
        Assert.AreEqual(0, runtime.Conversions, "Nothing starts while Settings is open.");

        await shell.CloseSettingsCommand.InvokeAsync();
        await shell.Media.AutoConversion;
        Assert.AreEqual(1, runtime.Conversions);

        shell.Settings.WatchSettleSecondsText = "2";
        Assert.IsTrue(shell.Settings.HasErrors, "The wait must be at least 5 seconds.");
    }
}
