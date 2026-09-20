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
        Assert.AreEqual(2, model.BatchProgress.Total);
        Assert.AreEqual(0, model.BatchProgress.Processed);
        Assert.AreSame(model.Files[0], model.BatchProgress.CurrentJob);
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
        Assert.AreEqual(cancelAll ? 50 : 100, model.BatchProgress.Percent);
        Assert.IsFalse(model.BatchProgress.IsRunning);
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
        Assert.IsTrue(model.Files.Single().HasWarning);
        Assert.AreEqual("Enhancement-layer picture data will be lost.", model.Files.Single().Warning);
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));
        Assert.AreEqual("Ready to convert", model.Files.Single().Status);
        Assert.AreEqual(MediaRowState.PlanReady, model.Files.Single().State);
        Assert.IsTrue(model.Files.Single().PlannedOutput.EndsWith(" - DV P8.1.mkv"));
        model.Hdr10 = true;
        Assert.IsFalse(model.ApproveCommand.CanExecute(null));
        Assert.IsFalse(model.Files.Single().HasWarning);
        Assert.IsNull(model.Files.Single().Warning);
        Assert.AreEqual("", model.Files.Single().Status);
        Assert.AreEqual("", model.Files.Single().PlannedOutput);
        Assert.AreEqual(MediaRowState.Scanned, model.Files.Single().State);
        await model.ApproveCommand.ExecuteAsync();
        Assert.AreEqual(0, runtime.Conversions);
        await model.ReviewCommand.ExecuteAsync();
        Assert.AreEqual("Ready to convert", model.Files.Single().Status);
        Assert.AreEqual(MediaRowState.PlanReady, model.Files.Single().State);
        Assert.IsTrue(model.Files.Single().HasWarning);
        Assert.AreEqual("Enhancement-layer picture data will be lost.", model.Files.Single().Warning);
        await model.ApproveCommand.ExecuteAsync();
        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files.Single().Status);
        Assert.AreEqual(MediaRowState.Converted, model.Files.Single().State);
        Assert.IsFalse(model.Files.Single().HasWarning);
        Assert.IsNull(model.Files.Single().Warning);
        Assert.IsTrue(model.Files.Single().CanOpenResult);
        Assert.IsTrue(model.Files.Single().Result!.Output!.EndsWith(" - HDR10.mkv"));
    }

    [TestMethod]
    public async Task PrepareAsyncSetsWarningTriangleOnFelRowsAndInvalidateClearsIt()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        model.Files[2].IsSelected = true;
        await model.ConvertCommand.ExecuteAsync();

        Assert.IsFalse(model.Files[0].HasWarning);
        Assert.IsNull(model.Files[0].Warning);

        Assert.IsTrue(model.Files[1].HasWarning);
        Assert.AreEqual("Enhancement-layer picture data will be lost.", model.Files[1].Warning);

        Assert.IsTrue(model.Files[2].HasWarning);
        Assert.AreEqual("Enhancement-layer picture data will be lost.", model.Files[2].Warning);

        model.OtherFolder = true;
        Assert.IsFalse(model.Files[1].HasWarning);
        Assert.IsNull(model.Files[1].Warning);
        Assert.IsFalse(model.Files[2].HasWarning);
        Assert.IsNull(model.Files[2].Warning);
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

    [TestMethod]
    public async Task ScanAutoSelectsMelAndSimpleFelAndDeselectsOthers()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([
            @"C:\Media\Mountain.mkv",
            @"C:\Media\Ocean.mkv",
            @"C:\Media\City.mkv",
            @"C:\Media\P81.mkv",
            @"C:\Media\Sdr.mkv"
        ]);

        Assert.HasCount(5, model.Files);
        Assert.IsTrue(model.Files[0].IsSelected, "Mountain (MEL) must be selected.");
        Assert.IsTrue(model.Files[1].IsSelected, "Ocean (Simple FEL) must be selected.");
        Assert.IsFalse(model.Files[2].IsSelected, "City (Complex FEL) must be deselected.");
        Assert.IsFalse(model.Files[3].IsSelected, "P81 (Profile 8.1) must be deselected.");
        Assert.IsFalse(model.Files[4].IsSelected, "Sdr (No DV) must be deselected.");

        Assert.AreEqual("5 items | 2 items selected", model.SelectionSummary);
        Assert.IsNull(model.AllFilesSelected);
    }

    [TestMethod]
    public async Task ScanCancellationDeselectsCancelledAndUnprocessedRows()
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
        Assert.IsTrue(model.IsBusy);
        runtime.DuringAnalysis = null;
        model.CancelCommand.Execute();
        await adding.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(model.IsIdle);
        Assert.IsFalse(model.Files[0].IsSelected, "Cancelled active row must be deselected.");
        Assert.IsFalse(model.Files[1].IsSelected, "Unprocessed pending row must be deselected.");
        Assert.AreEqual("2 items", model.SelectionSummary);
        Assert.IsFalse(model.AllFilesSelected);
    }

    [TestMethod]
    public async Task InspectIncompletePromotesRowToSelectedWhenMel()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = model.Files[0];

        var evidence = row.Analysis!.Evidence with
        {
            SuccessfulSamples = 9,
            SampleDiagnostics = "Sample 10/10 at 01:39:51: No RPU was found in input file"
        };
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(row.Analysis.Media, evidence);
        row.IsSelected = false;

        Assert.IsTrue(row.CanInspectIncomplete);
        Assert.IsFalse(row.IsSelected);

        await model.InspectIncompleteCommand.ExecuteAsync(row);

        Assert.AreEqual(DoViFixer.Domain.Analysis.AnalysisVerdict.Mel, row.Analysis!.Verdict);
        Assert.IsTrue(row.IsSelected, "Promoting incomplete scan to MEL via inspection must select the row.");
        Assert.AreEqual("1 item | 1 item selected", model.SelectionSummary);
    }

    [TestMethod]
    public async Task CachedScanResultsApplyIdenticalAutoSelection()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\City.mkv"]);
        Assert.IsTrue(model.Files[0].IsSelected);
        Assert.IsFalse(model.Files[1].IsSelected);

        model.ClearAllCommand.Execute();
        runtime.Ready = false;

        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\City.mkv"]);
        Assert.IsTrue(model.Files[0].IsSelected, "Cached MEL must be selected.");
        Assert.IsFalse(model.Files[1].IsSelected, "Cached Complex FEL must be deselected.");
    }

    [TestMethod]
    public async Task DisabledAutoSelectPreservesInitialSelection()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.Resolve<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.IsTrue(settings.AutoSelectAfterScan);
        settings.AutoSelectAfterScan = false;
        await settings.SaveCommand.ExecuteAsync();
        Assert.IsFalse(runtime.Settings.AutoSelectAfterScan);

        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\City.mkv"]);
        Assert.IsTrue(model.Files[0].IsSelected, "Mountain must remain selected when auto-select is disabled.");
        Assert.IsTrue(model.Files[1].IsSelected, "City must remain selected when auto-select is disabled.");
        Assert.AreEqual("2 items | 2 items selected", model.SelectionSummary);
        Assert.IsTrue(model.AllFilesSelected);
    }

    [TestMethod]
    public async Task AddingFilesPreservesExistingRowSelectionAndFocus()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);

        model.Files[0].IsSelected = false;
        model.Focused = model.Files[0];

        await model.AddAsync([@"C:\Media\City.mkv"]);

        Assert.HasCount(3, model.Files);
        Assert.IsFalse(model.Files[0].IsSelected, "Existing Mountain row selection must not change.");
        Assert.IsTrue(model.Files[1].IsSelected, "Existing Ocean row selection must not change.");
        Assert.IsFalse(model.Files[2].IsSelected, "New City row must be deselected based on scan result.");
        Assert.AreSame(model.Files[0], model.Focused, "Focused row must remain unchanged when adding files.");
    }

    [TestMethod]
    public async Task PrepareAsyncReportsBatchProgressAndFileStatus()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);

        int analysesBefore = runtime.Analyses;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            if (runtime.Analyses == analysesBefore + 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };

        var convertTask = model.ConvertCommand.ExecuteAsync();
        await started.Task;

        Assert.IsTrue(model.IsBusy);
        Assert.AreEqual("Conversion planning", model.BatchProgress.Operation);
        Assert.AreEqual(2, model.BatchProgress.Total);
        Assert.AreEqual(0, model.BatchProgress.Processed);
        Assert.IsTrue(model.Files[0].IsActive);
        Assert.IsTrue(model.Files[1].IsPending);
        Assert.AreEqual("Queued", model.Files[1].Status);

        release.SetResult();
        await convertTask;

        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual(2, model.BatchProgress.Processed);
        Assert.AreEqual("Ready to convert", model.Files[0].Status);
        Assert.AreEqual("Ready to convert", model.Files[1].Status);
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task PrepareAsyncSupportsPerFileCancellationAndSkip()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        model.Files[2].IsSelected = true;

        int analysesBefore = runtime.Analyses;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            if (runtime.Analyses == analysesBefore + 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };

        var convertTask = model.ConvertCommand.ExecuteAsync();
        await started.Task;

        model.SkipCommand.Execute(model.Files[2]);
        Assert.AreEqual("Conversion planning skipped", model.Files[2].Status);

        model.CancelFileCommand.Execute(model.Files[0]);
        release.SetResult();
        await convertTask;

        Assert.AreEqual("Conversion planning cancelled", model.Files[0].Status);
        Assert.AreEqual("Ready to convert", model.Files[1].Status);
        Assert.AreEqual("Conversion planning skipped", model.Files[2].Status);
    }

    [TestMethod]
    public async Task PrepareAsyncDetectsOutputCollisionsAcrossBatch()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\FolderA\Movie.mkv", @"C:\FolderB\Movie.mkv"]);
        model.OtherFolder = true;
        model.Destination = @"C:\Output";

        await model.ReviewCommand.ExecuteAsync();

        Assert.AreEqual("Ready to convert", model.Files[0].Status);
        Assert.AreEqual("Conversion planning failed", model.Files[1].Status);
        Assert.IsTrue(model.Files[1].AnalysisError!.Contains("Multiple inputs resolve to the same output"));
    }

    [TestMethod]
    public async Task SelectionSummaryFormatsPluralAndHidesWhenNoneSelected()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();

        Assert.AreEqual("0 items", model.SelectionSummary);

        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        model.Files[0].IsSelected = false;
        Assert.AreEqual("1 item", model.SelectionSummary);

        model.Files[0].IsSelected = true;
        Assert.AreEqual("1 item | 1 item selected", model.SelectionSummary);

        await model.AddAsync([@"C:\Media\Ocean.mkv", @"C:\Media\City.mkv", @"C:\Media\P81.mkv", @"C:\Media\Sdr.mkv"]);
        // 5 total: Mountain(selected), Ocean(selected), City(deselected), P81(deselected), Sdr(deselected)
        Assert.AreEqual("5 items | 2 items selected", model.SelectionSummary);

        model.Files[1].IsSelected = false;
        Assert.AreEqual("5 items | 1 item selected", model.SelectionSummary);

        model.Files[0].IsSelected = false;
        Assert.AreEqual("5 items", model.SelectionSummary);
    }

    [TestMethod]
    public async Task ActiveProgressPropertiesReflectBatchAndNonBatchOperations()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();

        Assert.IsFalse(model.IsBusy);
        Assert.IsFalse(model.BatchProgress.IsRunning);
        Assert.AreEqual(0, model.ActiveProgressPercent);
        Assert.IsFalse(model.ActiveProgressIndeterminate);
        Assert.AreEqual("Ready", model.ActiveProgressSummary);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var task = model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(model.IsBusy);
        Assert.IsTrue(model.BatchProgress.IsRunning);
        Assert.AreEqual("Scan", model.ActiveProgressTitle);
        Assert.AreEqual("0%", model.ActiveProgressText);
        Assert.AreEqual(0, model.ActiveProgressPercent);
        Assert.IsFalse(model.ActiveProgressIndeterminate);
        Assert.AreEqual("0 of 2 files processed", model.ActiveProgressSummary);
        Assert.IsNotNull(model.ActiveProgressToolTip);

        runtime.DuringAnalysis = null;
        model.CancelCommand.Execute();
        await task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsFalse(model.IsBusy);
        Assert.IsFalse(model.BatchProgress.IsRunning);
        Assert.IsNull(model.ActiveProgressToolTip);
    }
}

