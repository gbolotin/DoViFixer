using DoViFixer.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prism.Ioc;

namespace DoViFixer.App.Tests;
[TestClass]
public sealed class ViewModelTests
{
    [TestMethod]
    public async Task ClearedRowsCannotInvalidateNewConversionPlans()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var removedRow = model.Files.Single();
        model.ClearAllCommand.Execute();
        Assert.HasCount(0, model.Files);
        Assert.IsNull(model.Focused);

        await model.AddAsync([@"C:\Media\Ocean.mkv"]);
        await model.ConvertCommand.ExecuteAsync();
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));

        removedRow.IsSelected = false;
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));

        model.Files.Single().IsSelected = false;
        Assert.IsFalse(model.ApproveCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(false, true, 0, 0)]
    [DataRow(true, false, 1, 0)]
    [DataRow(true, true, 1, 1)]
    public async Task MissingToolsRequireSeparateApprovalAndResumeOnlyWhenReady(bool approve, bool succeeds, int installs, int probes)
    {
        using var runtime = new TestRuntime
        {
            Ready = false,
            Approval = approve,
            InstallationSucceeds = succeeds
        };
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(installs, runtime.Installations);
        Assert.AreEqual(probes, runtime.Probes);
        Assert.AreEqual(0, runtime.Conversions);
        Assert.IsTrue(runtime.Reviews[0].Contains("Fixture.Tools 1.2.3"));
        Assert.IsTrue(runtime.Reviews[0].Contains("SHA-256"));
    }

    [TestMethod]
    public async Task PreferencesValidateBeforeSavingAndBlankPathsResetDefaults()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.Resolve<DoViFixer.Application.Settings.SettingsService>();
        await settings.SetPreferencesAsync(@"C:\Scratch", @"C:\Output", true, true, default);
        Assert.IsTrue(runtime.Settings.ReplaceOriginal);
        var saved = runtime.Settings;
        runtime.FailDirectoryValidation = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => settings.SetPreferencesAsync(@"C:\Broken", null, false, false, default));
        Assert.AreEqual(saved, runtime.Settings);
        runtime.FailDirectoryValidation = false;
        await settings.SetPreferencesAsync("", "", false, false, default);
        Assert.IsNull(runtime.Settings.TemporaryDirectory);
        Assert.IsNull(runtime.Settings.OutputDirectory);
        Assert.IsFalse(runtime.Settings.ReplaceOriginal);
    }

    [TestMethod]
    public async Task ConvertPlansWithoutManualScanAndEditsInvalidateApproval()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv"]);
        await model.ConvertCommand.ExecuteAsync();
        Assert.AreEqual(1, runtime.FullAnalyses);
        Assert.AreEqual(0, runtime.Conversions);
        Assert.IsTrue(model.Review.Contains("Enhancement-layer picture data will be lost"));
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));
        model.Hdr10 = true;
        Assert.IsFalse(model.ApproveCommand.CanExecute(null));
        await model.ApproveCommand.ExecuteAsync();
        Assert.AreEqual(0, runtime.Conversions);
        await model.ReviewCommand.ExecuteAsync();
        await model.ApproveCommand.ExecuteAsync();
        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Completed", model.Files.Single().Status);
        Assert.IsTrue(model.Files.Single().Result!.Output!.EndsWith(" - HDR10.mkv"));
    }

    [TestMethod]
    public async Task SelectionAndFocusAreIndependentAndSelectionInvalidatesPlan()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        await model.ConvertCommand.ExecuteAsync();
        model.Focused = model.Files[1];
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));
        model.Files[1].IsSelected = false;
        Assert.AreSame(model.Files[1], model.Focused);
        Assert.IsFalse(model.ApproveCommand.CanExecute(null));
        Assert.IsTrue(model.Files[0].IsSelected);
    }

    [TestMethod]
    public async Task ArchivePreparationCannotExecuteWithoutDisplayedPlanApproval()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<ArchiveViewModel>();
        model.Input = @"C:\Media\Mountain.mkv";
        await model.BackupCommand.ExecuteAsync();
        Assert.HasCount(1, runtime.Reviews);
        Assert.IsTrue(runtime.Reviews[0].Contains(@"C:\Media\Mountain.dovi"));
        Assert.AreEqual("Backup not approved.", model.Status);
    }
}
