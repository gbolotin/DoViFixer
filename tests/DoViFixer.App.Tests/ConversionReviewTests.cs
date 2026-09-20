using DoViFixer.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prism.Ioc;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class ConversionReviewTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow(@"D:\Scratch folder")]
    [DataRow(@"relative-scratch")]
    public async Task ReviewShowsResolvedTemporaryFolderAndScratchForEveryFileBeforeApproval(string? temporaryDirectory)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { TemporaryDirectory = temporaryDirectory }, default);
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);

        await model.ConvertCommand.ExecuteAsync();

        string folder = Path.GetFullPath(temporaryDirectory ?? Path.GetTempPath());
        foreach (var row in model.Files)
        {
            int start = model.Review.IndexOf(row.Path, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0);
            int end = model.Review.IndexOf("\n\n", start, StringComparison.Ordinal);
            string entry = end < 0 ? model.Review[start..] : model.Review[start..end];
            StringAssert.Contains(entry, $"Temporary folder: {folder}");
            StringAssert.Contains(entry, "A separate job subfolder is created here when conversion starts.");
            StringAssert.Contains(entry, $"Required scratch space for this file: {321073741824L / 1073741824d:0.0} GiB ({321073741824L:N0} bytes)");
            StringAssert.Contains(entry, "Output space is additional.");
            StringAssert.Contains(entry, "original: retained");
        }

        Assert.AreEqual(0, runtime.Conversions);
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));
        await model.ApproveCommand.ExecuteAsync();
        Assert.AreEqual(2, runtime.Conversions);
    }

    [TestMethod]
    public async Task ChangedOptionsClearStorageReviewAndRequireFreshPlan()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.Resolve<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        await model.ConvertCommand.ExecuteAsync();

        model.Hdr10 = true;

        Assert.IsFalse(model.Review.Contains("Temporary folder:", StringComparison.Ordinal));
        Assert.IsFalse(model.Review.Contains("Required scratch space for this file:", StringComparison.Ordinal));
        Assert.IsFalse(model.ApproveCommand.CanExecute(null));
        await model.ApproveCommand.ExecuteAsync();
        Assert.AreEqual(0, runtime.Conversions);
        await model.ReviewCommand.ExecuteAsync();
        StringAssert.Contains(model.Review, "Temporary folder:");
        StringAssert.Contains(model.Review, "Required scratch space for this file:");
        StringAssert.Contains(model.Review, "Target: HDR10");
        Assert.IsTrue(model.ApproveCommand.CanExecute(null));
    }
}
