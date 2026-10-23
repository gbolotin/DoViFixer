using DoViFixer.Application.Operations;
using DoViFixer.Application.Dependencies;
using System.ComponentModel;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;
[TestClass]
public sealed class ViewModelTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitInspectCommandsAlwaysAnalyzeAgain(bool reuse)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(value => value with { UseCachedResults = reuse, AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        await model.InspectCommand.InvokeAsync();
        await model.InspectCommand.InvokeAsync();
        await model.DeepInspectCommand.InvokeAsync();
        await model.DeepInspectCommand.InvokeAsync();
        Assert.AreEqual(4, runtime.Analyses);
        Assert.AreEqual(4, runtime.Probes);
    }

    [TestMethod]
    public async Task FormatShowsDynamicRangeAndProfileWithoutFelVerdict()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Format.mkv"]);
        var row = model.Files[0];
        var analysis = row.Analysis!;
        foreach (var (profile, layer, hdr, number, expected) in new[]
        {
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile7, DoViFixer.Domain.Media.EnhancementLayer.Mel, (bool?)true, (int?)7, "Dolby Vision Profile 7 · MEL"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile7, DoViFixer.Domain.Media.EnhancementLayer.Fel, (bool?)true, (int?)7, "Dolby Vision Profile 7 · FEL"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile7, DoViFixer.Domain.Media.EnhancementLayer.Unknown, (bool?)true, (int?)7, "Dolby Vision Profile 7"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile81, DoViFixer.Domain.Media.EnhancementLayer.None, (bool?)true, (int?)8, "Dolby Vision Profile 8.1"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile5, DoViFixer.Domain.Media.EnhancementLayer.None, (bool?)true, (int?)5, "Dolby Vision Profile 5"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Other, DoViFixer.Domain.Media.EnhancementLayer.None, (bool?)true, (int?)8, "Dolby Vision Profile 8"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.None, DoViFixer.Domain.Media.EnhancementLayer.None, (bool?)true, (int?)null, "HDR"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.None, DoViFixer.Domain.Media.EnhancementLayer.None, (bool?)false, (int?)null, "SDR"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.None, DoViFixer.Domain.Media.EnhancementLayer.None, (bool?)null, (int?)null, "Unknown")
        })
        {
            row.Analysis = analysis with
            {
                Media = analysis.Media with { Profile = profile, IsHdr = hdr, DolbyVisionProfileNumber = number },
                Evidence = analysis.Evidence with { Layer = layer }
            };
            Assert.AreEqual(expected, row.Format);
            bool isDolbyVision = number is not null;
            Assert.AreEqual(isDolbyVision ? "Dolby Vision" : expected, row.DetailRows.Single(detail => detail.Label == "Format").Value);
            Assert.AreEqual(isDolbyVision, row.DetailRows.Any(detail => detail.Label == "Profile"));
        }
    }

    [TestMethod]
    public async Task DetailsShowSimpleAndComplexFelInASeparateProfileRow()
    {
        using var runtime = new TestRuntime();
        var row = new MediaRow(@"C:\Media\Ocean.mkv");
        var media = await runtime.ProbeAsync(row.Path, default);
        foreach (var (peak, expected) in new[] { (900d, "7 · Simple FEL"), (2000d, "7 · Complex FEL") })
        {
            row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
                new(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, DoViFixer.Domain.Media.EnhancementLayer.Fel, 1000, peak, 1, 1));
            Assert.AreEqual("Dolby Vision", row.DetailRows.Single(detail => detail.Label == "Format").Value);
            Assert.AreEqual(expected, row.DetailRows.Single(detail => detail.Label == "Profile").Value);
        }
    }

    [TestMethod]
    public async Task AnalysisSettingsPersistResetAndRejectOutOfRangeValues()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<SettingsViewModel>();
        await model.LoadCommand.InvokeAsync();
        Assert.IsTrue(model.Sections.Pages.Any(page => page is AnalysisSettingsSection));
        Assert.AreEqual(50, model.BrightnessMarginNits);
        Assert.AreEqual(1000, model.AssumedPeakBrightnessNits);
        model.BrightnessMarginNits = 75;
        model.AssumedPeakBrightnessNits = 2000;
        await model.SaveTask;
        var reloaded = runtime.Container.GetRequiredService<SettingsViewModel>();
        await reloaded.LoadCommand.InvokeAsync();
        Assert.AreEqual(75, reloaded.BrightnessMarginNits);
        Assert.AreEqual(2000, reloaded.AssumedPeakBrightnessNits);
        reloaded.ResetBrightnessMarginCommand.Invoke();
        reloaded.ResetAssumedPeakBrightnessCommand.Invoke();
        await reloaded.SaveTask;
        Assert.AreEqual(50, runtime.Settings.BrightnessMarginNits);
        Assert.AreEqual(1000, runtime.Settings.AssumedPeakBrightnessNits);
        reloaded.BrightnessMarginNits = -1;
        await reloaded.SaveTask;
        Assert.IsTrue(reloaded.HasSaveError);
        Assert.AreEqual(50, runtime.Settings.BrightnessMarginNits);
        reloaded.DiscardChangesCommand.Invoke();
        Assert.AreEqual(50, reloaded.BrightnessMarginNits);
        var service = runtime.Container.GetRequiredService<DoViFixer.Application.Settings.SettingsService>();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            service.SetPreferencesAsync(null, null, false, false, default, brightnessMarginNits: 1001));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            service.SetPreferencesAsync(null, null, false, false, default, assumedPeakBrightnessNits: 99));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            service.SetPreferencesAsync(null, null, false, false, default, assumedPeakBrightnessNits: 10001));
    }

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
        var details = row.DetailRows.ToDictionary(detail => detail.Label, detail => detail.Value);
        Assert.AreEqual(expectedLength, details["Length"]);
        Assert.AreEqual(path, details["File location"]);
        Assert.AreEqual("3840 × 2160", details["Resolution"]);
        Assert.AreEqual("10 of 10", row.ExaminationRows.Single(detail => detail.Label == "Samples read").Value);
        CollectionAssert.Contains(changes, nameof(MediaRow.DetailRows));
        CollectionAssert.Contains(changes, nameof(MediaRow.ExaminationRows));
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, DoViFixer.Domain.Media.EnhancementLayer.Mel, 390, 900, 1, 1));
        Assert.IsFalse(row.ExaminationRows.Any(detail => detail.Label == "Samples read"));
        row.Notice = "Keep this warning visible.";
        row.AnalysisError = "Fixture analysis error.";
        StringAssert.Contains(row.DetailNotes, row.Notice);
        StringAssert.Contains(row.DetailNotes, row.AnalysisError);
        CollectionAssert.Contains(changes, nameof(MediaRow.DetailNotes));
    }

    [TestMethod]
    [DataRow(0L, 0L, "Library: 0 B")]
    [DataRow(25L, 50L, "Library: 25 B")]
    [DataRow(512L, 512L, "Library: 512 B")]
    [DataRow(1048576L, 1048576L, "Library: 1 MiB")]
    [DataRow(536870912L, 536870912L, "Library: 512 MiB")]
    public async Task SettingsShowsLibrarySizeWithoutIncludingPreviewCache(long analysisBytes, long frameBytes, string expected)
    {
        using var runtime = new TestRuntime { AnalysisCacheBytes = analysisBytes, FrameCacheBytes = frameBytes };
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        Assert.AreEqual(expected, settings.LibrarySizeText);
        Assert.AreEqual(runtime.RootDirectory, settings.LibraryDirectory);
        settings.OpenLibraryFolderCommand.Invoke();
        Assert.AreEqual(runtime.RootDirectory, runtime.OpenedFolder);
        await settings.CleanLibraryCommand.InvokeAsync();
        Assert.AreEqual(expected, settings.LibrarySizeText, "Cleaning preserves existing records and their allocated space.");
        runtime.FrameCacheBytes = 1024;
        await settings.LoadCommand.InvokeAsync();
        Assert.AreEqual(expected, settings.LibrarySizeText, "Preview cache size is separate from library size.");
    }

    [TestMethod]
    public async Task ConversionUsesSavedTargetForBatchAndRowActions()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = model.Files[0];
        await model.ConvertCommand.InvokeAsync();
        Assert.IsTrue(row.Result!.Output!.EndsWith(" - DV P8.1.mkv"));

        await runtime.UpdateAsync(s => s with { RemoveDolbyVision = true }, default);
        model.UpdateSettingsSummary(runtime.Settings);
        await model.ConvertRowCommand.InvokeAsync(row);
        Assert.AreEqual(2, runtime.Conversions);
        Assert.IsTrue(row.Result!.Output!.EndsWith(" - HDR10.mkv"));
        row.IsSelected = false;
        Assert.IsFalse(model.ConvertCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task InspectMenuRunsThePickedInspectionAndRemembersIt()
    {
        using var runtime = new TestRuntime();
        // Without the cache every pick runs the tools, so each run is counted.
        await runtime.UpdateAsync(settings => settings with { UseCachedResults = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = model.Files[0];
        Assert.IsFalse(model.IsDeepInspectionChosen, "Inspect runs a regular inspection by default.");
        model.IsDeepInspectionChosen = true;
        Assert.IsTrue(model.ChooseInspectCommand.CanExecute(null));

        int analyses = runtime.Analyses;
        await model.ChooseInspectCommand.ExecuteAsync(null);
        Assert.AreEqual(analyses + 1, runtime.Analyses, "Picking Inspect runs it right away.");
        Assert.AreEqual(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, row.Analysis!.Evidence.Method);
        Assert.IsFalse(model.IsDeepInspectionChosen, "The button runs Inspect next.");

        await model.ChooseDeepInspectCommand.ExecuteAsync(null);
        Assert.AreEqual(analyses + 2, runtime.Analyses);
        Assert.AreEqual(DoViFixer.Domain.Analysis.AnalysisMethod.DeepInspection, row.Analysis!.Evidence.Method);
        Assert.IsTrue(model.IsDeepInspectionChosen);

        row.IsSelected = false;
        Assert.IsFalse(model.ChooseInspectCommand.CanExecute(null), "Nothing to inspect without a selection.");
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
            await model.AddDroppedPathsCommand.InvokeAsync(paths);
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
        await model.AddDroppedPathsCommand.InvokeAsync(mixed);
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
        CollectionAssert.AreEqual(new[] { "Movie1.mkv", "movie2.mkv", "Movie3.mkv", "Movie10.mkv" }, Names(), "Format sorting uses the known format even when analysis fails.");
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

        await model.ScanCommand.InvokeAsync();
        CollectionAssert.AreEqual(new[] { "Movie10.mkv", "movie2.mkv", "Movie1.mkv" }, processed, "Unsorted lists keep the order files were added.");

        model.FileSort.SortBy(MediaSortColumn.Name);
        processed.Clear();
        await model.ScanCommand.InvokeAsync();
        CollectionAssert.AreEqual(new[] { "Movie1.mkv", "movie2.mkv", "Movie10.mkv" }, processed);

        model.FileSort.SortBy(MediaSortColumn.Name);
        foreach (var row in model.Files)
        {
            row.IsSelected = row.Name != "Movie10.mkv";
        }
        processed.Clear();
        await model.InspectCommand.InvokeAsync();
        CollectionAssert.AreEqual(new[] { "movie2.mkv", "Movie1.mkv" }, processed, "Selected-file batches follow the sort too.");

        model.FileSort.SortBy(MediaSortColumn.Status);
        var sortedOrder = model.FileSort.Comparer;
        model.Files.Single(row => row.Name == "Movie1.mkv").Status = "Needs attention";
        model.Files.Single(row => row.Name == "movie2.mkv").Status = "";
        model.Files.Single(row => row.Name == "Movie10.mkv").Status = "";
        processed.Clear();
        await model.ScanCommand.InvokeAsync();
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
        var scanning = model.ScanCommand.InvokeAsync();
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
    public Task RemoveFileUpdatesSelectionAndDetailsAndRejectsRemovalWhileBusy() =>
        // Late scan callbacks must arrive on the UI thread, not race the change-list assertions.
        DispatcherThread.RunAsync(ExerciseRemoveFileAsync, TimeSpan.FromSeconds(30));

    private static async Task ExerciseRemoveFileAsync()
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
        var scanning = model.ScanCommand.InvokeAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(model.RemoveFileCommand.CanExecute(first));
            model.RemoveFileCommand.Invoke(first);
            Assert.HasCount(2, model.Files);
        }
        finally
        {
            model.CancelCommand.Invoke();
            await scanning.WaitAsync(TimeSpan.FromSeconds(10));
        }

        second.IsSelected = true;
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.IsTrue(model.RemoveFileCommand.CanExecute(first));
        model.RemoveFileCommand.Invoke(first);
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

        model.RemoveFileCommand.Invoke(second);
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
        model.Files.Add(new MediaRow(@"C:\Media\Ocean.mkv") { IsSelected = true });
        model.RemoveSelectedCommand.Invoke();

        CollectionAssert.AreEqual(new[] { false, true, false, true }, states);
        Assert.IsTrue(model.IsFileListEmpty);
    }

    [TestMethod]
    public void ShellShowsMediaAndSettingsSectionsInTheFrame()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        CollectionAssert.AreEqual(new[] { typeof(MediaViewModel) }, shell.Pages.Select(page => page.GetType()).ToArray());
        Assert.AreSame(shell.Pages[0], shell.CurrentPage);
        Assert.IsFalse(shell.IsSettingsOpen);

        var sections = shell.Settings.Sections;
        CollectionAssert.AreEqual(
            new[] { typeof(GeneralSettingsSection), typeof(PresetsSettingsSection), typeof(AnalysisSettingsSection), typeof(ConversionSettingsSection), typeof(SafetySettingsSection), typeof(PerformanceSettingsSection), typeof(NativeToolsSettingsSection), typeof(AboutSettingsSection) },
            sections.Pages.Select(page => page.GetType()).ToArray());
        CollectionAssert.AreEqual(new[] { "General", "Presets", "Analysis", "Conversion", "Safety", "Performance", "Native tools", "About" }, sections.Pages.Select(page => page.NavigationName).ToArray());
        Assert.IsTrue(sections.Pages.Where(page => page is not PerformanceSettingsSection).All(page => page.NavigationIcon is { Length: 1 }), "Other sections retain their Segoe Fluent Icons glyphs.");
        Assert.IsNull(sections.Pages.OfType<PerformanceSettingsSection>().Single().NavigationIcon, "Performance uses a vector speedometer.");
        Assert.IsTrue(sections.Pages.Cast<SettingsSection>().All(section => ReferenceEquals(section.Settings, shell.Settings)), "Every section shows the same settings.");
        Assert.AreSame(sections.Pages[0], sections.CurrentPage);
    }

    [TestMethod]
    public async Task OpeningSettingsFromMediaLoadsThemAndKeepsTheFileList()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(value => value with { IncludeSimple = true }, default);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.Pages.OfType<MediaViewModel>().Single();
        Assert.AreSame(media, shell.CurrentPage);
        await media.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = media.Files.Single();
        row.IsSelected = false;

        media.OpenSettingsCommand.Invoke();
        await (shell.OpenSettingsCommand.ExecutionTask ?? Task.CompletedTask);
        Assert.IsTrue(shell.IsSettingsOpen);
        Assert.AreSame(media, shell.CurrentPage, "Settings opens over the current page.");
        Assert.IsInstanceOfType<ConversionSettingsSection>(shell.Settings.Sections.CurrentPage, "The output summary opens the page that sets it.");
        Assert.IsTrue(shell.Settings.IncludeSimple);
        await shell.CloseSettingsCommand.InvokeAsync();
        Assert.IsFalse(shell.IsSettingsOpen);
        Assert.AreSame(row, media.Files.Single());
        Assert.AreSame(row, media.Focused);
        Assert.IsFalse(row.IsSelected);
        Assert.AreEqual(1, runtime.Analyses, "Navigation must not launch a new scan.");
    }

    [TestMethod]
    public async Task SettingsOpenOnlyWhenIdleAndCloseOnlyWithoutABusyOperationOrSaveError()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = shell.CurrentPage;
        runtime.DuringDependencyCheck = token => Task.Delay(Timeout.Infinite, token);
        var checking = shell.Settings.CheckCommand.InvokeAsync();
        try
        {
            Assert.IsFalse(shell.CanNavigate);
            Assert.AreSame(media, shell.CurrentPage);
            Assert.IsFalse(shell.OpenSettingsCommand.CanExecute(null), "Settings must not open while an operation runs.");
        }
        finally
        {
            shell.Settings.CancelCommand.Invoke();
            await checking.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await shell.OpenSettingsCommand.InvokeAsync();
        Assert.IsTrue(shell.IsSettingsOpen);
        Assert.IsFalse(shell.OpenSettingsCommand.CanExecute(null), "An open frame does not open again.");
        checking = shell.Settings.CheckCommand.InvokeAsync();
        try
        {
            Assert.IsFalse(shell.CloseSettingsCommand.CanExecute(null), "The frame stays open while a settings operation runs.");
        }
        finally
        {
            shell.Settings.CancelCommand.Invoke();
            await checking.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.IsTrue(shell.CloseSettingsCommand.CanExecute(null));
        shell.Settings.OtherFolder = true;
        await shell.Settings.SaveTask;
        Assert.IsFalse(shell.CloseSettingsCommand.CanExecute(null), "A save error keeps the frame open for recovery.");
        await shell.CloseSettingsCommand.InvokeAsync();
        Assert.IsTrue(shell.IsSettingsOpen);
        Assert.AreSame(media, shell.CurrentPage);
        Assert.IsFalse(shell.CanNavigate);

        shell.Settings.DiscardChangesCommand.Invoke();
        Assert.IsTrue(shell.CloseSettingsCommand.CanExecute(null));
        await shell.CloseSettingsCommand.InvokeAsync();
        Assert.IsFalse(shell.IsSettingsOpen);
        Assert.IsTrue(shell.OpenSettingsCommand.CanExecute(null));
        Assert.AreSame(media, shell.CurrentPage);
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
        Assert.IsFalse(model.PauseBatchCommand.CanExecute(null));
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
        Assert.IsTrue(model.PauseBatchCommand.CanExecute(null));
        model.PauseBatchCommand.Invoke();
        Assert.AreEqual("Resume", model.PauseBatchText);
        Assert.AreEqual("\uE768", model.PauseBatchIcon);
        Assert.AreEqual("Resume the batch", model.PauseBatchToolTip);
        StringAssert.Contains(model.ActiveProgressSummary, "Pausing after current job");
        release.SetResult();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, runtime.Analyses);
        Assert.IsFalse(adding.IsCompleted);

        if (cancelBatch)
        {
            model.CancelCommand.Invoke();
        }
        else
        {
            model.PauseBatchCommand.Invoke();
        }

        await adding.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(cancelBatch ? 1 : 2, runtime.Analyses);
        Assert.AreEqual("Pause", model.PauseBatchText);
        Assert.AreEqual("\uE769", model.PauseBatchIcon);
        Assert.AreEqual("Pause before the next job starts", model.PauseBatchToolTip);
        Assert.IsFalse(model.PauseBatchCommand.CanExecute(null));
        Assert.IsTrue(model.IsIdle);
    }

    [TestMethod]
    public async Task ExaminationShowsLimitsAndMarksEachScanSample()
    {
        using var runtime = new TestRuntime();
        var row = new MediaRow(@"C:\Media\Mountain.mkv");
        var media = (await runtime.ProbeAsync(row.Path, default)) with { MaxCll = 1000 };
        var samples = new DoViFixer.Domain.Analysis.ScanSample[] { new(3.4, 900), new(610, 1200), new(4000, null) };
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu, DoViFixer.Domain.Media.EnhancementLayer.Fel, 48, 1200, 2, 3,
                Options: new(MinimumScanCoveragePercent: 50), Samples: samples));

        Assert.IsTrue(row.HasExamination);
        var rows = row.ExaminationRows.ToDictionary(detail => detail.Label);
        Assert.AreEqual("Complex FEL", rows["Verdict"].Value);
        Assert.AreEqual("1,000 nits (MaxCLL)", rows["Base picture"].Value);
        Assert.AreEqual("1,050 nits", rows["Judged unsafe above"].Value);
        Assert.AreEqual("1,200 nits, 200 over the base picture", rows["Brightest found"].Value);
        Assert.AreEqual(MediaStatusTone.HighRisk, rows["Brightest found"].StatusTone);
        Assert.AreEqual("2 of 3", rows["Samples read"].Value);
        StringAssert.Contains(row.ExaminationNote, "Metadata heuristic");
        Assert.IsFalse(row.DetailNotes.Contains("Metadata heuristic", StringComparison.Ordinal));
        Assert.IsFalse(row.DetailRows.Any(detail => detail.Label == "Analysis"));

        CollectionAssert.AreEqual(new[] { "0:03", "10:10", "1:06:40" }, row.SampleRows.Select(sample => sample.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "900 nits", "1,200 nits", "Not read" }, row.SampleRows.Select(sample => sample.Value).ToArray());
        CollectionAssert.AreEqual(new[] { MediaStatusTone.None, MediaStatusTone.HighRisk, MediaStatusTone.VerifyRecommended }, row.SampleRows.Select(sample => sample.StatusTone).ToArray());
        Assert.AreEqual("Each sample · 2 of 3 read, 1 above limit", row.SampleSummary);

        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu, DoViFixer.Domain.Media.EnhancementLayer.Fel, 48, 1000, 2, 2,
                Samples: [new(3, 900), new(610, 1000)]));
        Assert.AreEqual("Each sample · 2 read, none above limit", row.SampleSummary);

        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu, DoViFixer.Domain.Media.EnhancementLayer.Mel, 48, 1200, 2, 2,
                Samples: [new(3, 900), new(610, 1200)]));
        Assert.AreEqual("Each sample · 2 read", row.SampleSummary, "MEL brightness has no unsafe limit to count against.");

        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, DoViFixer.Domain.Media.EnhancementLayer.Fel, 4475, 900, 1, 1));
        Assert.IsFalse(row.HasSampleRows);
        Assert.IsFalse(row.ExaminationRows.Any(detail => detail.Label == "Samples read"));
        Assert.AreEqual("4,475", row.ExaminationRows.Single(detail => detail.Label == "Frames read").Value);
    }

    [TestMethod]
    public async Task CopyDetailsPutsTheFocusedFileDetailsOnTheClipboard()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        model.Focused = null;
        Assert.IsFalse(model.CopyDetailsCommand.CanExecute(null));

        var row = model.Files[0];
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(row.Analysis!.Media,
            row.Analysis.Evidence with { Samples = [new(4, 1720)] });
        model.Focused = row;
        model.CopyDetailsCommand.Invoke();

        string text = runtime.ClipboardText!;
        Assert.AreEqual(row.DetailsText, text);
        StringAssert.StartsWith(text, $"{row.Name}{Environment.NewLine}File location: {row.Path}");
        StringAssert.Contains(text, "What the examination found");
        StringAssert.StartsWith(row.SampleSummary, "Each sample · 1 read");
        StringAssert.Contains(text, $"{row.SampleSummary}{Environment.NewLine}0:04  1,720 nits");
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
        Assert.AreEqual("9 of 10", row.ExaminationRows.Single(detail => detail.Label == "Samples read").Value);
        Assert.IsTrue(row.ExaminationNote.Contains("No RPU was found in input file"));
        Assert.IsTrue(row.DetailNotes.Contains("Suggested action: Inspect"));
        Assert.IsFalse(row.CanRetryAnalysis);
        Assert.IsTrue(model.InspectIncompleteCommand.CanExecute(row));
        row.IsActive = true;
        Assert.IsFalse(row.CanInspectIncomplete);
        row.IsActive = false;
        row.IsSelected = false;
        model.Focused = model.Files[1];
        int analyses = runtime.Analyses;
        await model.InspectIncompleteCommand.InvokeAsync(row);
        Assert.AreEqual(analyses + 1, runtime.Analyses);
        Assert.AreEqual(1, runtime.FullAnalyses);
        Assert.AreEqual(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, row.Analysis!.Evidence.Method);
        Assert.IsFalse(row.CanInspectIncomplete);
        Assert.AreEqual("Safe", row.Status);
        Assert.AreEqual(0, runtime.Conversions);
    }

    [TestMethod]
    public async Task FailedInspectionKeepsPreviousFileDetails()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = model.Files[0];
        var scanned = row.Analysis;
        Assert.IsNotNull(scanned);
        await runtime.ClearAsync(default);
        runtime.DuringAnalysis = _ => throw new IOException("Fixture analysis failure");
        await model.InspectCommand.InvokeAsync();
        Assert.AreEqual("Inspection failed", row.Status);
        Assert.AreSame(scanned, row.Analysis);
        Assert.IsTrue(row.DetailRows.Any(detail => detail.Label == "Resolution"));
        StringAssert.Contains(row.DetailNotes, "Fixture analysis failure");
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
        await command.InvokeAsync();
        Assert.AreEqual(method == DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu ? "Scan failed" : "Inspection failed", row.Status);
        Assert.IsTrue(row.CanRetryAnalysis);
        Assert.IsTrue(row.DetailNotes.Contains("Fixture analysis failure"));
        int analyses = runtime.Analyses;
        runtime.DuringAnalysis = null;
        row.IsSelected = false;
        model.Focused = model.Files[1];
        await model.RetryAnalysisCommand.InvokeAsync(row);
        Assert.AreEqual(analyses + 1, runtime.Analyses);
        Assert.AreEqual(method, row.Analysis!.Evidence.Method);
        Assert.AreEqual(method switch
        {
            DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu => "Likely safe",
            DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu => "Safe",
            _ => "Risk unknown"
        }, row.Status);
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
        Assert.IsFalse(model.OpenFileLocationCommand.CanExecute(null));
        model.Focused = model.Files[1];
        Assert.IsTrue(model.OpenFileLocationCommand.CanExecute(null));
        model.OpenFileLocationCommand.Invoke();
        Assert.AreEqual(@"C:\Media\Two\Ocean.mkv", runtime.ShownFile);
    }

    [TestMethod]
    public async Task InspectionLeavesOutFilesThatAreNotProfile7()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\P81.mkv", @"C:\Media\Mountain.mkv"]);
        var (p81, p7) = (model.Files[0], model.Files[1]);
        Assert.IsFalse(p81.IsProfile7);
        Assert.IsFalse(model.InspectRowCommand.CanExecute(p81), "Inspection has nothing to add for a file that is not Profile 7.");
        Assert.IsFalse(model.DeepInspectRowCommand.CanExecute(p81));
        Assert.IsTrue(model.ScanRowCommand.CanExecute(p81), "Scan still reads the file again.");
        Assert.IsTrue(model.InspectRowCommand.CanExecute(p7));
        Assert.IsTrue(model.InspectRowCommand.CanExecute(model.Files.Single(row => row.Analysis is not null && row.IsProfile7)));

        p7.IsSelected = false;
        Assert.IsTrue(p81.IsSelected);
        Assert.IsFalse(model.InspectCommand.CanExecute(null), "Only a file that is not Profile 7 is checked.");
        Assert.IsFalse(model.DeepInspectCommand.CanExecute(null));

        p7.IsSelected = true;
        int fullAnalyses = runtime.FullAnalyses;
        await model.InspectCommand.InvokeAsync();
        Assert.AreEqual(fullAnalyses + 1, runtime.FullAnalyses, "Only the Profile 7 file is inspected.");
        Assert.IsTrue(p81.IsSelected, "The skipped file keeps its check mark.");

        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        await model.AddAsync([@"C:\Media\Ocean.mkv"]);
        Assert.IsTrue(model.InspectRowCommand.CanExecute(model.Files.Single(row => row.Path == @"C:\Media\Ocean.mkv")), "A file not analyzed yet may be Profile 7.");
    }

    [TestMethod]
    public async Task DetailNextStepScansAndInspectsOnlyItsFileWithoutChangingSelection()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Mountain2.mkv"]);
        var row = model.Files[0];
        row.IsSelected = false;
        Assert.AreEqual("Scan", row.NextStepText);
        Assert.IsFalse(row.OffersConversion, "An unscanned file is not offered for conversion.");
        Assert.IsFalse(model.ScanRowCommand.CanExecute(new MediaRow(row.Path)));
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        await model.ScanRowCommand.InvokeAsync(row);
        Assert.IsNotNull(row.Analysis);
        Assert.IsNull(model.Files[1].Analysis);
        Assert.AreEqual("Inspect", row.NextStepText);
        Assert.IsTrue(row.OffersConversion, "A scanned MEL file offers Convert while inspection is still recommended.");
        CollectionAssert.Contains(changed, nameof(MediaRow.OffersConversion));
        await model.InspectIncompleteCommand.InvokeAsync(row);
        Assert.AreEqual(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, row.Analysis.Evidence.Method);
        Assert.AreEqual("Convert", row.NextStepText);
        Assert.IsTrue(row.OffersConversion);
        Assert.IsFalse(row.IsSelected);
        Assert.IsFalse(row.DetailRows.Any(detail => detail.Label == "Next step"));
        row.SetConverted(new(row.Path, OperationStatus.Completed, @"C:\Media\Mountain-P81.mkv"));
        Assert.AreEqual("Open output", row.NextStepText);
        Assert.IsFalse(row.OffersConversion, "A converted file offers its output instead.");
        row.IsMissing = true;
        Assert.AreEqual("", row.NextStepText);
    }

    [TestMethod]
    public async Task ConvertedOutputsFollowTheirSourcesWithoutDuplicatingExistingRows()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Mountain2.mkv"]);
        var sources = model.Files.ToArray();
        await model.ConvertCommand.InvokeAsync();
        CollectionAssert.AreEqual(new[] { sources[0].Path, sources[0].Result!.Output!, sources[1].Path, sources[1].Result!.Output! },
            model.Files.Select(row => row.Path).ToArray());
        Assert.IsTrue(sources.All(source => model.Files[model.Files.IndexOf(source) + 1].IsSelected == false));
        var output = model.Files[1];
        model.Files.Move(1, model.Files.Count - 1);
        sources[0].IsSelected = true;
        sources[1].IsSelected = false;
        await model.ConvertCommand.InvokeAsync();
        Assert.AreSame(output, model.Files[1]);
        Assert.AreEqual(4, model.Files.Count);
    }

    [TestMethod]
    public async Task ConvertedFileLinksShowTheConvertedFile()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\One\Mountain.mkv", @"C:\Media\Two\Ocean.mkv"]);
        Assert.IsFalse(model.Files[0].ShowsResultComparison);
        Assert.IsTrue(model.Files[0].DetailRows.All(detail => detail.Result == ""), "Before conversion details have no result column.");
        Assert.AreEqual(model.Files[0].Path, model.Files[0].DetailRows.Single(detail => detail.Label == MediaRow.FileLocationLabel).Value);
        await model.ConvertCommand.InvokeAsync();
        string output = model.Files[0].Result!.Output!;
        StringAssert.StartsWith(output, @"C:\Media\One\");
        Assert.IsTrue(model.Files[0].ShowsResultComparison);
        Assert.IsNotNull(model.Files[0].ResultAnalysis, "The produced file's metadata is read for the comparison.");
        var file = model.Files[0].DetailRows.Single(detail => detail.Label == MediaRow.FileLocationLabel);
        Assert.AreEqual("Mountain.mkv", file.Value);
        Assert.AreEqual(Path.GetFileName(output), file.Result);
        Assert.AreEqual("3840 × 2160", model.Files[0].DetailRows.Single(detail => detail.Label == "Resolution").Result);
        Assert.IsFalse(model.Files[0].DetailRows.Any(detail => detail.Label == "Type"), "Every file is Matroska, so the type is not shown.");
        Assert.AreEqual(0, model.Files[0].ResultFiles.Count, "The comparison already links the output and the unchanged original.");
        var outputRow = model.Files.Single(row => row.Path == output);
        Assert.AreEqual("Dolby Vision Profile 8.1", outputRow.Format);
        Assert.AreEqual("Already compatible", outputRow.Status);
        Assert.IsFalse(outputRow.IsSelected);
        model.Focused = model.Files[0];
        model.OpenOutputCommand.Invoke();
        Assert.AreEqual(output, runtime.ShownFile);
        Assert.AreEqual(model.Files[0].Path, model.Files[0].Result!.Original, "The default conversion keeps the original in place.");
        Assert.AreEqual("Converted", model.Files[0].Status);
        await model.ScanCommand.InvokeAsync();
        Assert.AreEqual("Likely safe", model.Files[0].Status);
        Assert.IsFalse(model.Files[0].CanOpenResult);
        Assert.IsFalse(model.Files[0].ShowsResultComparison, "Without a converted file the comparison is hidden.");
        Assert.AreEqual(model.Files[0].Path, model.Files[0].DetailRows.Single(detail => detail.Label == MediaRow.FileLocationLabel).Value);
        Assert.IsFalse(model.OpenOutputCommand.CanExecute(null));
        Assert.IsNotNull(model.Files[0].Result, "Analysis retains the previous conversion details.");
        var resultFiles = model.Files[0].ResultFiles;
        Assert.AreEqual(output, resultFiles.Single(resultFile => resultFile.Label == "Output").Path);
        string original = resultFiles.Single(resultFile => resultFile.Label == "Original").Path;
        model.ShowResultFileCommand.Invoke(original);
        Assert.AreEqual(original, runtime.ShownFile);
        Assert.IsFalse(model.ShowResultFileCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow(ExistingOutputHandling.Replace, @"C:\Media\One\Mountain - DV P8.1.mkv")]
    [DataRow(ExistingOutputHandling.KeepBoth, @"C:\Media\One\Mountain - DV P8.1 (1).mkv")]
    public async Task SingleFileConversionAsksWhatToDoWithAnExistingOutput(ExistingOutputHandling? answer, string? expectedOutput)
    {
        using var runtime = new TestRuntime { ExistingOutputAnswer = answer };
        const string existing = @"C:\Media\One\Mountain - DV P8.1.mkv";
        runtime.ExistingOutputFiles.Add(existing);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\One\Mountain.mkv"]);
        var row = model.Files.Single();
        int conversions = runtime.Conversions;
        await model.ConvertCommand.InvokeAsync();
        CollectionAssert.AreEqual(new[] { existing }, runtime.ExistingOutputQuestions);
        if (expectedOutput is null)
        {
            Assert.AreEqual(conversions, runtime.Conversions, "Cancel does not convert.");
            Assert.AreNotEqual(MediaRowState.Skipped, row.State, "Cancel does not mark the file skipped.");
            Assert.IsNull(row.Result);
            return;
        }

        Assert.AreEqual(MediaRowState.Converted, row.State);
        Assert.AreEqual(expectedOutput, row.Result!.Output);
        Assert.IsTrue(model.Files.Any(r => r.Path == expectedOutput));
        Assert.AreEqual((expectedOutput, answer == ExistingOutputHandling.Replace), runtime.StagedOutputs.Single(staged => staged.Path == expectedOutput));
    }

    [TestMethod]
    public async Task BatchConversionSkipsExistingOutputsWithoutAsking()
    {
        using var runtime = new TestRuntime { ExistingOutputAnswer = ExistingOutputHandling.Replace };
        runtime.ExistingOutputFiles.Add(@"C:\Media\One\Mountain - DV P8.1.mkv");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\One\Mountain.mkv", @"C:\Media\Two\Ocean.mkv"]);
        foreach (var file in model.Files)
        {
            file.IsSelected = true;
        }

        Assert.AreEqual(2, model.Files.Count(file => file.IsSelected));
        await model.ConvertCommand.InvokeAsync();
        Assert.AreEqual(0, runtime.ExistingOutputQuestions.Count);
        Assert.AreEqual(MediaRowState.Skipped, model.Files[0].State);
        StringAssert.Contains(model.Files[0].Notice, "Output collision");
        Assert.IsFalse(runtime.StagedOutputs.Any(staged => staged.ReplaceExisting), "Batches never replace existing files.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReopenedOriginalShowsItsEarlierConversionWhileTheOutputExists(bool outputDeleted)
    {
        using var runtime = new TestRuntime();
        const string source = @"C:\Media\One\Mountain.mkv";
        var firstRun = runtime.Container.GetRequiredService<MediaViewModel>();
        await firstRun.AddAsync([source]);
        await firstRun.ConvertCommand.InvokeAsync();
        var converted = firstRun.Files[0].Result!;
        if (outputDeleted)
        {
            runtime.MissingFiles.Add(converted.Output!);
        }

        var nextRun = ActivatorUtilities.CreateInstance<MediaViewModel>(runtime.Container);
        await nextRun.AddAsync([source]);
        var row = nextRun.Files.Single();
        Assert.IsNotNull(row.Analysis, "Added files are scanned automatically.");
        if (outputDeleted)
        {
            Assert.IsNull(row.Result, "A deleted output is not shown.");
            Assert.IsFalse(row.ShowsResultComparison);
            return;
        }

        Assert.AreEqual(converted, row.Result);
        Assert.AreEqual("Converted", row.Status);
        Assert.IsTrue(row.ShowsResultComparison);
        Assert.IsNotNull(row.ResultAnalysis);
        Assert.AreEqual(Path.GetFileName(converted.Output), row.DetailRows.Single(detail => detail.Label == MediaRow.FileLocationLabel).Result);
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
            model.CancelCommand.Invoke();
        }
        else
        {
            model.CancelFileCommand.Invoke(model.Files[0]);
        }

        await adding.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual("Scan cancelled", model.Files[0].Status);
        Assert.IsTrue(model.Files[0].IsSelected, "Cancelling a scan must preserve its checkbox selection.");
        Assert.AreEqual(MediaRowState.Cancelled, model.Files[0].State, "The row cancelled mid-scan must show the cancelled result dot.");
        Assert.AreEqual(cancelAll ? "Scan cancelled" : "Low risk", model.Files[1].Status);
        Assert.AreEqual(cancelAll ? MediaRowState.Cancelled : MediaRowState.Scanned, model.Files[1].State);
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
        model.Dispose();
        model = runtime.Container.GetRequiredService<MediaViewModel>();
        runtime.Ready = false;
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        Assert.AreEqual("Likely safe", model.Files.Single().Status);
        Assert.AreEqual(2, runtime.Analyses);
        Assert.HasCount(0, runtime.Reviews);
    }

    [TestMethod]
    public async Task SettingsControlAutomaticScanningCacheReuseAndClearing()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        Assert.IsTrue(settings.AutomaticallyScanAddedFiles);
        Assert.IsTrue(settings.UseCachedResults);
        settings.AutomaticallyScanAddedFiles = false;
        settings.UseCachedResults = false;
        await settings.SaveCommand.InvokeAsync();
        Assert.IsFalse(runtime.Settings.AutomaticallyScanAddedFiles);
        Assert.IsFalse(runtime.Settings.UseCachedResults);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        Assert.AreEqual(0, runtime.Analyses);
        await model.ScanCommand.InvokeAsync();
        await model.ScanCommand.InvokeAsync();
        Assert.AreEqual(2, runtime.Analyses);
        Assert.AreEqual(2, runtime.Probes, "Disabling reuse must also bypass cached probe metadata.");
        settings.UseCachedResults = true;
        await settings.SaveCommand.InvokeAsync();
        await model.ScanCommand.InvokeAsync();
        Assert.AreEqual(2, runtime.Analyses);
        runtime.CachedFrames = 2;
        await settings.CleanLibraryCommand.InvokeAsync();
        Assert.AreEqual(2, runtime.CachedFrames);
        StringAssert.Contains(settings.StatusMessage, "Removed 0 missing file records");
        await model.ScanCommand.InvokeAsync();
        Assert.AreEqual(2, runtime.Analyses, "Cleaning must preserve analysis for existing files.");
    }

    [TestMethod]
    public async Task SettingsLoadsAndAppliesThemeSelection()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.System, settings.SelectedTheme);
        Assert.IsTrue(settings.IsSystemTheme);
        Assert.IsFalse(settings.IsLightTheme);
        Assert.IsFalse(settings.IsDarkTheme);
        Assert.AreEqual(ThemePreference.System, runtime.AppliedTheme);

        settings.SelectedTheme = DoViFixer.Application.Settings.AppTheme.Dark;
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Dark, settings.SelectedTheme);
        Assert.IsFalse(settings.IsSystemTheme);
        Assert.IsFalse(settings.IsLightTheme);
        Assert.IsTrue(settings.IsDarkTheme);
        Assert.AreEqual(ThemePreference.Dark, runtime.AppliedTheme);

        await settings.SaveCommand.InvokeAsync();
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Dark, runtime.Settings.Theme);

        settings.IsLightTheme = true;
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Light, settings.SelectedTheme);
        Assert.IsTrue(settings.IsLightTheme);
        Assert.AreEqual(ThemePreference.Light, runtime.AppliedTheme);

        await settings.SaveCommand.InvokeAsync();
        var newSettings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings.LoadCommand.InvokeAsync();
        Assert.AreEqual(DoViFixer.Application.Settings.AppTheme.Light, newSettings.SelectedTheme);
        Assert.AreEqual(ThemePreference.Light, runtime.AppliedTheme);
    }

    [TestMethod]
    public async Task ClearedRowsCannotInvalidateNewConversionPlans()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var removedRow = model.Files.Single();
        removedRow.IsSelected = true;
        model.RemoveSelectedCommand.Invoke();
        Assert.HasCount(0, model.Files);
        Assert.IsNull(model.Focused);

        await model.AddAsync([@"C:\Media\Mountain2.mkv"]);
        Assert.IsTrue(model.ConvertCommand.CanExecute(null));

        removedRow.IsSelected = false;
        Assert.IsTrue(model.ConvertCommand.CanExecute(null));

        model.Files.Single().IsSelected = false;
        Assert.IsFalse(model.ConvertCommand.CanExecute(null));
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
        Assert.AreEqual(DependencyRequirements.All.Count, shell.Settings.Dependencies.Tools.Count);
        Assert.IsTrue(shell.CanNavigate);
        if (!ready)
        {
            StringAssert.Contains(runtime.Reviews[0], "Installed and ready");
            StringAssert.Contains(runtime.Reviews[0], "Missing or needs attention");
            StringAssert.Contains(runtime.Reviews[0], NativeToolDescriptions.Summary);
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
    public async Task MissingDependencyWarningPersistsAndClearsAfterOperationInstallsTools()
    {
        using var runtime = new TestRuntime();
        runtime.MissingTools.Add(DoViFixer.Application.Dependencies.NativeTool.FFmpeg);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync();
        StringAssert.Contains(runtime.Reviews.Single(), "FFprobe: Installed and ready");
        StringAssert.Contains(runtime.Reviews.Single(), "FFmpeg: Missing");
        Assert.IsTrue(shell.StatusItems.Contains(shell.Settings.Dependencies));
        runtime.Approval = true;
        await shell.Media.AddAsync([@"C:\Media\Mountain.mkv"]);
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
        await shell.Settings.CheckCommand.InvokeAsync();
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
        await shell.OpenSettingsCommand.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(shell.IsSettingsOpen);
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

        await shell.Settings.InstallCommand.InvokeAsync();
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
        await settings.LoadCommand.InvokeAsync();
        runtime.DuringDependencyCheck = _ => throw new IOException("Tool discovery failed");
        settings.ToolPath = @"C:\Tools\ffmpeg.exe";

        await settings.SetToolCommand.InvokeAsync();

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
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => settings.SetPreferencesAsync(null, null, false, false, default, verificationLevel: (DoViFixer.Application.Settings.VerificationLevel)5));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => settings.SetPreferencesAsync(null, null, false, false, default, freeSpaceMarginPercent: 201));
    }

    [TestMethod]
    public async Task ConvertBatchWithoutPriorManualScanExecutesConversionDirectly()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv"]);
        var row = model.Files.Single();
        Assert.AreEqual(0, runtime.FullAnalyses);
        Assert.AreEqual(0, runtime.Conversions);

        await model.ConvertCommand.InvokeAsync();
        Assert.AreEqual(1, runtime.FullAnalyses);
        Assert.AreEqual(1, runtime.Conversions);
        CollectionAssert.Contains(runtime.VerificationLevels, DoViFixer.Application.Settings.VerificationLevel.FrameCountWithRecount);
        Assert.AreEqual("Converted", row.Status);
        Assert.AreEqual(MediaRowState.Converted, row.State);
        Assert.IsTrue(row.CanOpenResult);
        Assert.IsTrue(row.Result!.Output!.EndsWith(" - DV P8.1.mkv"));
        Assert.IsTrue(model.Files.Any(r => r.Path == row.Result.Output));

        row.IsSelected = true;
        await runtime.UpdateAsync(s => s with { RemoveDolbyVision = true }, default);
        await model.ConvertCommand.InvokeAsync();
        Assert.AreEqual("Converted", row.Status);
        Assert.IsTrue(row.Result!.Output!.EndsWith(" - HDR10.mkv"));
        Assert.IsTrue(model.Files.Any(r => r.Path == row.Result.Output));
    }

    [TestMethod]
    public async Task ConvertClearsWarningOnFelRowsAfterConversionWhenAllowFelIsEnabled()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var originalRows = model.Files.ToArray();
        await model.ConvertCommand.InvokeAsync();

        Assert.IsFalse(originalRows[0].HasWarning);
        Assert.IsNull(originalRows[0].Warning);
        Assert.AreEqual("Converted", originalRows[0].Status);

        Assert.IsFalse(originalRows[1].HasWarning);
        Assert.IsNull(originalRows[1].Warning);
        Assert.AreEqual("Converted", originalRows[1].Status);
    }

    [TestMethod]
    public async Task SelectionAndFocusAreIndependentAndCommandsUpdate()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        model.Focused = model.Files[1];
        Assert.IsTrue(model.ConvertCommand.CanExecute(null));
        model.Files[1].IsSelected = false;
        Assert.AreSame(model.Files[1], model.Focused);
        Assert.IsTrue(model.Files[0].IsSelected);
        Assert.IsTrue(model.ConvertCommand.CanExecute(null));
        model.Files[0].IsSelected = false;
        Assert.IsFalse(model.ConvertCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CleanupRequiresReviewAndDeletesOnlyReviewedFiles(bool approve)
    {
        using var runtime = new TestRuntime { Approval = approve, PickedFolder = @"C:\Media" };
        runtime.Archives.UnionWith([@"C:\Media\Movie.dovi", @"C:\Other\Keep.dovi"]);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        await model.CleanupBackupsCommand.InvokeAsync();

        Assert.AreEqual("Delete", runtime.LastApproveLabel);
        Assert.HasCount(1, runtime.Reviews);
        StringAssert.Contains(runtime.Reviews[0], @"C:\Media\Movie.dovi");
        if (approve)
        {
            CollectionAssert.AreEqual(new[] { @"C:\Media\Movie.dovi" }, runtime.DeletedFiles);
            CollectionAssert.AreEqual(runtime.DeletedFiles, runtime.RecycledFiles);
        }
        else
        {
            Assert.IsEmpty(runtime.DeletedFiles);
            Assert.AreEqual(ViewStatus.CleanupNotApproved, model.Status);
        }
    }

    [TestMethod]
    public async Task CleanupUsesPermanentDeleteWhenRecycleBinIsDisabled()
    {
        using var runtime = new TestRuntime { Approval = true, PickedFolder = @"C:\Media" };
        await runtime.UpdateAsync(settings => settings with { RecycleDeletedOriginalsAndBackups = false }, default);
        runtime.Archives.Add(@"C:\Media\Movie.dovi");
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        await model.CleanupBackupsCommand.InvokeAsync();

        CollectionAssert.AreEqual(new[] { @"C:\Media\Movie.dovi" }, runtime.DeletedFiles);
        Assert.IsEmpty(runtime.RecycledFiles);
    }

    [TestMethod]
    public async Task ScanLeavesRowSelectionUnchanged()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        Assert.IsTrue(model.Files.All(row => row.IsSelected), "Scanning must not change which rows are selected.");

        model.Files[0].IsSelected = false;
        await model.ScanCommand.InvokeAsync();
        Assert.IsFalse(model.Files[0].IsSelected, "Scanning must keep a row the user cleared unselected.");
        Assert.IsTrue(model.Files[2].IsSelected, "Scanning must keep complex FEL rows selected.");
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

        foreach (var row in model.Files)
        {
            row.IsSelected = false;
        }

        // ScanCommand is executable even with 0 files selected!
        Assert.IsTrue(model.ScanCommand.CanExecute(null));

        await runtime.UpdateAsync(s => s with { UseCachedResults = false }, default);
        int analysesBefore = runtime.Analyses;
        await model.ScanCommand.InvokeAsync();

        Assert.AreEqual(analysesBefore + 3, runtime.Analyses, "Scan must analyze every file.");
        Assert.IsFalse(model.Files.Any(f => f.IsSelected), "Scan must not select files.");
    }

    [TestMethod]
    public async Task ChangingFelSettingsKeepsScanningExplicit()
    {
        using var runtime = new TestRuntime();
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        var shell = new ShellViewModel([media], settings, runtime.Container.GetRequiredService<StartupDependencyCheckViewModel>(), runtime.Container.GetRequiredService<DependencyReportViewModel>());

        await media.AddAsync([
            @"C:\Media\Mountain.mkv",
            @"C:\Media\Ocean.mkv",
            @"C:\Media\City.mkv"
        ]);

        media.Files[1].IsSelected = false;
        media.Files[2].IsSelected = false;

        await runtime.UpdateAsync(s => s with { UseCachedResults = false }, default);
        int analysesBefore = runtime.Analyses;
        await settings.LoadCommand.InvokeAsync();
        settings.IncludeSimple = true;
        await settings.SaveCommand.InvokeAsync();
        settings.ForceComplex = true;
        await settings.SaveCommand.InvokeAsync();
        await media.RefreshSettingsSummaryAsync();

        Assert.AreEqual(analysesBefore, runtime.Analyses, "Saving or refreshing settings must not launch a hidden scan.");
        Assert.IsTrue(shell.CanNavigate);
        Assert.IsFalse(media.Files[1].IsSelected);
        Assert.IsFalse(media.Files[2].IsSelected);

        await media.ScanCommand.InvokeAsync();
        Assert.AreEqual(analysesBefore + 3, runtime.Analyses);
        Assert.IsTrue(media.Files[0].IsSelected);
        Assert.IsFalse(media.Files[1].IsSelected);
        Assert.IsFalse(media.Files[2].IsSelected);
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
        var analysis = command.InvokeAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(model.IsBusy);
        runtime.DuringAnalysis = null;
        model.CancelCommand.Invoke();
        await analysis.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual(selected, model.Files[0].IsSelected, "Cancellation must preserve the active row's selection.");
        Assert.AreEqual(selected, model.Files[1].IsSelected, "Cancellation must preserve the queued row's selection.");
        Assert.IsTrue(model.Files.All(row => row.SelectionEnabled));
        Assert.AreEqual(selected ? "2 items | 2 items selected" : "2 items", model.SelectionSummary);
        Assert.AreEqual(selected, model.AllFilesSelected);
    }

    [TestMethod]
    public async Task InspectIncompleteKeepsRowSelection()
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

        await model.InspectIncompleteCommand.InvokeAsync(row);

        Assert.AreEqual(DoViFixer.Domain.Analysis.AnalysisVerdict.Mel, row.Analysis!.Verdict);
        Assert.IsFalse(row.IsSelected, "Inspection must not select the row.");
        Assert.AreEqual("1 item", model.SelectionSummary);
    }

    [TestMethod]
    public async Task AddingFilesPreservesExistingRowSelectionAndFocus()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { IncludeSimple = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        model.ShowsCheckBoxes = true;
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);

        model.Files[0].IsSelected = false;
        model.Focused = model.Files[0];

        await model.AddAsync([@"C:\Media\City.mkv"]);

        Assert.HasCount(3, model.Files);
        Assert.IsFalse(model.Files[0].IsSelected, "Existing Mountain row selection must not change.");
        Assert.IsTrue(model.Files[1].IsSelected, "Existing Ocean row selection must not change.");
        Assert.IsTrue(model.Files[2].IsSelected, "New City row must start selected.");
        Assert.AreSame(model.Files[0], model.Focused, "Focused row must remain unchanged when adding files.");
    }

    [TestMethod]
    public async Task ConvertBatchReportsBatchProgressAndFileStatus()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var originalRows = model.Files.ToArray();

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

        var convertTask = model.ConvertCommand.InvokeAsync();
        await started.Task;

        Assert.IsTrue(model.IsBusy);
        Assert.AreEqual("Conversion to DV8.1", model.BatchProgress.Operation);
        Assert.AreEqual(2, model.BatchProgress.Total);
        Assert.AreEqual(0, model.BatchProgress.Processed);
        Assert.IsTrue(originalRows[0].IsActive);
        Assert.IsTrue(originalRows[1].IsPending);
        Assert.AreEqual("Queued", originalRows[1].Status);

        release.SetResult();
        await convertTask;

        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual(2, model.BatchProgress.Processed);
        Assert.AreEqual("Converted", originalRows[0].Status);
        Assert.AreEqual("Converted", originalRows[1].Status);
        StringAssert.StartsWith(model.StatusMessage, "Conversion to DV8.1: 2 Completed in ");
    }

    [TestMethod]
    public async Task WorkerChoicesMarkRecommendedCountsAndResolvePreviouslyAutomaticValues()
    {
        using var runtime = new TestRuntime();
        runtime.Services.AddSingleton<IHardwareInfoProvider>(new EightCoreHardware());
        await runtime.UpdateAsync(value => value with { ConversionWorkers = 0, AnalysisWorkers = 0 }, default);
        var model = runtime.Container.GetRequiredService<SettingsViewModel>();
        await model.LoadCommand.InvokeAsync();
        Assert.AreEqual(2, model.ConversionWorkers);
        Assert.AreEqual(6, model.AnalysisWorkers);
        Assert.AreEqual("2 (Recommended)", model.ConversionWorkerChoices.Single(choice => choice.Value == 2).Label);
        Assert.AreEqual("6 (Recommended)", model.AnalysisWorkerChoices.Single(choice => choice.Value == 6).Label);
        Assert.IsFalse(model.ConversionWorkerChoices.Any(choice => choice.Value == 0));
        Assert.IsFalse(model.AnalysisWorkerChoices.Any(choice => choice.Value == 0));
        Assert.AreEqual(0, runtime.Settings.ConversionWorkers, "Loading the page must not save settings.");
    }

    private sealed class EightCoreHardware : IHardwareInfoProvider
    {
        public DoViFixer.Application.Settings.HardwareProfile Profile { get; } = new(8, 16, true);
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(0)]
    public async Task ConversionWorkersQueueFilesBeyondTheActiveLimit(int configuredWorkers)
    {
        using var runtime = new TestRuntime();
        runtime.Services.AddSingleton<IHardwareInfoProvider>(new EightCoreHardware());
        await runtime.UpdateAsync(settings => settings with { AllowFel = true, ConversionWorkers = configuredWorkers }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        var sources = model.Files.ToArray();
        sources[2].IsSelected = true;
        var started = System.Threading.Channels.Channel.CreateUnbounded<int>();
        using var release = new SemaphoreSlim(0);
        int count = 0;
        runtime.DuringConversion = async token =>
        {
            started.Writer.TryWrite(Interlocked.Increment(ref count));
            await release.WaitAsync(token);
        };

        var conversion = model.ConvertCommand.InvokeAsync();
        try
        {
            await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(2, model.BatchProgress.ActiveCount);
            Assert.AreEqual(1, model.Files.Count(row => row.IsPending));
            Assert.IsFalse(started.Reader.TryRead(out _));

            release.Release();
            await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            release.Release(2);
            await conversion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(3, model.BatchProgress.Processed);
            Assert.IsTrue(sources.All(row => row.State == MediaRowState.Converted));
            Assert.HasCount(6, model.Files);
        }
        finally
        {
            model.CancelCommand.Invoke();
            release.Release(3);
            await conversion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task ConversionWorkersWaitForAggregateDiskBudget()
    {
        using var runtime = new TestRuntime { AvailableSpaceBytes = 250_000_000_000 };
        await runtime.UpdateAsync(settings => settings with { AllowFel = true, ConversionWorkers = 2 }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var sources = model.Files.ToArray();
        var started = System.Threading.Channels.Channel.CreateUnbounded<int>();
        using var release = new SemaphoreSlim(0);
        int count = 0;
        runtime.DuringConversion = async token =>
        {
            started.Writer.TryWrite(Interlocked.Increment(ref count));
            await release.WaitAsync(token);
        };

        var conversion = model.ConvertCommand.InvokeAsync();
        try
        {
            await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(started.Reader.TryRead(out _));
            Assert.AreEqual(1, runtime.Conversions);
            release.Release();
            await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            release.Release();
            await conversion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(sources.All(row => row.State == MediaRowState.Converted));
            Assert.HasCount(4, model.Files);
        }
        finally
        {
            model.CancelCommand.Invoke();
            release.Release(2);
            await conversion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task ConversionDiskBudgetIncludesConfiguredSafetyMargin()
    {
        using var runtime = new TestRuntime { AvailableSpaceBytes = 205_000_000_000 };
        await runtime.UpdateAsync(settings => settings with { FreeSpaceMarginPercent = 0 }, default);
        var budget = runtime.Container.GetRequiredService<DoViFixer.Application.Conversion.ConversionDiskBudget>();
        var request = new DoViFixer.Application.Conversion.CandidateConversionRequest(@"C:\Media\Mountain.mkv", DoViFixer.Domain.Conversion.ConversionTarget.Profile81, null, null, false, false, true, false);

        using (await budget.ReserveAsync(request, default))
        {
        }

        await runtime.UpdateAsync(settings => settings with { FreeSpaceMarginPercent = 20 }, default);
        await Assert.ThrowsExactlyAsync<IOException>(() => budget.ReserveAsync(request, default));
    }

    [TestMethod]
    public async Task ConvertBatchSupportsPerFileCancellationAndSkip()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        var originalRows = model.Files.ToArray();
        originalRows[2].IsSelected = true;

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

        var convertTask = model.ConvertCommand.InvokeAsync();
        await started.Task;

        model.SkipCommand.Invoke(originalRows[2]);
        Assert.AreEqual("Conversion skipped", originalRows[2].Status);
        Assert.IsFalse(originalRows[2].HasWarning, "A skip reason is not a conversion warning.");
        Assert.AreEqual("Skipped before starting.", originalRows[2].StatusToolTip);
        Assert.IsTrue(originalRows[2].IsSelected, "Skip leaves the check mark to the user.");

        model.CancelFileCommand.Invoke(originalRows[0]);
        release.SetResult();
        await convertTask;

        Assert.AreEqual("Conversion cancelled", originalRows[0].Status);
        Assert.AreEqual("Converted", originalRows[1].Status);
        Assert.AreEqual("Conversion skipped", originalRows[2].Status);
    }

    [TestMethod]
    public async Task PrepareCandidateDetectsOutputCollisionsAcrossBatch()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { OutputDirectory = @"C:\Output", AllowFel = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\FolderA\Movie.mkv", @"C:\FolderB\Movie.mkv"]);
        var originalRows = model.Files.ToArray();

        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual("Converted", originalRows[0].Status);
        Assert.AreEqual("Conversion skipped", originalRows[1].Status);
        StringAssert.Contains(originalRows[1].Notice, "Multiple inputs resolve to the same output path");
        Assert.IsFalse(originalRows[1].HasWarning, "A skip reason is not a conversion warning.");
        Assert.AreEqual(originalRows[1].Notice, originalRows[1].StatusToolTip);
    }

    [TestMethod]
    public async Task SettingsLoadsAndPersistsAllowFel()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        Assert.IsFalse(settings.AllowFel);
        Assert.IsFalse(runtime.Settings.AllowFel);

        settings.AllowFel = true;
        Assert.IsTrue(settings.AllowFel);
        await settings.SaveCommand.InvokeAsync();
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings.LoadCommand.InvokeAsync();
        Assert.IsTrue(newSettings.AllowFel);
    }

    [TestMethod]
    public async Task SettingsWarnsOnlyWhenReplacedComplexFelHasNoBackup()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.ForceComplex = true;
        settings.ReplaceOriginal = true;
        settings.CreateArchive = false;
        Assert.IsTrue(settings.IsComplexFelLossPermanent);

        settings.CreateArchive = true;
        Assert.IsFalse(settings.IsComplexFelLossPermanent);
        settings.CreateArchive = false;
        settings.ReplaceOriginal = false;
        Assert.IsFalse(settings.IsComplexFelLossPermanent);
        settings.ReplaceOriginal = true;
        settings.AllowFel = false;
        Assert.IsFalse(settings.IsComplexFelLossPermanent);
        Assert.IsTrue(changed.Count(name => name == nameof(SettingsViewModel.IsComplexFelLossPermanent)) >= 5);
    }

    [TestMethod]
    public async Task SettingsLoadsAndPersistsIncludeSimpleAndForceComplex()
    {
        using var runtime = new TestRuntime();
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.LoadCommand.InvokeAsync();
        Assert.IsFalse(settings.IncludeSimple);
        Assert.IsFalse(settings.ForceComplex);
        Assert.IsFalse(settings.AllowFel);

        // Change only IncludeSimple to true and save
        settings.IncludeSimple = true;
        Assert.IsTrue(settings.IncludeSimple);
        Assert.IsFalse(settings.ForceComplex);
        Assert.IsTrue(settings.AllowFel);
        await settings.SaveCommand.InvokeAsync();

        Assert.IsTrue(runtime.Settings.IncludeSimple);
        Assert.IsFalse(runtime.Settings.ForceComplex);
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings1 = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings1.LoadCommand.InvokeAsync();
        Assert.IsTrue(newSettings1.IncludeSimple);
        Assert.IsFalse(newSettings1.ForceComplex);
        Assert.IsTrue(newSettings1.AllowFel);

        // Change only ForceComplex to true, reset IncludeSimple to false, and save
        newSettings1.IncludeSimple = false;
        newSettings1.ForceComplex = true;
        await newSettings1.SaveCommand.InvokeAsync();

        Assert.IsFalse(runtime.Settings.IncludeSimple);
        Assert.IsTrue(runtime.Settings.ForceComplex);
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings2 = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings2.LoadCommand.InvokeAsync();
        Assert.IsFalse(newSettings2.IncludeSimple);
        Assert.IsTrue(newSettings2.ForceComplex);
        Assert.IsTrue(newSettings2.AllowFel);

        // Change both to true and save
        newSettings2.IncludeSimple = true;
        newSettings2.ForceComplex = true;
        await newSettings2.SaveCommand.InvokeAsync();

        Assert.IsTrue(runtime.Settings.IncludeSimple);
        Assert.IsTrue(runtime.Settings.ForceComplex);
        Assert.IsTrue(runtime.Settings.AllowFel);

        var newSettings3 = runtime.Container.GetRequiredService<SettingsViewModel>();
        await newSettings3.LoadCommand.InvokeAsync();
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
        model.ShowsCheckBoxes = true;

        Assert.AreEqual("0 items", model.SelectionSummary);

        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        model.Files[0].IsSelected = false;
        Assert.AreEqual("1 item", model.SelectionSummary);

        model.Files[0].IsSelected = true;
        Assert.AreEqual("1 item | 1 item selected", model.SelectionSummary);

        await model.AddAsync([@"C:\Media\Ocean.mkv", @"C:\Media\City.mkv", @"C:\Media\P81.mkv", @"C:\Media\Sdr.mkv"]);
        Assert.AreEqual("5 items | 5 items selected", model.SelectionSummary);

        foreach (var row in model.Files.Skip(2))
        {
            row.IsSelected = false;
        }
        Assert.AreEqual("5 items | 2 items selected", model.SelectionSummary);

        model.Files[1].IsSelected = false;
        Assert.AreEqual("5 items | 1 item selected", model.SelectionSummary);

        model.Files[0].IsSelected = false;
        Assert.AreEqual("5 items", model.SelectionSummary);
    }

    [TestMethod]
    public void ActiveProgressTextRoundsBatchPercentToInteger()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        model.BatchProgress.Begin(3, "Convert");
        model.BatchProgress.Complete(DoViFixer.Application.Operations.OperationStatus.Completed);

        Assert.AreEqual("33%", model.ActiveProgressText);

        model.BatchProgress.Complete(DoViFixer.Application.Operations.OperationStatus.Completed);

        Assert.AreEqual("67%", model.ActiveProgressText);
        model.BatchProgress.End();
    }

    [TestMethod]
    public Task ActiveProgressPropertiesReflectBatchAndNonBatchOperations() =>
        // Progress<T> needs the app's ordered UI context; on the thread pool, the earlier
        // indeterminate stage report can land after the 40% report and reset the batch to 0%.
        DispatcherThread.RunAsync(ExerciseActiveProgressAsync, TimeSpan.FromSeconds(30));

    private static async Task ExerciseActiveProgressAsync()
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
        model.CancelCommand.Invoke();
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

        Assert.AreEqual("Not checked", row.StatusToolTip);

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
        var shell = new ShellViewModel([media], settings, runtime.Container.GetRequiredService<StartupDependencyCheckViewModel>(), runtime.Container.GetRequiredService<DependencyReportViewModel>());

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
        await settings.LoadCommand.InvokeAsync();
        settings.Destination = @"C:\Media\CustomOutput";
        settings.OtherFolder = true;
        settings.ReplaceOriginal = true;
        settings.ForceComplex = true;
        await settings.SaveCommand.InvokeAsync();

        Assert.AreEqual(@"Output: C:\Media\CustomOutput (Replace original)", media.OutputSummary);
        Assert.IsTrue(media.IsReplaceOriginalActive);
        StringAssert.Contains(media.OutputSummaryToolTip, @"C:\Media\CustomOutput");
        StringAssert.Contains(media.OutputSummaryToolTip, "Replace original after conversion");
        CollectionAssert.AreEqual(new[] { media.SelectionSummary, media.StatusText, media.OutputSummary, media.RetentionSummary, media.FelSummary, media.ArchiveSummary }, updatedStatusItems);
        var outputItem = media.StatusItems.Single(item => item.Text == media.OutputSummary);
        Assert.AreSame(media.OpenSettingsCommand, outputItem.Command);
        Assert.AreEqual(media.OutputSummaryToolTip, outputItem.ToolTipText);
        Assert.IsTrue(media.StatusItems.Single(item => item.Text == media.RetentionSummary).IsEmphasized);
        var felItem = media.StatusItems.Single(item => item.Text == media.FelSummary);
        StringAssert.StartsWith(media.FelSummary, "⚠ FEL:");
        Assert.IsTrue(felItem.IsEmphasized, "Allowing Complex FEL conversion is marked as a warning.");
        StringAssert.Contains(felItem.ToolTipText, "permanently lost");
    }

    [TestMethod]
    public void MediaViewModel_OpenSettingsCommand_RaisesRequestOpenSettings()
    {
        using var runtime = new TestRuntime();
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        bool requested = false;
        media.RequestOpenSettings += () => requested = true;

        Assert.IsTrue(media.OpenSettingsCommand.CanExecute(null));
        media.OpenSettingsCommand.Invoke();
        Assert.IsTrue(requested);
    }

    [TestMethod]
    public void MediaRow_SetConverted_Completed_ClearsPreConversionWarningAndSetsConvertedStatus()
    {
        var row = new MediaRow(@"C:\Media\Movie.mkv");
        row.SetPlan("Enhancement-layer picture data will be lost.");
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
        await settings.LoadCommand.InvokeAsync();

        settings.ConversionWorkers = 16;
        await settings.SaveTask;
        Assert.AreEqual(16, runtime.Settings.ConversionWorkers);
        settings.AnalysisWorkers = 10;
        await settings.SaveTask;
        Assert.AreEqual(10, runtime.Settings.AnalysisWorkers);
        settings.ToolProcessPriority = DoViFixer.Application.Settings.ToolProcessPriority.Normal;
        await settings.SaveTask;
        Assert.AreEqual(DoViFixer.Application.Settings.ToolProcessPriority.Normal, runtime.Settings.ToolProcessPriority);
        settings.PipeBufferKiB = 8192;
        await settings.SaveTask;
        Assert.AreEqual(8192, runtime.Settings.PipeBufferKiB);
        settings.ConversionWorkers = 0;
        settings.AnalysisWorkers = 0;
        await settings.SaveTask;
        Assert.AreEqual(0, runtime.Settings.ConversionWorkers);
        Assert.AreEqual(0, runtime.Settings.AnalysisWorkers);

        Assert.AreEqual(3, settings.VerificationLevel);
        settings.VerificationLevel = 4;
        await settings.SaveTask;
        Assert.AreEqual(DoViFixer.Application.Settings.VerificationLevel.FullIntegrity, runtime.Settings.VerificationLevel);

        settings.RecycleDeletedOriginalsAndBackups = false;
        await settings.SaveTask;
        Assert.IsFalse(runtime.Settings.RecycleDeletedOriginalsAndBackups);

        settings.FreeSpaceMarginPercent = 35;
        await settings.SaveTask;
        Assert.AreEqual(35d, runtime.Settings.FreeSpaceMarginPercent);

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
        await settings.LoadCommand.InvokeAsync();

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

    [TestMethod]
    public async Task MediaRow_ResultAndAnalysisKeepRiskSeparateFromEvidenceAndOutcome()
    {
        using var runtime = new TestRuntime();
        var row = new MediaRow(@"C:\Media\Ocean.mkv");
        Assert.AreEqual("—", row.AnalysisText);
        var media = await runtime.ProbeAsync(row.Path, default);
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.SampledRpu, DoViFixer.Domain.Media.EnhancementLayer.Mel, 1000, 900, 10, 10));
        Assert.AreEqual("Likely safe", row.Status);
        Assert.AreEqual("Scan", row.AnalysisText);
        Assert.AreEqual("Inspection recommended", row.ResultRecommendation);
        Assert.IsTrue(row.CanInspectIncomplete);

        foreach (var method in new[] { DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, DoViFixer.Domain.Analysis.AnalysisMethod.DeepInspection })
        {
            row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(media,
                new(method, DoViFixer.Domain.Media.EnhancementLayer.Fel, 1000, 900, 1, 1,
                    Brightness: method == DoViFixer.Domain.Analysis.AnalysisMethod.DeepInspection ? new(1000, 0, 1000, 0, 0) : null));
            Assert.AreEqual("Low risk", row.Status);
            Assert.AreEqual(MediaStatusTone.LowRisk, row.StatusTone);
            Assert.AreEqual("", row.ResultRecommendation);
            Assert.IsFalse(row.CanInspectIncomplete);
            Assert.IsFalse(row.DetailNotes.Contains("Safe to convert", StringComparison.Ordinal));
            Assert.IsTrue(row.ExaminationRows.Any(detail => detail.Label == "Method"));
        }

        var methodBeforeConversion = row.AnalysisText;
        row.SetConverted(new(row.Path, OperationStatus.Completed, @"C:\Media\Ocean-P81.mkv"));
        Assert.AreEqual("Converted", row.Status);
        Assert.AreEqual(MediaStatusTone.Safe, row.StatusTone);
        Assert.AreEqual(methodBeforeConversion, row.AnalysisText);
        Assert.AreEqual("Low risk", row.DetailRows.Single(detail => detail.Label == "Original risk").Value);
        Assert.AreEqual(MediaStatusTone.LowRisk, row.DetailRows.Single(detail => detail.Label == "Original risk").StatusTone);
        Assert.IsTrue(row.DetailRows.Where(detail => detail.Label != "Original risk").All(detail => detail.StatusTone == MediaStatusTone.None));
        Assert.AreEqual("", row.ResultRecommendation);
        row.SetConverted(new(row.Path, OperationStatus.Partial, @"C:\Media\Ocean-P81.mkv", "Archive warning"));
        Assert.AreEqual("Converted with warnings", row.Status);
        Assert.AreEqual(MediaStatusTone.VerifyRecommended, row.StatusTone);
        Assert.AreEqual("Archive warning", row.Warning);
    }

    [TestMethod]
    public async Task MediaRow_NonProfile7MetadataDescribesConversionApplicability()
    {
        using var runtime = new TestRuntime();
        var row = new MediaRow(@"C:\Media\Format.mkv");
        var media = await runtime.ProbeAsync(row.Path, default);
        foreach (var (profile, hdr, expected) in new[]
        {
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile81, (bool?)true, "Already compatible"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.None, (bool?)true, "No conversion needed"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.None, (bool?)false, "No conversion needed"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.None, (bool?)null, "Unknown format"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile5, (bool?)true, "Unsupported profile"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Other, (bool?)true, "Unsupported profile"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Unknown, (bool?)null, "Unknown format"),
            (DoViFixer.Domain.Media.DolbyVisionProfile.Profile7, (bool?)true, "Not checked")
        })
        {
            row.Analysis = new(media with { Profile = profile, IsHdr = hdr },
                new(DoViFixer.Domain.Analysis.AnalysisMethod.MetadataOnly, DoViFixer.Domain.Media.EnhancementLayer.Unknown, 0, null, 0, 0),
                DoViFixer.Domain.Analysis.AnalysisVerdict.NotApplicable, "Metadata only");
            Assert.AreEqual(expected, row.Status);
            Assert.AreEqual("Metadata only", row.AnalysisText);
            Assert.AreEqual(expected, row.DetailRows.Single(detail => detail.Label == "Risk").Value);
        }
    }

    [TestMethod]
    public async Task MediaRow_MetadataOnlyAndFailedChecksNeverClaimACompletedScan()
    {
        using var runtime = new TestRuntime();
        var row = new MediaRow(@"C:\Media\Ocean.mkv");
        var media = await runtime.ProbeAsync(row.Path, default);
        row.Analysis = new(media, new(DoViFixer.Domain.Analysis.AnalysisMethod.MetadataOnly,
            DoViFixer.Domain.Media.EnhancementLayer.Unknown, 0, null, 0, 0), DoViFixer.Domain.Analysis.AnalysisVerdict.Unknown, "Metadata only");
        Assert.AreEqual("Metadata only", row.AnalysisText);
        Assert.AreEqual("Not checked", row.Status);
        row.Analysis = null;
        row.LastAnalysisMethod = DoViFixer.Domain.Analysis.AnalysisMethod.DeepInspection;
        row.SetAnalysisFailed("Missing evidence");
        Assert.AreEqual("Deep inspection (failed)", row.AnalysisText);
        Assert.AreEqual(MediaStatusTone.HighRisk, row.StatusTone);
    }

    [TestMethod]
    public async Task MediaRow_ReadyToConvertDoesNotClaimLowerRiskForComplexFel()
    {
        using var runtime = new TestRuntime();
        var row = new MediaRow(@"C:\Media\Ocean.mkv");
        row.Analysis = DoViFixer.Domain.Analysis.MediaClassifier.Classify(await runtime.ProbeAsync(row.Path, default),
            new(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, DoViFixer.Domain.Media.EnhancementLayer.Fel, 1000, 2000, 1, 1));
        Assert.AreEqual("High risk", row.Status);
        row.SetPlan("Explicit override");
        Assert.AreEqual(MediaStatusTone.Neutral, row.StatusTone);
        Assert.AreEqual("High risk", row.DetailRows.Single(detail => detail.Label == "Risk").Value);
        row.ClearPlan();
        Assert.AreEqual("High risk", row.Status);
    }

    [TestMethod]
    [DataRow(false, false, "Dolby Vision Profile 7 · MEL", "Dolby Vision Profile 8.1")]
    [DataRow(true, false, "Dolby Vision Profile 8.1", "Dolby Vision Profile 8.1")]
    [DataRow(true, true, "HDR", "HDR")]
    public async Task ConvertUpdatesFormatColumnToResultAnalysisOnlyWhenOriginalIsReplaced(
        bool replaceOriginal,
        bool removeDolbyVision,
        string expectedRowFormat,
        string expectedResultDetailFormat)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(s => s with { ReplaceOriginal = replaceOriginal, RemoveDolbyVision = removeDolbyVision }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = model.Files.Single();
        Assert.AreEqual("Dolby Vision Profile 7 · MEL", row.Format);

        var changedProperties = new List<string?>();
        row.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(MediaRowState.Converted, row.State);
        Assert.AreEqual(expectedRowFormat, row.Format);
        Assert.HasCount(replaceOriginal ? 1 : 2, model.Files);
        if (!replaceOriginal)
        {
            Assert.AreEqual(expectedResultDetailFormat, model.Files[1].Format);
        }
        CollectionAssert.Contains(changedProperties, nameof(MediaRow.Format));

        var formatDetail = row.DetailRows.Single(d => d.Label == "Format");
        Assert.AreEqual("Dolby Vision", formatDetail.Value, "Comparison table keeps the original format on the left.");
        Assert.AreEqual(removeDolbyVision ? "HDR" : "Dolby Vision", formatDetail.Result, "Comparison table shows the produced format on the right.");
        var profileDetail = row.DetailRows.Single(detail => detail.Label == "Profile");
        Assert.AreEqual("7 · MEL", profileDetail.Value);
        Assert.AreEqual(removeDolbyVision ? "" : "8.1", profileDetail.Result);
    }
}
