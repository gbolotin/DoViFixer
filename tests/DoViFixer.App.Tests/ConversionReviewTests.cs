using DoViFixer.App.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class ConversionReviewTests
{
    [TestMethod]
    public async Task ConvertShowsPopupAndHaltsWhenConfiguredTempDirDoesNotExist()
    {
        using var runtime = new TestRuntime();
        string nonExistentPath = Path.Combine(Path.GetTempPath(), "DoViFixer_NonExistentTemp_" + Guid.NewGuid());
        await runtime.UpdateAsync(settings => settings with { TemporaryDirectory = nonExistentPath }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);

        await model.ConvertDv81Command.ExecuteAsync();

        Assert.AreEqual(1, runtime.Messages.Count);
        StringAssert.Contains(runtime.Messages[0], "The configured temporary storage folder does not exist");
        StringAssert.Contains(runtime.Messages[0], nonExistentPath);
        Assert.AreEqual(0, runtime.Conversions);

        await model.ConvertHdrCommand.ExecuteAsync();

        Assert.AreEqual(2, runtime.Messages.Count);
        Assert.AreEqual(0, runtime.Conversions);
    }

    [TestMethod]
    public async Task ConvertDv81InBatchPreparesAndConvertsEligibleCandidatesAndSkipsFelByDefault()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        model.Files[1].IsSelected = true;

        await model.ConvertDv81Command.ExecuteAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files[0].Status);
        Assert.AreEqual(MediaRowState.Converted, model.Files[0].State);
        Assert.IsTrue(model.Files[0].CanOpenResult);

        Assert.AreEqual("Conversion skipped", model.Files[1].Status);
        Assert.AreEqual(MediaRowState.Skipped, model.Files[1].State);
        StringAssert.Contains(model.Files[1].Notice, "Simple FEL conversion requires enabling 'Include Simple FEL' in Settings.");
    }

    [TestMethod]
    public async Task ConvertDv81WithIncludeSimpleConvertsSimpleFelAndSkipsComplexFel()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { IncludeSimple = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        model.Files[1].IsSelected = true;

        await model.ConvertDv81Command.ExecuteAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files[0].Status);
        Assert.IsFalse(model.Files[0].HasWarning);
        Assert.AreEqual(MediaRowState.Converted, model.Files[0].State);

        Assert.AreEqual("Conversion skipped", model.Files[1].Status);
        Assert.AreEqual(MediaRowState.Skipped, model.Files[1].State);
        StringAssert.Contains(model.Files[1].Notice, "Complex FEL conversion requires enabling 'Force Complex FEL' in Settings.");
    }

    [TestMethod]
    public async Task ConvertDv81WithForceComplexConvertsComplexFel()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { ForceComplex = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\City.mkv"]);
        model.Files[0].IsSelected = true;

        await model.ConvertDv81Command.ExecuteAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files.Single().Status);
        Assert.IsFalse(model.Files.Single().HasWarning);
        Assert.AreEqual(MediaRowState.Converted, model.Files.Single().State);
    }

    [TestMethod]
    public async Task ConvertHdrConvertsToHdrTargetDirectly()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);

        await model.ConvertHdrCommand.ExecuteAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files.Single().Status);
        Assert.AreEqual(MediaRowState.Converted, model.Files.Single().State);
        Assert.IsTrue(model.Files.Single().CanOpenResult);
        Assert.IsTrue(model.Files.Single().PlannedOutput.EndsWith(" - HDR10.mkv"));
    }

    [TestMethod]
    public async Task ConvertSkipsCandidateWhenInsufficientDiskSpaceBeforeFullInspection()
    {
        using var runtime = new TestRuntime();
        runtime.FailAvailableSpace = true;
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);

        await model.ConvertDv81Command.ExecuteAsync();

        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual(0, runtime.FullAnalyses, "Long operation full inspection must NOT run when disk space check fails beforehand.");
        Assert.AreEqual("Conversion skipped", model.Files.Single().Status);
        Assert.AreEqual(MediaRowState.Skipped, model.Files.Single().State);
        StringAssert.Contains(model.Files.Single().Notice, "Insufficient temporary disk space");
    }
}
