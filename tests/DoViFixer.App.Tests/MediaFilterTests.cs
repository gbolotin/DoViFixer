using DoViFixer.App.ViewModels;
using DoViFixer.Domain.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class MediaFilterTests
{
    private const string Profile7 = @"C:\Media\Ocean.mkv";
    private const string Profile81 = @"C:\Media\OceanP81.mkv";
    private const string NoDolbyVision = @"C:\Media\OceanSdr.mkv";
    private const string Unscanned = @"C:\Media\City.mkv";

    private static MediaFilterOption Option(MediaViewModel model, MediaFileFilter filter) => model.FilterOptions.Single(option => option.Filter == filter);

    private static MediaRow Row(MediaViewModel model, string path) => model.Files.Single(row => row.Path == path);

    private static string[] Shown(MediaViewModel model) => [.. model.Files.Where(row => model.ShownFilePredicate(row)).Select(row => row.Path)];

    private static async Task<MediaViewModel> CreateAsync(TestRuntime runtime)
    {
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Profile7, Profile81, NoDolbyVision]);
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        await model.AddAsync([Unscanned]);
        return model;
    }

    [TestMethod]
    public async Task FilterOptionsCountFilesByScanResult()
    {
        using var runtime = new TestRuntime();
        var model = await CreateAsync(runtime);

        Assert.AreEqual(MediaFileFilter.All, model.FileFilter);
        Assert.IsTrue(Option(model, MediaFileFilter.All).IsSelected);
        Assert.AreEqual("All (4)", Option(model, MediaFileFilter.All).Label);
        Assert.AreEqual("Profile 7 (1)", Option(model, MediaFileFilter.Profile7).Label);
        Assert.AreEqual("Profile 8.1 (1)", Option(model, MediaFileFilter.Profile81).Label);
        Assert.AreEqual("No Dolby Vision (1)", Option(model, MediaFileFilter.NoDolbyVision).Label);
        Assert.AreEqual("Not scanned / failed (1)", Option(model, MediaFileFilter.NotScanned).Label);
        Assert.IsFalse(Option(model, MediaFileFilter.OtherProfiles).IsVisible, "Other profiles appear only when a file needs them.");
        CollectionAssert.AreEqual(new[] { Profile7, Profile81, NoDolbyVision, Unscanned }, Shown(model));

        Row(model, Profile7).AnalysisError = "Fixture analysis error.";
        Assert.AreEqual(MediaFileFilter.NotScanned, Row(model, Profile7).FilterGroup, "A failed analysis joins the unscanned files.");
        Assert.AreEqual("Profile 7 (0)", Option(model, MediaFileFilter.Profile7).Label);
        Assert.AreEqual("Not scanned / failed (2)", Option(model, MediaFileFilter.NotScanned).Label);
    }

    [TestMethod]
    public async Task ChoosingAFilterShowsOnlyItsFilesAndScopesSelection()
    {
        using var runtime = new TestRuntime();
        var model = await CreateAsync(runtime);
        foreach (var row in model.Files)
        {
            row.IsSelected = true;
        }

        Option(model, MediaFileFilter.NoDolbyVision).IsSelected = true;
        Assert.AreEqual(MediaFileFilter.NoDolbyVision, model.FileFilter);
        Assert.IsFalse(Option(model, MediaFileFilter.All).IsSelected);
        CollectionAssert.AreEqual(new[] { NoDolbyVision }, Shown(model));
        Assert.AreEqual(NoDolbyVision, model.Focused?.Path, "A hidden focused file gives way to the first shown file.");
        Assert.AreEqual("1 of 4 items shown | 1 item selected", model.SelectionSummary);
        Assert.AreEqual(true, model.AllFilesSelected);

        model.ToggleSelectAllCommand.Invoke();
        Assert.IsFalse(Row(model, NoDolbyVision).IsSelected);
        Assert.IsTrue(Row(model, Profile7).IsSelected, "Select all leaves hidden files alone.");
        Assert.AreEqual(false, model.AllFilesSelected);
        Assert.IsFalse(model.InspectCommand.CanExecute(null), "Hidden selected files do not enable batch actions.");
        Assert.IsFalse(model.ConvertDv81Command.CanExecute(null));

        model.FileFilter = MediaFileFilter.OtherProfiles;
        Assert.IsTrue(Option(model, MediaFileFilter.OtherProfiles).IsVisible, "The chosen filter stays visible even when it shows no files.");
        Assert.IsTrue(model.HasNoShownFiles);
        Assert.IsNull(model.Focused);
        Assert.IsFalse(model.ScanCommand.CanExecute(null));

        model.FileFilter = MediaFileFilter.All;
        Assert.IsFalse(model.HasNoShownFiles);
        Assert.AreEqual("4 items | 3 items selected", model.SelectionSummary);
    }

    [TestMethod]
    public async Task BatchActionsProcessOnlyShownFiles()
    {
        using var runtime = new TestRuntime();
        var model = await CreateAsync(runtime);
        foreach (var row in model.Files)
        {
            row.IsSelected = true;
        }

        model.FileFilter = MediaFileFilter.Profile7;
        await model.InspectCommand.InvokeAsync();
        Assert.AreEqual(AnalysisMethod.FullRpu, Row(model, Profile7).LastAnalysisMethod);
        Assert.AreEqual(AnalysisMethod.SampledRpu, Row(model, Profile81).LastAnalysisMethod, "Hidden files are not inspected.");
        Assert.AreEqual(AnalysisMethod.SampledRpu, Row(model, Unscanned).LastAnalysisMethod);

        model.FileFilter = MediaFileFilter.NotScanned;
        await model.ScanCommand.InvokeAsync();
        Assert.AreEqual(MediaFileFilter.Profile7, Row(model, Unscanned).FilterGroup);
        Assert.AreEqual(MediaRowState.Scanned, Row(model, Unscanned).State);
        Assert.AreEqual("Not scanned / failed (0)", Option(model, MediaFileFilter.NotScanned).Label);
        Assert.AreEqual("Profile 7 (2)", Option(model, MediaFileFilter.Profile7).Label);
        Assert.IsTrue(model.HasNoShownFiles, "Scanned files leave the unscanned filter.");
    }
}
