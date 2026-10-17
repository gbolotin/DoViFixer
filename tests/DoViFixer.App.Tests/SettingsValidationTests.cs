using System.Globalization;
using DoViFixer.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class SettingsValidationTests
{
    [TestMethod]
    public async Task SamplingAndPercentileEditsPersistValidateAndReset()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.OpenSettingsCommand.InvokeAsync();
        var settings = shell.Settings;
        settings.ScanSampleCountText = "7";
        settings.ScanSampleSecondsText = 2.5.ToString(CultureInfo.CurrentCulture);
        settings.MinimumScanCoveragePercentText = "50";
        settings.InspectionPeakPercentileText = 99.9.ToString(CultureInfo.CurrentCulture);
        await settings.SaveAsync();
        Assert.AreEqual(7, runtime.Settings.ScanSampleCount);
        Assert.AreEqual(2.5, runtime.Settings.ScanSampleSeconds);
        Assert.AreEqual(50, runtime.Settings.MinimumScanCoveragePercent);
        Assert.AreEqual(99.9, runtime.Settings.InspectionPeakPercentile);
        StringAssert.Contains(settings.ScanCoverageSummary, "4 of 7");

        settings.ScanSampleCountText = "101";
        settings.ScanSampleSecondsText = "NaN";
        settings.MinimumScanCoveragePercentText = "0";
        settings.InspectionPeakPercentileText = "letters";
        await settings.SaveTask;
        Assert.IsTrue(settings.HasErrors);
        Assert.AreEqual(7, runtime.Settings.ScanSampleCount);
        settings.DiscardChangesCommand.Invoke();
        Assert.IsFalse(settings.HasErrors);
        Assert.AreEqual("7", settings.ScanSampleCountText);

        settings.ResetScanSampleCountCommand.Invoke();
        settings.ResetScanSampleSecondsCommand.Invoke();
        settings.ResetMinimumScanCoveragePercentCommand.Invoke();
        settings.ResetInspectionPeakPercentileCommand.Invoke();
        await settings.SaveAsync();
        Assert.AreEqual(10, runtime.Settings.ScanSampleCount);
        Assert.AreEqual(1d, runtime.Settings.ScanSampleSeconds);
        Assert.AreEqual(100, runtime.Settings.MinimumScanCoveragePercent);
        Assert.AreEqual(100d, runtime.Settings.InspectionPeakPercentile);
        await shell.CloseSettingsCommand.InvokeAsync();
        await shell.OpenSettingsCommand.InvokeAsync();
        Assert.AreEqual("10", settings.ScanSampleCountText);
        Assert.AreEqual("100", settings.InspectionPeakPercentileText);
    }

    [TestMethod]
    public async Task InvalidDraftsBlockSavingAndClosingUntilCorrectedOrDiscarded()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.OpenSettingsCommand.InvokeAsync();
        var settings = shell.Settings;
        var changed = new List<string?>();
        settings.ErrorsChanged += (_, args) => changed.Add(args.PropertyName);

        settings.BrightnessMarginText = "-50";
        await settings.SaveTask;
        Assert.IsTrue(settings.HasErrors);
        Assert.IsFalse(settings.GetErrors(null).Cast<string>().Any(), "Field errors must not mark a binding to the whole ViewModel invalid.");
        CollectionAssert.Contains(changed, nameof(settings.BrightnessMarginText));
        Assert.AreEqual(50, runtime.Settings.BrightnessMarginNits);
        await shell.CloseSettingsCommand.InvokeAsync();
        Assert.IsTrue(shell.IsSettingsOpen);
        await settings.Sections.NavigateAsync(settings.Sections.Pages.OfType<SafetySettingsSection>().Single());
        Assert.AreEqual("-50", settings.BrightnessMarginText);

        settings.FreeSpaceMarginText = "NaN";
        settings.BrightnessMarginText = "50";
        await settings.SaveTask;
        Assert.IsTrue(settings.HasErrors, "Correcting one field must not erase another invalid draft.");
        Assert.AreEqual("NaN", settings.FreeSpaceMarginText);
        settings.DiscardChangesCommand.Invoke();
        Assert.IsFalse(settings.HasErrors);
        Assert.IsNull(settings.SaveError);
        Assert.AreEqual("20", settings.FreeSpaceMarginText);

        settings.BrightnessMarginText = "letters";
        await settings.SaveTask;
        settings.ResetBrightnessMarginCommand.Invoke();
        await settings.SaveTask;
        Assert.IsFalse(settings.HasErrors, "Reset must recover even when the stored number already equals its default.");
        Assert.IsNull(settings.SaveError);
        await shell.CloseSettingsCommand.InvokeAsync();
        Assert.IsFalse(shell.IsSettingsOpen);
    }

    [TestMethod]
    public async Task NumericBoundsAndMalformedInputAreValidatedBeforePersistence()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        foreach (string invalid in new[] { "", " ", "abc", "1.5", "-1", "1001", "999999999999999999999" })
        {
            settings.BrightnessMarginText = invalid;
            await settings.SaveTask;
            Assert.IsTrue(settings.HasErrors, invalid);
            Assert.AreEqual(50, runtime.Settings.BrightnessMarginNits);
        }
        foreach (string valid in new[] { "0", "1000", "50" })
        {
            settings.BrightnessMarginText = valid;
            await settings.SaveAsync();
            Assert.IsFalse(settings.HasErrors);
            Assert.AreEqual(int.Parse(valid, CultureInfo.CurrentCulture), runtime.Settings.BrightnessMarginNits);
        }
        foreach (string invalid in new[] { "99", "10001", "", "abc" })
        {
            settings.AssumedPeakBrightnessText = invalid;
            await settings.SaveTask;
            Assert.IsTrue(settings.HasErrors, invalid);
        }
        foreach (string valid in new[] { "100", "10000", "1000" })
        {
            settings.AssumedPeakBrightnessText = valid;
            await settings.SaveAsync();
            Assert.IsFalse(settings.HasErrors);
            Assert.AreEqual(int.Parse(valid, CultureInfo.CurrentCulture), runtime.Settings.AssumedPeakBrightnessNits);
        }
        foreach (string invalid in new[] { "-1", "201", "NaN", double.PositiveInfinity.ToString(CultureInfo.CurrentCulture), "", "abc" })
        {
            settings.FreeSpaceMarginText = invalid;
            await settings.SaveTask;
            Assert.IsTrue(settings.HasErrors, invalid);
            Assert.AreEqual(20, runtime.Settings.FreeSpaceMarginPercent);
        }
        foreach (double valid in new[] { 0, 200, 12.5 })
        {
            settings.FreeSpaceMarginText = valid.ToString(CultureInfo.CurrentCulture);
            await settings.SaveAsync();
            Assert.IsFalse(settings.HasErrors);
            Assert.AreEqual(valid, runtime.Settings.FreeSpaceMarginPercent);
        }
    }

    [TestMethod]
    public async Task RequiredDestinationAndSliderChangesClearTheirOwnErrors()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        settings.OtherFolder = true;
        await settings.SaveTask;
        Assert.IsTrue(settings.GetErrors(nameof(settings.Destination)).Cast<string>().Any());
        settings.OtherFolder = false;
        await settings.SaveAsync();
        Assert.IsFalse(settings.HasErrors);
        settings.AssumedPeakBrightnessText = "abc";
        await settings.SaveTask;
        settings.AssumedPeakBrightnessNits = 1500;
        await settings.SaveAsync();
        Assert.IsFalse(settings.HasErrors);
        Assert.AreEqual("1500", settings.AssumedPeakBrightnessText);
        Assert.AreEqual(1500, runtime.Settings.AssumedPeakBrightnessNits);
    }
}
