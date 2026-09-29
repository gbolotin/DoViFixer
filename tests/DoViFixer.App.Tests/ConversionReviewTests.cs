using DoViFixer.App.ViewModels;
using DoViFixer.Application.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class ConversionReviewTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelConversionClearsPlanAndKeepsOriginalBatchTotal(bool cancelBatch)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { IncludeSimple = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv", @"C:\Media\Mountain.mkv"]);
        var first = model.Files[0];
        var second = model.Files[1];
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringConversion = async token =>
        {
            if (runtime.Conversions == 1)
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            else
            {
                nextStarted.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };

        var conversion = model.ConvertDv81Command.ExecuteAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(first.HasWarning);
            Assert.IsFalse(string.IsNullOrEmpty(first.PlannedOutput));
            if (cancelBatch)
            {
                model.CancelCommand.Execute();
            }
            else
            {
                model.CancelFileCommand.Execute(first);
                await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual("1 of 2 files processed · 1 cancelled", model.ActiveProgressSummary);
                release.TrySetResult();
            }

            await conversion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(MediaRowState.Cancelled, first.State);
            Assert.AreEqual("Conversion cancelled", first.Status);
            Assert.AreEqual("", first.PlannedOutput);
            Assert.IsFalse(first.HasWarning);
            Assert.IsFalse(first.CanOpenResult);
            Assert.AreEqual(OperationStatus.Cancelled, first.Result!.Status);
            Assert.IsNull(first.Result.Output);
            StringAssert.Contains(first.Result.Message, "Original retained");
            Assert.AreEqual(first.Result.Message, first.StatusToolTip);
            Assert.AreEqual(cancelBatch ? MediaRowState.Cancelled : MediaRowState.Converted, second.State);
            Assert.IsTrue(first.IsSelected, "Cancelling a conversion must preserve its selection for retry.");
            Assert.IsTrue(second.IsSelected, "Cancelling the batch must preserve queued selections.");
            Assert.IsTrue(first.SelectionEnabled);
            Assert.IsTrue(second.SelectionEnabled);
            Assert.IsTrue(model.ConvertDv81Command.CanExecute(null));
            Assert.AreEqual(2, model.BatchProgress.Total);
            Assert.AreEqual(cancelBatch ? 1 : 2, model.BatchProgress.Processed);
            Assert.AreEqual(cancelBatch ? 50d : 100d, model.BatchProgress.Percent);
            runtime.DuringConversion = null;
            await model.ConvertDv81Command.ExecuteAsync();
            Assert.AreEqual(MediaRowState.Converted, first.State, "Retry must work without selecting the file again.");
        }
        finally
        {
            model.CancelCommand.Execute();
            release.TrySetResult();
            await conversion.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

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
        Assert.IsTrue(model.Files[1].HasWarning);
        Assert.AreEqual(model.Files[1].Notice, model.Files[1].Warning);
        model.Files[1].IsSelected = true;
        Assert.IsTrue(model.Files[1].HasWarning, "Reselecting a skipped file must retain its skip reason until retry.");
        Assert.AreEqual(model.Files[1].Notice, model.Files[1].Warning);
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
        Assert.IsTrue(model.Files[1].HasWarning);
        Assert.AreEqual(model.Files[1].Notice, model.Files[1].Warning);
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
        Assert.IsTrue(model.Files.Single().HasWarning);
        Assert.AreEqual(model.Files.Single().Notice, model.Files.Single().Warning);
    }
}
