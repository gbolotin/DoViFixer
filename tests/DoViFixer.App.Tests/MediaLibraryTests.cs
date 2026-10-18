using DoViFixer.App.ViewModels;
using DoViFixer.Application.Library;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class MediaLibraryTests
{
    [TestMethod]
    [DataRow("Remove")]
    [DataRow("Clear")]
    [DataRow("Remove missing")]
    public async Task RemovingRowsForgetsTheirLibraryRecordsAcrossReload(string action)
    {
        using var runtime = new TestRuntime();
        string first = @"C:\Media\First.mkv";
        string second = @"C:\Media\Second.mkv";
        string outside = @"C:\Media\Outside.mkv";
        runtime.LibraryFiles[first] = new("first", runtime.Identify(first), null);
        runtime.LibraryFiles[second] = new("second", runtime.Identify(second), null);
        if (action == "Remove missing")
        {
            runtime.MissingFiles.Add(first);
        }
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        await media.LoadLibraryAsync();
        // A library record outside this list must survive Clear all.
        runtime.LibraryFiles[outside] = new("outside", runtime.Identify(outside), null);
        if (action == "Clear")
        {
            await media.ClearAllCommand.InvokeAsync();
        }
        else if (action == "Remove missing")
        {
            await media.RemoveMissingCommand.InvokeAsync();
        }
        else
        {
            await media.RemoveFileCommand.InvokeAsync(media.Files.Single(row => row.Path == first));
        }
        Assert.IsFalse(runtime.LibraryFiles.ContainsKey(first));
        Assert.AreEqual(action != "Clear", runtime.LibraryFiles.ContainsKey(second));
        Assert.IsTrue(runtime.LibraryFiles.ContainsKey(outside));
        Assert.HasCount(0, runtime.DeletedFiles, "Removing rows must never delete media.");
        media.Dispose();
        var reopened = runtime.Container.GetRequiredService<MediaViewModel>();
        await reopened.LoadLibraryAsync();
        Assert.IsFalse(reopened.Files.Any(row => row.Path == first));
        Assert.AreEqual(action != "Clear", reopened.Files.Any(row => row.Path == second));
        await reopened.CancelPreviewsAsync();
    }

    [TestMethod]
    public async Task FailedLibraryRemovalKeepsRowsForRetry()
    {
        using var runtime = new TestRuntime();
        string path = @"C:\Media\Keep.mkv";
        runtime.LibraryFiles[path] = new("keep", runtime.Identify(path), null);
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        await media.LoadLibraryAsync();
        runtime.ForgetLibraryFailure = new IOException("Library is unavailable.");
        await media.ClearAllCommand.InvokeAsync();
        Assert.HasCount(1, media.Files);
        Assert.HasCount(1, runtime.LibraryFiles);
        Assert.AreEqual(DoViFixer.App.Presentation.Application.ViewStatus.Error, media.Status);
        runtime.ForgetLibraryFailure = null;
        await media.ClearAllCommand.InvokeAsync();
        Assert.HasCount(0, media.Files);
        Assert.HasCount(0, runtime.LibraryFiles);
        await media.CancelPreviewsAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeleteLibraryClearsSavedRecordsAndCurrentListButKeepsPreviews(bool failFirst)
    {
        using var runtime = new TestRuntime();
        string path = @"C:\Media\Keep.mkv";
        runtime.LibraryFiles[path] = new("keep", runtime.Identify(path), null);
        runtime.CachedFrames = 3;
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var media = (MediaViewModel)shell.CurrentPage!;
        await media.LoadLibraryAsync();
        var settings = shell.Settings;
        if (failFirst)
        {
            runtime.DeleteLibraryFailure = new IOException("Library is unavailable.");
            await settings.DeleteLibraryCommand.InvokeAsync();
            Assert.HasCount(1, media.Files, "A failed deletion must keep the current list.");
            Assert.HasCount(1, runtime.LibraryFiles);
            runtime.DeleteLibraryFailure = null;
        }
        await settings.DeleteLibraryCommand.InvokeAsync();
        Assert.HasCount(0, runtime.LibraryFiles);
        Assert.HasCount(0, media.Files);
        Assert.IsNull(media.Focused);
        Assert.IsFalse(media.ClearAllCommand.CanExecute(null));
        Assert.HasCount(0, runtime.DeletedFiles);
        Assert.AreEqual(3, runtime.CachedFrames);
        StringAssert.Contains(settings.StatusMessage, "Deleted 1 library record");
        await media.CancelPreviewsAsync();
    }

    [TestMethod]
    public async Task PublishedConversionResultReturnsWithItsRememberedFile()
    {
        using var runtime = new TestRuntime();
        string path = @"C:\Media\Mountain.mkv";
        var source = runtime.Identify(path);
        var analysis = MediaClassifier.Classify(await runtime.ProbeAsync(path, default), new(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 24, null, 1, 1));
        runtime.LibraryFiles[path] = new("converted", source, analysis);
        var result = new DoViFixer.Application.Operations.OperationItemResult(path,
            DoViFixer.Application.Operations.OperationStatus.Completed, @"C:\Media\Output.mkv");
        runtime.ConversionResults[source] = result;
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.LoadLibraryAsync();
        Assert.AreEqual(MediaRowState.Converted, model.Files.Single().State);
        Assert.AreEqual(result, model.Files.Single().Result);
        Assert.IsNotNull(model.Files.Single().ResultAnalysis, "Reloading a conversion must also load its output metadata.");
        foreach (string label in new[] { "Size", "Date modified", "Length", "Format", "Profile", "Resolution", "Frame rate" })
        {
            Assert.AreNotEqual("Unknown", model.Files.Single().DetailRows.Single(detail => detail.Label == label).Result, label);
        }
        Assert.AreEqual(0, runtime.Analyses);
    }

    [TestMethod]
    public async Task RememberedFilesReturnWithoutScanningAndMissingPresentationIsUnchanged()
    {
        using var runtime = new TestRuntime();
        string present = @"C:\Media\Mountain.mkv";
        string missing = @"C:\Media\Missing.mkv";
        var media = await runtime.ProbeAsync(present, default);
        var analysis = MediaClassifier.Classify(media, new(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 24, null, 1, 1));
        runtime.LibraryFiles[present] = new("present", runtime.Identify(present), analysis);
        runtime.LibraryFiles[missing] = new("missing", runtime.Identify(missing), analysis with { Media = media with { Source = runtime.Identify(missing) } });
        runtime.MissingFiles.Add(missing);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.LoadLibraryAsync();
        Assert.HasCount(2, model.Files);
        Assert.AreEqual(0, runtime.Analyses, "Loading the library must not start a scan, even when automatic scanning is on.");
        var missingRow = model.Files.Single(row => row.Path == missing);
        Assert.IsTrue(missingRow.IsMissing);
        Assert.AreEqual(MediaRow.MissingStatus, missingRow.Status);
        Assert.IsFalse(missingRow.IsSelected);
        await model.LoadLibraryAsync();
        Assert.HasCount(2, model.Files, "Repeated activation must not duplicate remembered rows.");

        runtime.CachedFrames = 3;
        var settings = runtime.Container.GetRequiredService<SettingsViewModel>();
        await settings.CleanLibraryCommand.InvokeAsync();
        Assert.IsFalse(runtime.LibraryFiles.ContainsKey(missing));
        Assert.IsTrue(runtime.LibraryFiles.ContainsKey(present));
        Assert.AreEqual(3, runtime.CachedFrames);
        Assert.IsTrue(model.Files.Contains(missingRow), "Cleaning the library must not change the current missing-file presentation.");
    }

    [TestMethod]
    public async Task AddingAMovedRememberedFileReplacesItsOldRowWithoutScanningWhenDisabled()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        string oldPath = @"C:\Media\Before.mkv";
        string newPath = @"C:\Media\After.mkv";
        runtime.LibraryFiles[oldPath] = new("same-content", runtime.Identify(oldPath), null);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.LoadLibraryAsync();
        runtime.LibraryFiles[newPath] = new("same-content", runtime.Identify(newPath), null);
        await model.AddAsync([newPath]);
        Assert.HasCount(1, model.Files);
        Assert.AreEqual(newPath, model.Files[0].Path);
        Assert.AreEqual(0, runtime.Analyses);
        await model.CancelPreviewsAsync();
    }
}
