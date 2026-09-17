using DoViFixer.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prism.Ioc;

namespace DoViFixer.App.Tests;
[TestClass]
public sealed class ViewModelTests
{
    [TestMethod]
    public async Task IncompleteScanExplainsSampleFailureAndInspectsOnlyClickedRow()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var row = model.Files[0];
        var evidence = row.Analysis!.Evidence with
        {
            SuccessfulSamples = 9,
            SampleDiagnostics = "Sample 10/10 at 01:39:51: No RPU was found in input file"
        };
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(row.Analysis.Media, evidence);
        Assert.AreEqual("Profile 7 · Incomplete scan", row.Classification);
        Assert.IsTrue(row.Details.Contains("Samples: 9/10"));
        Assert.IsTrue(row.Details.Contains("No RPU was found in input file"));
        Assert.IsTrue(row.Details.Contains("Suggested action: Inspect"));
        Assert.IsFalse(row.CanRetryAnalysis);
        Assert.IsTrue(model.InspectIncompleteCommand.CanExecute(row));
        row.IsActive = true;
        Assert.IsFalse(row.CanInspectIncomplete);
        row.IsActive = false;
        row.IsSelected = false;
        model.Focused = model.Files[1];
        int analyses = runtime.Analyses;
        await model.InspectIncompleteCommand.ExecuteAsync(row);
        Assert.AreEqual(analyses + 1, runtime.Analyses);
        Assert.AreEqual(1, runtime.FullAnalyses);
        Assert.AreEqual(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, row.Analysis!.Evidence.Method);
        Assert.IsFalse(row.CanInspectIncomplete);
        Assert.AreEqual("", row.Status);
        Assert.AreEqual(0, runtime.Conversions);
    }

    [TestMethod]
    [DataRow(DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu)]
    [DataRow(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu)]
    [DataRow(DoViFixer.Domain.Analysis.AnalysisMethod.DeepInspection)]
    public async Task RetryRepeatsOnlyFailedRowsAnalysisMethod(DoViFixer.Domain.Analysis.AnalysisMethod method)
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var row = model.Files[0];
        model.Files[1].IsSelected = false;
        await runtime.ClearAsync(default);
        runtime.DuringAnalysis = _ => throw new IOException("Fixture analysis failure");
        var command = method switch
        {
            DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu => model.ScanCommand,
            DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu => model.InspectCommand,
            _ => model.DeepInspectCommand
        };
        await command.ExecuteAsync();
        Assert.AreEqual(method == DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu ? "Scan failed" : "Inspection failed", row.Status);
        Assert.IsTrue(row.CanRetryAnalysis);
        Assert.IsTrue(row.Details.Contains("Fixture analysis failure"));
        int analyses = runtime.Analyses;
        runtime.DuringAnalysis = null;
        row.IsSelected = false;
        model.Focused = model.Files[1];
        await model.RetryAnalysisCommand.ExecuteAsync(row);
        Assert.AreEqual(analyses + 1, runtime.Analyses);
        Assert.AreEqual(method, row.Analysis!.Evidence.Method);
        Assert.AreEqual("", row.Status);
        Assert.IsFalse(row.CanRetryAnalysis);
        Assert.IsFalse(model.RetryAnalysisCommand.CanExecute(row));
        Assert.AreEqual(0, runtime.Conversions);
    }

    [TestMethod]
    public async Task OpenFolderUsesClickedRowRatherThanFocusedRow()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\One\Mountain.mkv", @"C:\Media\Two\Ocean.mkv"]);
        await model.ConvertCommand.ExecuteAsync();
        await model.ApproveCommand.ExecuteAsync();
        model.Focused = model.Files[1];
        model.OpenRowOutputCommand.Execute(model.Files[0]);
        Assert.AreEqual(@"C:\Media\One", runtime.OpenedFolder);
        Assert.AreEqual("Converted", model.Files[0].Status);
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual("", model.Files[0].Status);
        Assert.IsFalse(model.Files[0].CanOpenResult);
        Assert.IsNotNull(model.Files[0].Result, "Analysis retains the previous conversion details.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddedFilesAppearBeforeSequentialScanningAndSupportCancellation(bool cancelAll)
    {
        using var runtime = new TestRuntime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var model = runtime.Container.Resolve<MediaViewModel>();
        var adding = model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.HasCount(2, model.Files);
        Assert.AreEqual("Scanning", model.Files[0].Status);
        Assert.AreEqual("Queued", model.Files[1].Status);
        Assert.AreEqual(1, runtime.Analyses);
        Assert.IsTrue(model.IsBusy);
        runtime.DuringAnalysis = null;
        if (cancelAll)
        {
            model.CancelCommand.Execute();
        }
        else
        {
            model.CancelFileCommand.Execute(model.Files[0]);
        }

        await adding.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual("Scan cancelled", model.Files[0].Status);
        Assert.AreEqual(cancelAll ? "Scan cancelled" : "", model.Files[1].Status);
        Assert.IsTrue(model.Files[0].CanRetryAnalysis);
        Assert.AreEqual(cancelAll ? 1 : 2, runtime.Analyses);
        Assert.AreEqual(0, runtime.FullAnalyses);
    }

    [TestMethod]
    public async Task AutomaticScanUsesCacheWithoutToolsAndDoesNotRescanExistingRows()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        Assert.HasCount(2, model.Files);
        Assert.AreEqual(2, runtime.Analyses);
        model.ClearAllCommand.Execute();
        runtime.Ready = false;
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        Assert.AreEqual("", model.Files.Single().Status);
        Assert.AreEqual(2, runtime.Analyses);
        Assert.HasCount(0, runtime.Reviews);
    }

    [TestMethod]
    public async Task SettingsControlAutomaticScanningCacheReuseAndClearing()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.Resolve<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.IsTrue(settings.AutomaticallyScanAddedFiles);
        Assert.IsTrue(settings.UseCachedResults);
        settings.AutomaticallyScanAddedFiles = false;
        settings.UseCachedResults = false;
        await settings.SaveCommand.ExecuteAsync();
        Assert.IsFalse(runtime.Settings.AutomaticallyScanAddedFiles);
        Assert.IsFalse(runtime.Settings.UseCachedResults);
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        Assert.AreEqual(0, runtime.Analyses);
        await model.ScanCommand.ExecuteAsync();
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(2, runtime.Analyses);
        Assert.AreEqual(2, runtime.Probes, "Disabling reuse must also bypass cached probe metadata.");
        settings.UseCachedResults = true;
        await settings.SaveCommand.ExecuteAsync();
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(2, runtime.Analyses);
        await settings.ClearCacheCommand.ExecuteAsync();
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(3, runtime.Analyses);
    }

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
        Assert.AreEqual("Converted", model.Files.Single().Status);
        Assert.IsTrue(model.Files.Single().CanOpenResult);
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
