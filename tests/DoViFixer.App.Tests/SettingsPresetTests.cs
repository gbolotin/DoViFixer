using DoViFixer.App.ViewModels;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class SettingsPresetTests
{
    [TestMethod]
    public async Task LoadingListsBuiltInsFirstAndNamesTheMatchingPreset()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();

        CollectionAssert.AreEqual(BuiltInPresets.All.ToList(), settings.Presets.ToList());
        Assert.AreEqual(BuiltInPresets.Default, settings.SelectedPreset);
        Assert.AreEqual("Preset: Default", settings.ActivePresetText);
        Assert.IsFalse(settings.DeletePresetCommand.CanExecute(null));

        settings.ScanSampleCountText = "12";
        await settings.SaveTask;
        Assert.AreEqual("Custom settings", settings.ActivePresetText);
    }

    [TestMethod]
    public async Task ApplyingShowsEveryChangeAndRiskBeforeChangingSettings()
    {
        using var runtime = new TestRuntime { Approval = true };
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        UserSettings? published = null;
        settings.Saved += value => published = value;
        await settings.LoadCommand.InvokeAsync();

        settings.SelectedPreset = BuiltInPresets.ReplaceOriginalsKeepArchive;
        await settings.ApplyPresetCommand.InvokeAsync();

        string review = runtime.Reviews.Single();
        StringAssert.Contains(review, "Caution");
        StringAssert.Contains(review, "Replace original after verification: Off → On");
        StringAssert.Contains(review, "Clips per video: 10 → 40");
        Assert.AreEqual("Apply preset", runtime.LastApproveLabel);
        Assert.IsTrue(runtime.Settings.ReplaceOriginal);
        Assert.IsTrue(settings.ReplaceOriginal);
        Assert.AreEqual("40", settings.ScanSampleCountText);
        Assert.AreEqual(1, runtime.Settings.ConversionWorkers);
        Assert.IsTrue(published!.ReplaceOriginal);
        Assert.AreEqual("Preset: Replace originals, keep EL archive", settings.ActivePresetText);

        settings.ScanSampleCountText = "50";
        await settings.SaveTask;
        Assert.AreEqual("Custom settings (changed from Replace originals, keep EL archive)", settings.ActivePresetText);
    }

    [TestMethod]
    public async Task DecliningTheReviewLeavesSettingsUnchanged()
    {
        using var runtime = new TestRuntime { Approval = false };
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        var before = runtime.Settings;

        settings.SelectedPreset = BuiltInPresets.ExhaustiveCheck;
        await settings.ApplyPresetCommand.InvokeAsync();

        Assert.HasCount(1, runtime.Reviews);
        Assert.AreEqual(before, runtime.Settings);
        Assert.AreEqual("10", settings.ScanSampleCountText);
        StringAssert.Contains(settings.StatusText, "not applied");
    }

    [TestMethod]
    public async Task ApplyingTheMatchingPresetAsksNothing()
    {
        using var runtime = new TestRuntime { Approval = true };
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();

        settings.SelectedPreset = BuiltInPresets.Default;
        await settings.ApplyPresetCommand.InvokeAsync();

        Assert.IsEmpty(runtime.Reviews);
        StringAssert.Contains(settings.StatusText, "already match");
    }

    [TestMethod]
    public async Task SavedPresetsCanBeSelectedReplacedAndDeleted()
    {
        using var runtime = new TestRuntime { Approval = true };
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        settings.ScanSampleCountText = "30";
        await settings.SaveTask;

        settings.NewPresetName = "My films";
        await settings.SavePresetCommand.InvokeAsync();
        Assert.AreEqual("My films", runtime.SavedPresets.Single().Name);
        Assert.AreEqual(30, runtime.SavedPresets.Single().Values.ScanSampleCount);
        Assert.AreEqual("My films", settings.SelectedPreset?.Name);
        Assert.AreEqual("Preset: My films", settings.ActivePresetText);
        Assert.AreEqual("", settings.NewPresetName);

        settings.ScanSampleCountText = "35";
        await settings.SaveTask;
        settings.NewPresetName = "my films";
        await settings.SavePresetCommand.InvokeAsync();
        StringAssert.Contains(runtime.Confirmations.Single(), "already exists");
        Assert.AreEqual(35, runtime.SavedPresets.Single().Values.ScanSampleCount);

        Assert.IsTrue(settings.DeletePresetCommand.CanExecute(null));
        await settings.DeletePresetCommand.InvokeAsync();
        Assert.IsEmpty(runtime.SavedPresets);
        Assert.AreEqual(35, runtime.Settings.ScanSampleCount);
        Assert.AreEqual(BuiltInPresets.All.Count, settings.Presets.Count);
    }
}
