using System.ComponentModel;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Presentation.Application;
using DoViFixer.App.Presentation.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;
[TestClass]
public sealed class ViewModelTests
{
    [TestMethod]
    [DataRow(61.2710416, "00:01:01")]
    [DataRow(90061d, "25:01:01")]
    [DataRow(double.NaN, "Unknown")]
    public async Task MediaDetailsUseLabeledRowsAndKeepDiagnostics(double seconds, string expectedLength)
    {
        using var runtime = new TestRuntime();
        const string path = @"C:\Media\Mountain.mkv";
        var row = new MediaRow(path);
        Assert.AreEqual(path, row.DetailRows.Single().Value);
        var media = (await runtime.ProbeAsync(path, default)) with { DurationSeconds = seconds };
        var changes = new List<string?>();
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu, DoViFixer.Domain.Media.EnhancementLayer.Mel, 390, 900, 10, 10));
        var details = row.DetailRows.ToDictionary();
        Assert.AreEqual(expectedLength, details["Length"]);
        Assert.AreEqual("Matroska", details["Type"]);
        Assert.AreEqual(path, details["File location"]);
        Assert.AreEqual("3840 × 2160", details["Resolution"]);
        Assert.AreEqual("10/10", details["Samples"]);
        CollectionAssert.Contains(changes, nameof(MediaRow.DetailRows));
        row.Notice = "Keep this warning visible.";
        row.AnalysisError = "Fixture analysis error.";
        StringAssert.Contains(row.DetailNotes, row.Notice);
        StringAssert.Contains(row.DetailNotes, row.AnalysisError);
        CollectionAssert.Contains(changes, nameof(MediaRow.DetailNotes));
    }

    [TestMethod]
    [DataRow(0L, 0L, "Cache: 0 B")]
    [DataRow(25L, 50L, "Cache: 75 B")]
    [DataRow(512L, 512L, "Cache: 1 KiB")]
    [DataRow(1048576L, 1048576L, "Cache: 2 MiB")]
    [DataRow(536870912L, 536870912L, "Cache: 1 GiB")]
    public async Task SettingsShowsCombinedCacheSizeAndRefreshesAfterClearing(long analysisBytes, long frameBytes, string expected)
    {
        using var runtime = new TestRuntime { AnalysisCacheBytes = analysisBytes, FrameCacheBytes = frameBytes };
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.AreEqual(expected, settings.CacheSizeText);
        Assert.AreEqual(runtime.RootDirectory, settings.CacheDirectory);
        await settings.ClearCacheCommand.ExecuteAsync();
        Assert.AreEqual("Cache: 0 B", settings.CacheSizeText);
        runtime.FrameCacheBytes = 1024;
        await settings.LoadCommand.ExecuteAsync();
        Assert.AreEqual("Cache: 1 KiB", settings.CacheSizeText);
    }

    [TestMethod]
    public async Task DroppedPathsIgnoreUnsupportedFilesBeforeDiscovery()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        runtime.Folders.Add(@"C:\Media\Folder.with.dots");
        Assert.IsTrue(model.AddDroppedPathsCommand.CanExecute(new[] { @"C:\Media\Movie.MKV", @"C:\Media\Folder.with.dots" }));
        int discoveries = 0;
        runtime.DiscoverFiles = _ => { discoveries++; return []; };
        foreach (string[] paths in new string[][] { [], [""], [@"C:\Media\Movie.mp4"], [@"C:\Media\Movie"] })
        {
            Assert.IsFalse(model.AddDroppedPathsCommand.CanExecute(paths));
            await model.AddDroppedPathsCommand.ExecuteAsync(paths);
        }
        Assert.AreEqual(0, discoveries);
        Assert.IsEmpty(model.Files);
        Assert.AreEqual(ViewStatus.Ready, model.Status);

        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var discovered = new List<string>();
        runtime.DiscoverFiles = input =>
        {
            discovered.Add(input);
            return [input.EndsWith("Folder.with.dots") ? @"C:\Media\Folder.with.dots\Nested.mkv" : input];
        };
        string[] mixed = [@"C:\Media\Movie.srt", @"C:\Media\Movie.MKV", @"C:\Media\Folder.with.dots", @"C:\Media\Movie.mp4"];
        Assert.IsTrue(model.AddDroppedPathsCommand.CanExecute(mixed));
        await model.AddDroppedPathsCommand.ExecuteAsync(mixed);
        CollectionAssert.AreEqual(new[] { mixed[1], mixed[2] }, discovered.ToArray());
        CollectionAssert.AreEqual(new[] { mixed[1], @"C:\Media\Folder.with.dots\Nested.mkv" }, model.Files.Select(row => row.Path).ToArray());
        Assert.AreEqual(ViewStatus.Ready, model.Status);
    }

    [TestMethod]
    public async Task SortingByColumnOrdersTheDisplayAndTogglesDirectionWithoutReorderingFiles()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Movie10.mkv", @"C:\Media\movie2.mkv", @"C:\Media\Movie1.mkv"]);
        var added = model.Files.ToArray();
        string[] Names() => [.. model.FileSort.Order(model.Files).Select(row => row.Name)];
        Assert.IsNull(model.FileSort.Column);
        Assert.IsNull(model.FileSort.Comparer);
        CollectionAssert.AreEqual(new[] { "Movie10.mkv", "movie2.mkv", "Movie1.mkv" }, Names(), "Unsorted lists keep the order files were added.");

        var changes = new List<string?>();
        model.FileSort.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        model.FileSort.SortBy(MediaSortColumn.Name);
        CollectionAssert.Contains(changes, nameof(ColumnSort<MediaRow>.Comparer));
        CollectionAssert.AreEqual(new[] { "Movie1.mkv", "movie2.mkv", "Movie10.mkv" }, Names(), "Names use case-insensitive natural order.");
        CollectionAssert.AreEqual(added, model.Files.ToArray(), "Sorting changes only the displayed order.");
        Assert.AreEqual(ListSortDirection.Ascending, model.FileSort.Direction);

        model.FileSort.SortBy(MediaSortColumn.Name);
        CollectionAssert.AreEqual(new[] { "Movie10.mkv", "movie2.mkv", "Movie1.mkv" }, Names());
        Assert.AreEqual(ListSortDirection.Descending, model.FileSort.Direction);

        await model.AddAsync([@"C:\Media\Movie3.mkv"]);
        Assert.AreEqual("Movie3.mkv", model.Files[^1].Name, "New files are appended to the source collection.");
        CollectionAssert.AreEqual(new[] { "Movie10.mkv", "Movie3.mkv", "movie2.mkv", "Movie1.mkv" }, Names());

        model.Files.Single(row => row.Name == "Movie3.mkv").Status = "Converted";
        model.Files.Single(row => row.Name == "Movie1.mkv").Status = "";
        model.Files.Single(row => row.Name == "Movie10.mkv").Status = "Analysis failed";
        model.FileSort.SortBy(MediaSortColumn.Status);
        Assert.AreEqual(MediaSortColumn.Status, model.FileSort.Column);
        Assert.AreEqual(ListSortDirection.Ascending, model.FileSort.Direction, "A new column starts ascending.");
        CollectionAssert.AreEqual(new[] { "Movie10.mkv", "Movie3.mkv", "movie2.mkv", "Movie1.mkv" }, Names());
        model.FileSort.SortBy(MediaSortColumn.Status);
        CollectionAssert.AreEqual(new[] { "movie2.mkv", "Movie3.mkv", "Movie10.mkv", "Movie1.mkv" }, Names(), "Blank values stay last.");

        model.Files.Single(row => row.Name == "movie2.mkv").AnalysisError = "Fixture failure.";
        model.FileSort.SortBy(MediaSortColumn.Classification);
        Assert.AreEqual("movie2.mkv", Names()[0]);
        CollectionAssert.AreEqual(new[] { "Movie1.mkv", "Movie3.mkv", "Movie10.mkv" }, Names()[1..], "Ties fall back to the file name.");
    }

    [TestMethod]
    public async Task BatchesProcessFilesInDisplayedOrder()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Movie10.mkv", @"C:\Media\movie2.mkv", @"C:\Media\Movie1.mkv"]);
        var processed = new List<string>();
        foreach (var row in model.Files)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MediaRow.IsActive) && row.IsActive)
                {
                    processed.Add(row.Name);
                }
            };
        }

        await model.ScanCommand.ExecuteAsync();
        CollectionAssert.AreEqual(new[] { "Movie10.mkv", "movie2.mkv", "Movie1.mkv" }, processed, "Unsorted lists keep the order files were added.");

        model.FileSort.SortBy(MediaSortColumn.Name);
        processed.Clear();
        await model.ScanCommand.ExecuteAsync();
        CollectionAssert.AreEqual(new[] { "Movie1.mkv", "movie2.mkv", "Movie10.mkv" }, processed);

        model.FileSort.SortBy(MediaSortColumn.Name);
        foreach (var row in model.Files)
        {
            row.IsSelected = row.Name != "Movie10.mkv";
        }
        processed.Clear();
        await model.InspectCommand.ExecuteAsync();
        CollectionAssert.AreEqual(new[] { "movie2.mkv", "Movie1.mkv" }, processed, "Selected-file batches follow the sort too.");

        model.FileSort.SortBy(MediaSortColumn.Status);
        var sortedOrder = model.FileSort.Comparer;
        model.Files.Single(row => row.Name == "Movie1.mkv").Status = "Needs attention";
        processed.Clear();
        await model.ScanCommand.ExecuteAsync();
        Assert.AreNotSame(sortedOrder, model.FileSort.Comparer,"Starting a batch re-applies the sort so the screen matches the processing order.");
        CollectionAssert.AreEqual(new[] { "Movie1.mkv", "movie2.mkv", "Movie10.mkv" }, processed, "Blank statuses stay last and fall back to name order.");
    }

    [TestMethod]
    public async Task SortingIsIgnoredWhileABatchIsRunning()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Movie2.mkv", @"C:\Media\Movie3.mkv", @"C:\Media\Movie1.mkv"]);
        var processed = new List<string>();
        foreach (var row in model.Files)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MediaRow.IsActive) && row.IsActive)
                {
                    processed.Add(row.Name);
                }
            };
        }

        model.FileSort.SortBy(MediaSortColumn.Name);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var scanning = model.ScanCommand.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var comparer = model.FileSort.Comparer;

        // A header click calls SortBy; reversing the display mid-batch would no longer match processing.
        model.FileSort.SortBy(MediaSortColumn.Name);
        model.FileSort.SortBy(MediaSortColumn.Status);
        Assert.AreEqual(MediaSortColumn.Name, model.FileSort.Column);
        Assert.AreEqual(ListSortDirection.Ascending, model.FileSort.Direction);
        Assert.AreSame(comparer, model.FileSort.Comparer, "The displayed order must not change while a batch runs.");

        release.TrySetResult();
        await scanning.WaitAsync(TimeSpan.FromSeconds(10));
        CollectionAssert.AreEqual(new[] { "Movie1.mkv", "Movie2.mkv", "Movie3.mkv" }, processed);

        model.FileSort.SortBy(MediaSortColumn.Name);
        Assert.AreEqual(ListSortDirection.Descending, model.FileSort.Direction, "Sorting works again once the batch ends.");
    }

    [TestMethod]
    public async Task RemoveFileUpdatesSelectionAndDetailsAndRejectsRemovalWhileBusy()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var first = model.Files[0];
        var second = model.Files[1];
        first.IsSelected = false;
        model.Focused = first;
        await runtime.ClearAsync(default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var scanning = model.ScanCommand.ExecuteAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(model.RemoveFileCommand.CanExecute(first));
            model.RemoveFileCommand.Execute(first);
            Assert.HasCount(2, model.Files);
        }
        finally
        {
            model.CancelCommand.Execute();
            await scanning.WaitAsync(TimeSpan.FromSeconds(10));
        }

        second.IsSelected = true;
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.IsTrue(model.RemoveFileCommand.CanExecute(first));
        model.RemoveFileCommand.Execute(first);
        Assert.AreSame(second, model.Files.Single());
        Assert.AreSame(second, model.Focused);
        Assert.AreEqual("1 item | 1 item selected", model.SelectionSummary);
        Assert.AreEqual(true, model.AllFilesSelected);
        CollectionAssert.Contains(changes, nameof(MediaViewModel.SelectionSummary));
        CollectionAssert.Contains(changes, nameof(MediaViewModel.AllFilesSelected));
        Assert.IsFalse(model.RemoveFileCommand.CanExecute(first));
        changes.Clear();
        first.IsSelected = !first.IsSelected;
        Assert.IsEmpty(changes, "Removed rows must no longer notify the page.");

        model.RemoveFileCommand.Execute(second);
        Assert.IsTrue(model.IsFileListEmpty);
        Assert.IsNull(model.Focused);
        Assert.AreEqual(ViewStatus.FileListCleared, model.Status);
        Assert.IsFalse(model.ScanCommand.CanExecute(null));
    }

    [TestMethod]
    public void EmptyFileListNotifiesBindingsOnAddRemoveAndClear()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        var states = new List<bool>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MediaViewModel.IsFileListEmpty))
            {
                states.Add(model.IsFileListEmpty);
            }
        };

        Assert.IsTrue(model.IsFileListEmpty);
        model.Files.Add(new MediaRow(@"C:\Media\Mountain.mkv"));
        model.Files.RemoveAt(0);
        model.Files.Add(new MediaRow(@"C:\Media\Ocean.mkv"));
        model.ClearAllCommand.Execute();

        CollectionAssert.AreEqual(new[] { false, true, false, true }, states);
        Assert.IsTrue(model.IsFileListEmpty);
    }

    [TestMethod]
    public async Task ShellNavigationLoadsSettingsAndRetainsPageState()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(value => value with { IncludeSimple = true }, default);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        var archive = shell.Pages.OfType<ArchiveViewModel>().Single();
        Assert.AreSame(media, shell.CurrentPage);
        await media.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = media.Files.Single();
        row.IsSelected = false;
        archive.Input = row.Path;

        media.OpenSettingsCommand.Execute();
        await shell.Settings.Completion;
        Assert.AreSame(shell.Settings, shell.CurrentPage);
        Assert.IsTrue(shell.Settings.IncludeSimple);
        shell.CurrentPage = archive;
        Assert.AreEqual(row.Path, archive.Input);
        shell.CurrentPage = media;
        Assert.AreSame(row, media.Files.Single());
        Assert.AreSame(row, media.Focused);
        Assert.IsFalse(row.IsSelected);
        Assert.AreEqual(1, runtime.Analyses, "Navigation must not launch a new scan.");
    }

    [TestMethod]
    public async Task ShellRejectsNavigationDuringOperationsAndUntilSaveErrorIsDiscarded()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.CurrentPage;
        var archive = shell.Pages.OfType<ArchiveViewModel>().Single();
        runtime.DuringDependencyCheck = token => Task.Delay(Timeout.Infinite, token);
        var checking = shell.Settings.CheckCommand.ExecuteAsync();
        try
        {
            Assert.IsFalse(shell.CanNavigate);
            shell.CurrentPage = archive;
            Assert.AreSame(media, shell.CurrentPage);
        }
        finally
        {
            shell.Settings.CancelCommand.Execute();
            await checking.WaitAsync(TimeSpan.FromSeconds(10));
        }

        shell.CurrentPage = shell.Settings;
        await shell.Settings.Completion;
        shell.Settings.OtherFolder = true;
        await shell.Settings.SaveTask;
        shell.CurrentPage = archive;
        Assert.AreSame(shell.Settings, shell.CurrentPage);
        Assert.IsFalse(shell.CanNavigate);
        shell.Settings.DiscardChangesCommand.Execute();
        shell.CurrentPage = archive;
        Assert.AreSame(archive, shell.CurrentPage);
        Assert.IsTrue(shell.CanNavigate);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PauseBatchCommandWaitsBetweenJobsAndResetsAfterCompletion(bool cancelBatch)
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        Assert.IsFalse(model.PauseBatchCommand.CanExecute());
        Assert.AreEqual("\uE769", model.PauseBatchIcon);

        var adding = model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.ActiveProgressSummary) && model.ActiveProgressSummary.EndsWith("· Paused"))
            {
                paused.TrySetResult();
            }
        };
        Assert.IsTrue(model.PauseBatchCommand.CanExecute());
        model.PauseBatchCommand.Execute();
        Assert.AreEqual("Resume", model.PauseBatchText);
        Assert.AreEqual("\uE768", model.PauseBatchIcon);
        StringAssert.Contains(model.ActiveProgressSummary, "Pausing after current job");
        release.SetResult();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, runtime.Analyses);
        Assert.IsFalse(adding.IsCompleted);

        if (cancelBatch)
        {
            model.CancelCommand.Execute();
        }
        else
        {
            model.PauseBatchCommand.Execute();
        }

        await adding.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(cancelBatch ? 1 : 2, runtime.Analyses);
        Assert.AreEqual("Pause", model.PauseBatchText);
        Assert.AreEqual("\uE769", model.PauseBatchIcon);
        Assert.IsFalse(model.PauseBatchCommand.CanExecute());
        Assert.IsTrue(model.IsIdle);
    }

    [TestMethod]
    public async Task IncompleteScanExplainsSampleFailureAndInspectsOnlyClickedRow()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var row = model.Files[0];
        var evidence = row.Analysis!.Evidence with
        {
            SuccessfulSamples = 9,
            SampleDiagnostics = "Sample 10/10 at 01:39:51: No RPU was found in input file"
        };
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(row.Analysis.Media, evidence);
        Assert.AreEqual("Profile 7 · Incomplete scan", row.Classification);
        Assert.AreEqual("9/10", row.DetailRows.Single(detail => detail.Key == "Samples").Value);
        Assert.IsTrue(row.DetailNotes.Contains("No RPU was found in input file"));
        Assert.IsTrue(row.DetailNotes.Contains("Suggested action: Inspect"));
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
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
        Assert.IsTrue(row.DetailNotes.Contains("Fixture analysis failure"));
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
    public async Task OpenFileLocationShowsFocusedFileInFolder()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\One\Mountain.mkv", @"C:\Media\Two\Ocean.mkv"]);
        model.Focused = null;
        Assert.IsFalse(model.OpenFileLocationCommand.CanExecute());
        model.Focused = model.Files[1];
        Assert.IsTrue(model.OpenFileLocationCommand.CanExecute());
        model.OpenFileLocationCommand.Execute();
        Assert.AreEqual(@"C:\Media\Two\Ocean.mkv", runtime.ShownFile);
    }

    [TestMethod]
    public async Task OpenFolderUsesClickedRowRatherThanFocusedRow()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\One\Mountain.mkv", @"C:\Media\Two\Ocean.mkv"]);
        await model.ConvertDv81Command.ExecuteAsync();
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
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
        Assert.IsTrue(model.Files[0].IsSelected, "Cancelling a scan must preserve its checkbox selection.");
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
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.IsTrue(settings.AutomaticallyScanAddedFiles);
        Assert.IsTrue(settings.UseCachedResults);
        settings.AutomaticallyScanAddedFiles = false;
        settings.UseCachedResults = false;
        await settings.SaveCommand.ExecuteAsync();
        Assert.IsFalse(runtime.Settings.AutomaticallyScanAddedFiles);
        Assert.IsFalse(runtime.Settings.UseCachedResults);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
        runtime.CachedFrames = 2;
        await settings.ClearCacheCommand.ExecuteAsync();
        Assert.AreEqual(0, runtime.CachedFrames);
        StringAssert.Contains(settings.StatusMessage, "2 frame previews");
        await model.ScanCommand.ExecuteAsync();
        Assert.AreEqual(3, runtime.Analyses);
    }

    [TestMethod]
    public async Task SettingsLoadsAndAppliesThemeSelection()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.System, settings.SelectedTheme);
        Assert.IsTrue(settings.IsSystemTheme);
        Assert.IsFalse(settings.IsLightTheme);
        Assert.IsFalse(settings.IsDarkTheme);
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.System, runtime.AppliedTheme);

        settings.SelectedTheme = DoViFixer.Application.Settings.AppTheme.Dark;
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Dark, settings.SelectedTheme);
        Assert.IsFalse(settings.IsSystemTheme);
        Assert.IsFalse(settings.IsLightTheme);
        Assert.IsTrue(settings.IsDarkTheme);
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Dark, runtime.AppliedTheme);

        await settings.SaveCommand.ExecuteAsync();
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Dark, runtime.Settings.Theme);

        settings.IsLightTheme = true;
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Light, settings.SelectedTheme);
        Assert.IsTrue(settings.IsLightTheme);
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Light, runtime.AppliedTheme);

        await settings.SaveCommand.ExecuteAsync();
        var newSettings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings.LoadCommand.ExecuteAsync();
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Light, newSettings.SelectedTheme);
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Light, runtime.AppliedTheme);
    }

    [TestMethod]
    public async Task ClearedRowsCannotInvalidateNewConversionPlans()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var removedRow = model.Files.Single();
        model.ClearAllCommand.Execute();
        Assert.HasCount(0, model.Files);
        Assert.IsNull(model.Focused);

        await model.AddAsync([@"C:\Media\Mountain2.mkv"]);
        Assert.IsTrue(model.ConvertDv81Command.CanExecute(null));

        removedRow.IsSelected = false;
        Assert.IsTrue(model.ConvertDv81Command.CanExecute(null));

        model.Files.Single().IsSelected = false;
        Assert.IsFalse(model.ConvertDv81Command.CanExecute(null));
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
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        Assert.AreEqual(installs, runtime.Installations);
        Assert.AreEqual(probes, runtime.Probes);
        Assert.AreEqual(0, runtime.Conversions);
        Assert.IsTrue(runtime.Reviews[0].Contains("Fixture.Tools 1.2.3"));
        Assert.IsTrue(runtime.Reviews[0].Contains("SHA-256"));
    }

    [TestMethod]
    [DataRow(true, false, true)]
    [DataRow(false, false, true)]
    [DataRow(false, true, true)]
    [DataRow(false, true, false)]
    public async Task StartupChecksDependenciesAndRequiresInstallationApproval(bool ready, bool approve, bool succeeds)
    {
        using var runtime = new TestRuntime { Ready = ready, Approval = approve, InstallationSucceeds = succeeds };
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync();
        bool missing = !ready && !(approve && succeeds);
        Assert.AreEqual(missing, shell.Settings.Dependencies.HasWarning);
        Assert.AreEqual(missing, shell.StatusItems.Contains(shell.Settings.Dependencies));
        Assert.AreEqual(!ready && approve ? 1 : 0, runtime.Installations);
        Assert.AreEqual(6, shell.Settings.Dependencies.Tools.Count);
        Assert.IsTrue(shell.CanNavigate);
        if (!ready)
        {
            StringAssert.Contains(runtime.Reviews[0], "Installed and ready");
            StringAssert.Contains(runtime.Reviews[0], "Missing or needs attention");
            StringAssert.Contains(runtime.Reviews[0], DependencySetup.ToolDescriptions);
            StringAssert.Contains(runtime.Reviews[0], "Source:");
            StringAssert.Contains(runtime.Reviews[0], "Scope:");
            StringAssert.Contains(runtime.Reviews[0], "elevation:");
        }
        else
        {
            Assert.IsEmpty(runtime.Reviews);
        }
        int reviews = runtime.Reviews.Count;
        await shell.InitializeAsync();
        Assert.AreEqual(reviews, runtime.Reviews.Count, "Startup must not prompt twice.");
    }

    [TestMethod]
    public async Task MissingDependencyWarningPersistsAcrossPagesAndClearsAfterOperationInstallsTools()
    {
        using var runtime = new TestRuntime();
        runtime.MissingTools.Add(DoViFixer.Application.Dependencies.NativeTool.FFmpeg);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync();
        StringAssert.Contains(runtime.Reviews.Single(), "FFprobe: Installed and ready");
        StringAssert.Contains(runtime.Reviews.Single(), "FFmpeg: Missing");
        shell.CurrentPage = shell.Settings;
        await shell.NavigationTask;
        Assert.IsTrue(shell.StatusItems.Contains(shell.Settings.Dependencies));
        shell.CurrentPage = shell.Pages.OfType<MediaViewModel>().Single();
        await shell.NavigationTask;
        Assert.IsTrue(shell.StatusItems.Contains(shell.Settings.Dependencies));
        runtime.Approval = true;
        await ((MediaViewModel)shell.CurrentPage).AddAsync([@"C:\Media\Mountain.mkv"]);
        Assert.AreEqual(1, runtime.Installations);
        Assert.IsFalse(shell.Settings.Dependencies.HasWarning);
        Assert.IsFalse(shell.StatusItems.Contains(shell.Settings.Dependencies));
        Assert.IsTrue(shell.Settings.Dependencies.Tools.All(tool => tool.State == DoViFixer.Application.Dependencies.DependencyState.Ready));
    }

    [TestMethod]
    public async Task ShutdownCancelsBackgroundDependencyCheckBeforePrompting()
    {
        using var runtime = new TestRuntime { Ready = false };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringDependencyCheck = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var initializing = shell.InitializeAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(initializing.IsCompleted, "The background check must not block the caller.");
        await shell.CancelAndWaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await initializing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsEmpty(runtime.Reviews);
        Assert.AreEqual(0, runtime.Installations);
    }

    [TestMethod]
    public async Task FailedStartupDependencyCheckWarnsAndCanBeRetried()
    {
        using var runtime = new TestRuntime();
        runtime.DuringDependencyCheck = _ => throw new IOException("Tool discovery failed");
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync();
        Assert.IsTrue(shell.Settings.Dependencies.HasWarning);
        StringAssert.Contains(shell.Settings.Dependencies.WarningDetails, "Tool discovery failed");
        Assert.AreEqual("Dependency check failed", shell.Settings.Dependencies.WarningText);
        Assert.IsTrue(shell.CanNavigate);
        runtime.DuringDependencyCheck = null;
        await shell.Settings.CheckCommand.ExecuteAsync();
        Assert.IsFalse(shell.Settings.Dependencies.HasWarning);
    }

    [TestMethod]
    public async Task StartupDependencyCheckDoesNotBlockNavigation()
    {
        using var runtime = new TestRuntime { Ready = false };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringDependencyCheck = async token =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var initializing = shell.InitializeAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(shell.CanNavigate);
        shell.CurrentPage = shell.Settings;
        await shell.NavigationTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreSame(shell.Settings, shell.CurrentPage);
        Assert.AreEqual(ViewStatus.SettingsLoaded, shell.Settings.Status);

        runtime.DuringDependencyCheck = null;
        release.TrySetResult();
        await initializing.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.HasCount(1, runtime.Reviews);
    }

    [TestMethod]
    public async Task StartupDoesNotShowUnavailableSetupDialog()
    {
        using var runtime = new TestRuntime { Ready = false, InstallationAvailable = false };
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync();
        Assert.IsEmpty(runtime.Reviews);
        Assert.IsTrue(shell.StatusItems.Contains(shell.Settings.Dependencies));

        await shell.Settings.InstallCommand.ExecuteAsync();
        Assert.HasCount(1, runtime.Reviews, "An explicit setup request still explains why nothing can be installed.");
    }

    [TestMethod]
    public async Task ConcurrentDependencyRequestsShareOneInstallationPrompt()
    {
        using var runtime = new TestRuntime { Ready = false };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int checks = 0;
        runtime.DuringDependencyCheck = async token =>
        {
            checks++;
            started.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var setup = runtime.Container.GetRequiredService<DependencySetup>();
        var progress = new Progress<DoViFixer.Application.Operations.OperationProgress>();

        var first = setup.EnsureAsync(progress, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = setup.EnsureAsync(progress, CancellationToken.None);
        release.TrySetResult();

        Assert.IsFalse(await first.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsFalse(await second.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.HasCount(1, runtime.Reviews);
        Assert.AreEqual(1, checks);

        Assert.IsFalse(await setup.EnsureAsync(progress, CancellationToken.None));
        Assert.HasCount(2, runtime.Reviews, "A later request must prompt again.");
    }

    [TestMethod]
    public async Task SavingToolPathSucceedsWhenRecheckFails()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        runtime.DuringDependencyCheck = _ => throw new IOException("Tool discovery failed");
        settings.ToolPath = @"C:\Tools\ffmpeg.exe";

        await settings.SetToolCommand.ExecuteAsync();

        Assert.AreEqual(ViewStatus.ToolPathSaved, settings.Status);
        Assert.IsTrue(settings.Dependencies.HasWarning);
        Assert.AreEqual("Dependency check failed", settings.Dependencies.WarningText);
    }

    [TestMethod]
    public async Task StatusItemsUpdateOnlyChangedEntries()
    {
        using var runtime = new TestRuntime { Ready = false };
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync();
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        var dependencyItem = shell.StatusItems.Last();
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedEventArgs>();
        shell.StatusItems.CollectionChanged += (_, e) => changes.Add(e);

        media.SetStatus(ViewStatus.Progress, "Scanning Mountain.mkv");

        var change = changes.Single();
        Assert.AreEqual(System.Collections.Specialized.NotifyCollectionChangedAction.Replace, change.Action);
        Assert.AreEqual(1, change.NewStartingIndex);
        Assert.AreSame(dependencyItem, shell.StatusItems.Last());
    }

    [TestMethod]
    public async Task PreferencesValidateBeforeSavingAndBlankPathsResetDefaults()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<DoViFixer.Application.Settings.SettingsService>();
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
    public async Task ConvertBatchWithoutPriorManualScanExecutesConversionDirectly()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv"]);
        Assert.AreEqual(0, runtime.FullAnalyses);
        Assert.AreEqual(0, runtime.Conversions);

        await model.ConvertDv81Command.ExecuteAsync();
        Assert.AreEqual(1, runtime.FullAnalyses);
        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files.Single().Status);
        Assert.AreEqual(MediaRowState.Converted, model.Files.Single().State);
        Assert.IsTrue(model.Files.Single().CanOpenResult);
        Assert.IsTrue(model.Files.Single().Result!.Output!.EndsWith(" - DV P8.1.mkv"));

        model.Files.Single().IsSelected = true;
        await model.ConvertHdrCommand.ExecuteAsync();
        Assert.AreEqual("Converted", model.Files.Single().Status);
        Assert.IsTrue(model.Files.Single().Result!.Output!.EndsWith(" - HDR10.mkv"));
    }

    [TestMethod]
    public async Task ConvertClearsWarningOnFelRowsAfterConversionWhenAllowFelIsEnabled()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        await model.ConvertDv81Command.ExecuteAsync();

        Assert.IsFalse(model.Files[0].HasWarning);
        Assert.IsNull(model.Files[0].Warning);
        Assert.AreEqual("Converted", model.Files[0].Status);

        Assert.IsFalse(model.Files[1].HasWarning);
        Assert.IsNull(model.Files[1].Warning);
        Assert.AreEqual("Converted", model.Files[1].Status);
    }

    [TestMethod]
    public async Task SelectionAndFocusAreIndependentAndCommandsUpdate()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        model.Focused = model.Files[1];
        Assert.IsTrue(model.ConvertDv81Command.CanExecute(null));
        model.Files[1].IsSelected = false;
        Assert.AreSame(model.Files[1], model.Focused);
        Assert.IsTrue(model.Files[0].IsSelected);
        Assert.IsTrue(model.ConvertDv81Command.CanExecute(null));
        model.Files[0].IsSelected = false;
        Assert.IsFalse(model.ConvertDv81Command.CanExecute(null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CleanupRequiresReviewAndDeletesOnlyReviewedFiles(bool approve)
    {
        using var runtime = new TestRuntime { Approval = approve, PickedFolder = @"C:\Media" };
        runtime.Archives.UnionWith([@"C:\Media\Movie.dovi", @"C:\Other\Keep.dovi"]);
        var model = runtime.Container.GetRequiredService<ArchiveViewModel>();

        await model.CleanupCommand.ExecuteAsync();

        Assert.AreEqual("Delete", runtime.LastApproveLabel);
        Assert.HasCount(1, runtime.Reviews);
        StringAssert.Contains(runtime.Reviews[0], @"C:\Media\Movie.dovi");
        if (approve)
        {
            CollectionAssert.AreEqual(new[] { @"C:\Media\Movie.dovi" }, runtime.DeletedFiles);
        }
        else
        {
            Assert.IsEmpty(runtime.DeletedFiles);
            Assert.AreEqual(ViewStatus.CleanupNotApproved, model.Status);
        }
    }

    [TestMethod]
    public async Task ArchivePreparationCannotExecuteWithoutDisplayedPlanApproval()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<ArchiveViewModel>();
        model.Input = @"C:\Media\Mountain.mkv";
        await model.BackupCommand.ExecuteAsync();
        Assert.HasCount(1, runtime.Reviews);
        Assert.IsTrue(runtime.Reviews[0].Contains(@"C:\Media\Mountain.dovi"));
        Assert.AreEqual(ViewStatus.BackupNotApproved, model.Status);
    }

    [TestMethod]
    public async Task ScanAutoSelectsMelOnlyWithDefaultSettings()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([
            @"C:\Media\Mountain.mkv",
            @"C:\Media\Ocean.mkv",
            @"C:\Media\City.mkv",
            @"C:\Media\P81.mkv",
            @"C:\Media\Sdr.mkv"
        ]);

        Assert.HasCount(5, model.Files);
        Assert.IsTrue(model.Files[0].IsSelected, "Mountain (MEL) must be selected.");
        Assert.IsFalse(model.Files[1].IsSelected, "Ocean (Simple FEL) must be deselected by default.");
        Assert.IsFalse(model.Files[2].IsSelected, "City (Complex FEL) must be deselected by default.");
        Assert.IsFalse(model.Files[3].IsSelected, "P81 (Profile 8.1) must be deselected.");
        Assert.IsFalse(model.Files[4].IsSelected, "Sdr (No DV) must be deselected.");

        Assert.AreEqual("5 items | 1 item selected", model.SelectionSummary);
        Assert.IsNull(model.AllFilesSelected);
    }

    [TestMethod]
    public async Task ScanAutoSelectsSimpleFelWhenIncludeSimpleEnabled()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { IncludeSimple = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([
            @"C:\Media\Mountain.mkv",
            @"C:\Media\Ocean.mkv",
            @"C:\Media\City.mkv"
        ]);

        Assert.IsTrue(model.Files[0].IsSelected, "Mountain (MEL) must be selected.");
        Assert.IsTrue(model.Files[1].IsSelected, "Ocean (Simple FEL) must be selected when IncludeSimple is true.");
        Assert.IsFalse(model.Files[2].IsSelected, "City (Complex FEL) must be deselected when ForceComplex is false.");
        Assert.AreEqual("3 items | 2 items selected", model.SelectionSummary);
    }

    [TestMethod]
    public async Task ScanAutoSelectsComplexFelWhenForceComplexEnabled()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { ForceComplex = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([
            @"C:\Media\Mountain.mkv",
            @"C:\Media\Ocean.mkv",
            @"C:\Media\City.mkv"
        ]);

        Assert.IsTrue(model.Files[0].IsSelected, "Mountain (MEL) must be selected.");
        Assert.IsFalse(model.Files[1].IsSelected, "Ocean (Simple FEL) must be deselected when IncludeSimple is false.");
        Assert.IsTrue(model.Files[2].IsSelected, "City (Complex FEL) must be selected when ForceComplex is true.");
        Assert.AreEqual("3 items | 2 items selected", model.SelectionSummary);
    }

    [TestMethod]
    public async Task ScanCommandProcessesAllFilesNotOnlySelected()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([
            @"C:\Media\Mountain.mkv",
            @"C:\Media\Ocean.mkv",
            @"C:\Media\City.mkv"
        ]);

        // Default: Mountain is selected, Ocean and City are deselected
        Assert.IsTrue(model.Files[0].IsSelected);
        Assert.IsFalse(model.Files[1].IsSelected);
        Assert.IsFalse(model.Files[2].IsSelected);

        // Explicitly deselect all files
        model.Files[0].IsSelected = false;
        Assert.IsFalse(model.Files.Any(f => f.IsSelected));

        // ScanCommand is executable even with 0 files selected!
        Assert.IsTrue(model.ScanCommand.CanExecute(null));

        // Turn on IncludeSimple and execute ScanCommand
        await runtime.UpdateAsync(s => s with { IncludeSimple = true }, default);
        await model.ScanCommand.ExecuteAsync();

        // All files were processed by ScanCommand:
        Assert.IsTrue(model.Files[0].IsSelected, "Mountain (MEL) must be auto-selected.");
        Assert.IsTrue(model.Files[1].IsSelected, "Ocean (Simple FEL) must be auto-selected.");
        Assert.IsFalse(model.Files[2].IsSelected, "City (Complex FEL) must be deselected.");
    }

    [TestMethod]
    public async Task ChangingFelSettingsKeepsScanningExplicit()
    {
        using var runtime = new TestRuntime();
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        var archive = runtime.Container.GetRequiredService<ArchiveViewModel>();
        var shell = new ShellViewModel(media, archive, settings, runtime.Container.GetRequiredService<StartupDependencyCheckViewModel>(), runtime.Container.GetRequiredService<DependencyReportViewModel>());

        await media.AddAsync([
            @"C:\Media\Mountain.mkv",
            @"C:\Media\Ocean.mkv",
            @"C:\Media\City.mkv"
        ]);

        // Initial default: Mountain is selected, Ocean and City are deselected
        Assert.IsTrue(media.Files[0].IsSelected);
        Assert.IsFalse(media.Files[1].IsSelected);
        Assert.IsFalse(media.Files[2].IsSelected);

        await runtime.UpdateAsync(s => s with { UseCachedResults = false }, default);
        int analysesBefore = runtime.Analyses;
        await settings.LoadCommand.ExecuteAsync();
        settings.IncludeSimple = true;
        await settings.SaveCommand.ExecuteAsync();
        settings.ForceComplex = true;
        await settings.SaveCommand.ExecuteAsync();
        await media.RefreshSettingsSummaryAsync();

        Assert.AreEqual(analysesBefore, runtime.Analyses, "Saving or refreshing settings must not launch a hidden scan.");
        Assert.IsTrue(shell.CanNavigate);
        Assert.IsFalse(media.Files[1].IsSelected);
        Assert.IsFalse(media.Files[2].IsSelected);

        await media.ScanCommand.ExecuteAsync();
        Assert.IsTrue(media.Files[0].IsSelected);
        Assert.IsTrue(media.Files[1].IsSelected);
        Assert.IsTrue(media.Files[2].IsSelected);
    }

    [TestMethod]
    [DataRow("Scan", true)]
    [DataRow("Scan", false)]
    [DataRow("Inspect", true)]
    [DataRow("DeepInspect", true)]
    public async Task AnalysisBatchCancellationPreservesCancelledAndUnprocessedSelections(string operation, bool selected)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        foreach (var row in model.Files)
        {
            row.IsSelected = selected;
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        var command = operation switch
        {
            "Inspect" => model.InspectCommand,
            "DeepInspect" => model.DeepInspectCommand,
            _ => model.ScanCommand
        };
        var analysis = command.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(model.IsBusy);
        runtime.DuringAnalysis = null;
        model.CancelCommand.Execute();
        await analysis.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual(selected, model.Files[0].IsSelected, "Cancellation must preserve the active row's selection.");
        Assert.AreEqual(selected, model.Files[1].IsSelected, "Cancellation must preserve the queued row's selection.");
        Assert.IsTrue(model.Files.All(row => row.SelectionEnabled));
        Assert.AreEqual(selected ? "2 items | 2 items selected" : "2 items", model.SelectionSummary);
        Assert.AreEqual(selected, model.AllFilesSelected);
    }

    [TestMethod]
    public async Task InspectIncompletePromotesRowToSelectedWhenMel()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.IsTrue(settings.AutoSelectAfterScan);
        settings.AutoSelectAfterScan = false;
        await settings.SaveCommand.ExecuteAsync();
        Assert.IsFalse(runtime.Settings.AutoSelectAfterScan);

        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
        await runtime.UpdateAsync(s => s with { IncludeSimple = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
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
    public async Task ConvertBatchReportsBatchProgressAndFileStatus()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringConversion = async token =>
        {
            if (runtime.Conversions == 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };

        var convertTask = model.ConvertDv81Command.ExecuteAsync();
        await started.Task;

        Assert.IsTrue(model.IsBusy);
        Assert.AreEqual("Conversion to DV8.1", model.BatchProgress.Operation);
        Assert.AreEqual(2, model.BatchProgress.Total);
        Assert.AreEqual(0, model.BatchProgress.Processed);
        Assert.IsTrue(model.Files[0].IsActive);
        Assert.IsTrue(model.Files[1].IsPending);
        Assert.AreEqual("Queued", model.Files[1].Status);

        release.SetResult();
        await convertTask;

        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual(2, model.BatchProgress.Processed);
        Assert.AreEqual("Converted", model.Files[0].Status);
        Assert.AreEqual("Converted", model.Files[1].Status);
    }

    [TestMethod]
    public async Task ConvertBatchSupportsPerFileCancellationAndSkip()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        model.Files[2].IsSelected = true;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringConversion = async token =>
        {
            if (runtime.Conversions == 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };

        var convertTask = model.ConvertDv81Command.ExecuteAsync();
        await started.Task;

        model.SkipCommand.Execute(model.Files[2]);
        Assert.AreEqual("Conversion skipped", model.Files[2].Status);
        Assert.IsTrue(model.Files[2].HasWarning);
        Assert.AreEqual("Deselected before starting.", model.Files[2].Warning);

        model.CancelFileCommand.Execute(model.Files[0]);
        release.SetResult();
        await convertTask;

        Assert.AreEqual("Conversion cancelled", model.Files[0].Status);
        Assert.AreEqual("Converted", model.Files[1].Status);
        Assert.AreEqual("Conversion skipped", model.Files[2].Status);
    }

    [TestMethod]
    public async Task PrepareCandidateDetectsOutputCollisionsAcrossBatch()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { OutputDirectory = @"C:\Output", AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\FolderA\Movie.mkv", @"C:\FolderB\Movie.mkv"]);

        await model.ConvertDv81Command.ExecuteAsync();

        Assert.AreEqual("Converted", model.Files[0].Status);
        Assert.AreEqual("Conversion skipped", model.Files[1].Status);
        StringAssert.Contains(model.Files[1].Notice, "Multiple inputs resolve to the same output path");
        Assert.IsTrue(model.Files[1].HasWarning);
        Assert.AreEqual(model.Files[1].Notice, model.Files[1].Warning);
    }

    [TestMethod]
    public async Task SettingsLoadsAndPersistsAllowFel()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.IsFalse(settings.AllowFel);
        Assert.IsFalse(runtime.Settings.AllowFel);

        settings.AllowFel = true;
        Assert.IsTrue(settings.AllowFel);
        await settings.SaveCommand.ExecuteAsync();
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings.LoadCommand.ExecuteAsync();
        Assert.IsTrue(newSettings.AllowFel);
    }

    [TestMethod]
    public async Task SettingsLoadsAndPersistsIncludeSimpleAndForceComplex()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();
        Assert.IsFalse(settings.IncludeSimple);
        Assert.IsFalse(settings.ForceComplex);
        Assert.IsFalse(settings.AllowFel);

        // Change only IncludeSimple to true and save
        settings.IncludeSimple = true;
        Assert.IsTrue(settings.IncludeSimple);
        Assert.IsFalse(settings.ForceComplex);
        Assert.IsTrue(settings.AllowFel);
        await settings.SaveCommand.ExecuteAsync();

        Assert.IsTrue(runtime.Settings.IncludeSimple);
        Assert.IsFalse(runtime.Settings.ForceComplex);
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings1 = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings1.LoadCommand.ExecuteAsync();
        Assert.IsTrue(newSettings1.IncludeSimple);
        Assert.IsFalse(newSettings1.ForceComplex);
        Assert.IsTrue(newSettings1.AllowFel);

        // Change only ForceComplex to true, reset IncludeSimple to false, and save
        newSettings1.IncludeSimple = false;
        newSettings1.ForceComplex = true;
        await newSettings1.SaveCommand.ExecuteAsync();

        Assert.IsFalse(runtime.Settings.IncludeSimple);
        Assert.IsTrue(runtime.Settings.ForceComplex);
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings2 = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings2.LoadCommand.ExecuteAsync();
        Assert.IsFalse(newSettings2.IncludeSimple);
        Assert.IsTrue(newSettings2.ForceComplex);
        Assert.IsTrue(newSettings2.AllowFel);

        // Change both to true and save
        newSettings2.IncludeSimple = true;
        newSettings2.ForceComplex = true;
        await newSettings2.SaveCommand.ExecuteAsync();

        Assert.IsTrue(runtime.Settings.IncludeSimple);
        Assert.IsTrue(runtime.Settings.ForceComplex);
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings3 = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings3.LoadCommand.ExecuteAsync();
        Assert.IsTrue(newSettings3.IncludeSimple);
        Assert.IsTrue(newSettings3.ForceComplex);
        Assert.IsTrue(newSettings3.AllowFel);
    }

    [TestMethod]
    public async Task SelectionSummaryFormatsPluralAndHidesWhenNoneSelected()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { IncludeSimple = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

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
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        Assert.IsFalse(model.IsBusy);
        Assert.IsFalse(model.BatchProgress.IsRunning);
        Assert.AreEqual(0, model.ActiveProgressPercent);
        Assert.IsFalse(model.ActiveProgressIndeterminate);
        Assert.AreEqual("", model.ActiveProgressSummary);

        var progressUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.ActiveProgressPercent) && model.ActiveProgressPercent == 20)
            {
                progressUpdated.TrySetResult();
            }
        };

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringAnalysis = async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        };

        var task = model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await progressUpdated.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(model.IsBusy);
        Assert.IsTrue(model.BatchProgress.IsRunning);
        Assert.AreEqual("Scan", model.ActiveProgressTitle);
        Assert.AreEqual("20%", model.ActiveProgressText);
        Assert.AreEqual(20, model.ActiveProgressPercent);
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

    [TestMethod]
    public void MediaRow_StatusToolTip_ReflectsNoticeWarningResultAndError()
    {
        var row = new MediaRow(@"C:\Media\Test.mkv");
        var propertyChanges = new List<string>();
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
            {
                propertyChanges.Add(e.PropertyName);
            }
        };

        Assert.AreEqual("Not scanned", row.StatusToolTip);

        row.Status = "Ready to convert";
        Assert.AreEqual("Ready to convert", row.StatusToolTip);
        CollectionAssert.Contains(propertyChanges, nameof(MediaRow.StatusToolTip));

        row.Notice = "Profile 7 Complex FEL conversion is disabled by policy.";
        Assert.AreEqual("Profile 7 Complex FEL conversion is disabled by policy.", row.StatusToolTip);

        row.Warning = "Minor metadata mismatch.";
        Assert.AreEqual("Profile 7 Complex FEL conversion is disabled by policy.", row.StatusToolTip);

        row.Notice = "";
        Assert.AreEqual("Minor metadata mismatch.", row.StatusToolTip);

        row.Warning = null;
        row.Result = new(row.Path, DoViFixer.Application.Operations.OperationStatus.Completed, null, "Conversion succeeded cleanly.");
        Assert.AreEqual("Conversion succeeded cleanly.", row.StatusToolTip);

        row.Result = null;
        row.AnalysisError = "Could not parse video container.";
        Assert.AreEqual("Could not parse video container.", row.StatusToolTip);
    }

    [TestMethod]
    public async Task MediaViewModel_OutputSummary_ReflectsSettingsAndNotifiesOnSave()
    {
        using var runtime = new TestRuntime();
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        var archive = runtime.Container.GetRequiredService<ArchiveViewModel>();
        var shell = new ShellViewModel(media, archive, settings, runtime.Container.GetRequiredService<StartupDependencyCheckViewModel>(), runtime.Container.GetRequiredService<DependencyReportViewModel>());

        await media.RefreshSettingsSummaryAsync();
        Assert.AreEqual("Output: Same folder", media.OutputSummary);
        Assert.IsFalse(media.IsReplaceOriginalActive);

        string[]? updatedStatusItems = null;
        media.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(media.StatusItems))
            {
                updatedStatusItems = media.StatusItems.Select(item => item.Text).ToArray();
            }
        };
        await settings.LoadCommand.ExecuteAsync();
        settings.Destination = @"C:\Media\CustomOutput";
        settings.OtherFolder = true;
        settings.ReplaceOriginal = true;
        await settings.SaveCommand.ExecuteAsync();

        Assert.AreEqual(@"Output: C:\Media\CustomOutput (Replace original)", media.OutputSummary);
        Assert.IsTrue(media.IsReplaceOriginalActive);
        StringAssert.Contains(media.OutputSummaryToolTip, @"C:\Media\CustomOutput");
        StringAssert.Contains(media.OutputSummaryToolTip, "Replace original after verification");
        CollectionAssert.AreEqual(new[] { media.SelectionSummary, media.StatusText, media.OutputSummary, media.RetentionSummary, media.FelSummary, media.ArchiveSummary }, updatedStatusItems);
        var outputItem = media.StatusItems.Single(item => item.Text == media.OutputSummary);
        Assert.AreSame(media.OpenSettingsCommand, outputItem.Command);
        Assert.AreEqual(media.OutputSummaryToolTip, outputItem.ToolTipText);
        Assert.IsTrue(media.StatusItems.Single(item => item.Text == media.RetentionSummary).IsEmphasized);
    }

    [TestMethod]
    public void MediaViewModel_OpenSettingsCommand_RaisesRequestNavigateToSettings()
    {
        using var runtime = new TestRuntime();
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        bool requested = false;
        media.RequestNavigateToSettings += () => requested = true;

        Assert.IsTrue(media.OpenSettingsCommand.CanExecute());
        media.OpenSettingsCommand.Execute();
        Assert.IsTrue(requested);
    }

    [TestMethod]
    public void MediaRow_SetConverted_Completed_ClearsPreConversionWarningAndSetsConvertedStatus()
    {
        var row = new MediaRow(@"C:\Media\Movie.mkv");
        row.SetPlan(@"C:\Media\Movie - DV P8.1.mkv", "Enhancement-layer picture data will be lost.");
        Assert.IsTrue(row.HasWarning);
        Assert.AreEqual("Enhancement-layer picture data will be lost.", row.Warning);

        var result = new DoViFixer.Application.Operations.OperationItemResult(
            row.Path,
            DoViFixer.Application.Operations.OperationStatus.Completed,
            @"C:\Media\Movie - DV P8.1.mkv",
            "Verified output published.");
        row.SetConverted(result);

        Assert.AreEqual("Converted", row.Status);
        Assert.IsFalse(row.HasWarning);
        Assert.IsNull(row.Warning);
        Assert.AreEqual(MediaRowState.Converted, row.State);
        Assert.IsTrue(row.CanOpenResult);
    }

    [TestMethod]
    public void MediaRow_SetConverted_Partial_SetsConvertedWithWarningsAndRetainsMessage()
    {
        var row = new MediaRow(@"C:\Media\Movie.mkv");
        var result = new DoViFixer.Application.Operations.OperationItemResult(
            row.Path,
            DoViFixer.Application.Operations.OperationStatus.Partial,
            @"C:\Media\Movie - DV P8.1.mkv",
            "Conversion verified and published. Original backup cleanup did not complete.");
        row.SetConverted(result);

        Assert.AreEqual("Converted with warnings", row.Status);
        Assert.IsTrue(row.HasWarning);
        Assert.AreEqual("Conversion verified and published. Original backup cleanup did not complete.", row.Warning);
        Assert.AreEqual(MediaRowState.Converted, row.State);
        Assert.IsTrue(row.CanOpenResult);
    }

    [TestMethod]
    public async Task SettingsAutoSavesOnPropertyChangesWithoutCallingSaveCommand()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();

        Assert.IsFalse(runtime.Settings.IncludeSimple);
        settings.IncludeSimple = true;
        await settings.SaveTask;
        Assert.IsTrue(runtime.Settings.IncludeSimple);

        Assert.IsFalse(runtime.Settings.ForceComplex);
        settings.ForceComplex = true;
        await settings.SaveTask;
        Assert.IsTrue(runtime.Settings.ForceComplex);

        Assert.IsFalse(runtime.Settings.ReplaceOriginal);
        settings.ReplaceOriginal = true;
        await settings.SaveTask;
        Assert.IsTrue(runtime.Settings.ReplaceOriginal);

        Assert.IsFalse(runtime.Settings.CreateElArchive);
        settings.CreateArchive = true;
        await settings.SaveTask;
        Assert.IsTrue(runtime.Settings.CreateElArchive);

        Assert.IsTrue(runtime.Settings.AutomaticallyScanAddedFiles);
        settings.AutomaticallyScanAddedFiles = false;
        await settings.SaveTask;
        Assert.IsFalse(runtime.Settings.AutomaticallyScanAddedFiles);

        Assert.IsTrue(runtime.Settings.UseCachedResults);
        settings.UseCachedResults = false;
        await settings.SaveTask;
        Assert.IsFalse(runtime.Settings.UseCachedResults);

        Assert.IsTrue(runtime.Settings.AutoSelectAfterScan);
        settings.AutoSelectAfterScan = false;
        await settings.SaveTask;
        Assert.IsFalse(runtime.Settings.AutoSelectAfterScan);

        settings.Destination = @"C:\Media\AutoSaveOutput";
        settings.OtherFolder = true;
        await settings.SaveTask;
        Assert.AreEqual(@"C:\Media\AutoSaveOutput", runtime.Settings.OutputDirectory);

        settings.OtherFolder = false;
        await settings.SaveTask;
        Assert.IsNull(runtime.Settings.OutputDirectory);

        settings.SelectedTheme = DoViFixer.Application.Settings.AppTheme.Dark;
        await settings.SaveTask;
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Dark, runtime.Settings.Theme);
    }

    [TestMethod]
    public async Task SettingsAutoSaveStatusReflectsMissingDestinationWhenOtherFolderEnabled()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.ExecuteAsync();

        settings.Destination = "";
        settings.OtherFolder = true;
        await settings.SaveTask;

        Assert.AreEqual(ViewStatus.Error, settings.Status);
        Assert.AreEqual("Choose an output folder.", settings.StatusMessage);
        Assert.IsNull(runtime.Settings.OutputDirectory);

        settings.Destination = @"C:\Media\Chosen";
        await settings.SaveTask;

        Assert.AreEqual(ViewStatus.SettingsSaved, settings.Status);
        Assert.AreEqual(@"C:\Media\Chosen", runtime.Settings.OutputDirectory);
    }
}
