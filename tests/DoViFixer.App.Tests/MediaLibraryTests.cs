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
    public async Task PublishedConversionResultReturnsWithItsRememberedFile()
    {
        using var runtime = new TestRuntime();
        string path = @"C:\Media\Mountain.mkv";
        var source = runtime.Identify(path);
        runtime.LibraryFiles[path] = new("converted", source, null);
        var result = new DoViFixer.Application.Operations.OperationItemResult(path,
            DoViFixer.Application.Operations.OperationStatus.Completed, @"C:\Media\Output.mkv");
        runtime.ConversionResults[source] = result;
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.LoadLibraryAsync();
        Assert.AreEqual(MediaRowState.Converted, model.Files.Single().State);
        Assert.AreEqual(result, model.Files.Single().Result);
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
