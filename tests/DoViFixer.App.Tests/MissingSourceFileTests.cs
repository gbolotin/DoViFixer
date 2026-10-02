using DoViFixer.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class MissingSourceFileTests
{
    private const string Mountain = @"C:\Media\Mountain.mkv";
    private const string Ocean = @"C:\Media\Ocean.mkv";

    [TestMethod]
    public async Task DeletedFileIsMarkedMissingAndLeftOutOfOperations()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean]);
        CollectionAssert.AreEquivalent(new[] { Mountain, Ocean }, runtime.WatchedPaths.ToArray());
        var row = model.Files[0];
        model.Focused = row;
        Assert.IsTrue(row.IsSelected);

        runtime.MissingFiles.Add(Mountain);
        runtime.RaiseSourceFilesChanged();

        Assert.IsTrue(row.IsMissing);
        Assert.AreEqual(MediaRow.MissingStatus, row.Status);
        Assert.AreEqual(MediaRow.MissingNote, row.StatusToolTip);
        StringAssert.Contains(row.DetailNotes, MediaRow.MissingNote);
        Assert.IsNotNull(row.Analysis, "The last scan result stays visible.");
        Assert.IsFalse(row.IsSelected);
        Assert.IsFalse(row.SelectionEnabled);
        Assert.IsFalse(model.ConvertRowDv81Command.CanExecute(row));
        Assert.AreEqual("File not found.", model.PreviewStatus);
        Assert.IsTrue(model.HasMissingFiles);

        model.ToggleSelectAllCommand.Execute();
        Assert.IsFalse(row.IsSelected);
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(1, model.BatchProgress.Processed);
        Assert.AreEqual(MediaRow.MissingStatus, row.Status);
    }

    [TestMethod]
    public async Task RestoredFileReturnsToItsPreviousState()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain]);
        var row = model.Files[0];
        string status = row.Status;
        runtime.MissingFiles.Add(Mountain);
        runtime.RaiseSourceFilesChanged();

        runtime.MissingFiles.Clear();
        runtime.RaiseSourceFilesChanged();

        Assert.IsFalse(row.IsMissing);
        Assert.AreEqual(status, row.Status);
        Assert.IsTrue(row.SelectionEnabled);
        Assert.IsFalse(model.HasMissingFiles);
        Assert.IsTrue(model.ConvertRowDv81Command.CanExecute(row));
    }

    [TestMethod]
    public async Task WindowActivationFindsFilesTheWatcherMissed()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var model = shell.Pages.OfType<MediaViewModel>().Single();
        await model.AddAsync([Mountain, Ocean]);
        runtime.MissingFiles.Add(Ocean);

        shell.WindowActivated();

        Assert.IsFalse(model.Files[0].IsMissing);
        Assert.IsTrue(model.Files[1].IsMissing);
    }

    [TestMethod]
    public async Task QueuedFileDeletedDuringBatchIsSkippedInsteadOfFailed()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        };

        var scan = model.ScanCommand.ExecuteAsync();
        await started.Task;
        runtime.MissingFiles.Add(Ocean);
        runtime.RaiseSourceFilesChanged();
        Assert.IsTrue(model.Files[1].IsMissing);
        Assert.IsFalse(model.Files[1].IsPending);
        release.SetResult();
        await scan;

        Assert.AreEqual(MediaRow.MissingStatus, model.Files[1].Status);
        Assert.IsNull(model.Files[1].AnalysisError);
        Assert.AreEqual("Scan: 1 Completed · 1 Skipped", model.StatusMessage);
    }

    [TestMethod]
    public async Task FileDeletedWithoutNoticeIsSkippedWhenItsTurnComes()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean]);
        runtime.MissingFiles.Add(Ocean);

        await model.ScanCommand.ExecuteAsync();

        Assert.IsTrue(model.Files[1].IsMissing);
        Assert.IsNull(model.Files[1].AnalysisError);
        Assert.AreEqual("Scan: 1 Completed · 1 Skipped", model.StatusMessage);
    }

    [TestMethod]
    public async Task RunningJobIsNotMarkedMissingAndConvertedRowKeepsItsResult()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { ReplaceOriginal = true, OutputDirectory = @"C:\Output" }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain]);
        var row = model.Files[0];
        bool missingWhileRunning = true;
        runtime.DuringConversion = _ =>
        {
            // Replace original moves the source away while the job holds it.
            runtime.MissingFiles.Add(Mountain);
            runtime.RaiseSourceFilesChanged();
            missingWhileRunning = row.IsMissing;
            return Task.CompletedTask;
        };

        await model.ConvertDv81Command.ExecuteAsync();

        Assert.IsFalse(missingWhileRunning);
        Assert.IsTrue(row.IsMissing);
        Assert.AreEqual("Converted", row.Status);
        Assert.IsTrue(row.CanOpenResult);
    }

    [TestMethod]
    public async Task RemoveMissingRemovesOnlyMissingFiles()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean]);
        Assert.IsFalse(model.RemoveMissingCommand.CanExecute());
        runtime.MissingFiles.Add(Mountain);
        runtime.RaiseSourceFilesChanged();

        model.RemoveMissingCommand.Execute();

        Assert.AreEqual(Ocean, model.Files.Single().Path);
        Assert.IsFalse(model.HasMissingFiles);
        CollectionAssert.AreEqual(new[] { Ocean }, runtime.WatchedPaths.ToArray());
    }
}
