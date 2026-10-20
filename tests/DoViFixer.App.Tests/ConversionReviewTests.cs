using DoViFixer.App.ViewModels;
using DoViFixer.Application.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class ConversionReviewTests
{
    [TestMethod]
    public async Task ConvertRowDv81ConvertsOnlyClickedProfile7AndPreservesSelection()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Mountain2.mkv", @"C:\Media\P81.mkv", @"C:\Media\Sdr.mkv"]);
        var originalRows = model.Files.ToArray();
        var row = originalRows[0];
        row.IsSelected = false;
        model.Focused = originalRows[1];
        bool[] selections = model.Files.Select(file => file.IsSelected).ToArray();
        var command = model.ConvertRowCommand;
        Assert.IsTrue(command.CanExecute(row));
        Assert.IsFalse(command.CanExecute(originalRows[2]));
        Assert.IsFalse(command.CanExecute(originalRows[3]));
        Assert.IsFalse(command.CanExecute(new MediaRow(row.Path) { Analysis = row.Analysis }));
        var analysis = row.Analysis;
        row.Analysis = null;
        Assert.IsFalse(command.CanExecute(row));
        row.Analysis = analysis;
        bool startingCalled = false;
        model.ConversionStarting += _ =>
        {
            startingCalled = true;
            Assert.IsFalse(command.CanExecute(originalRows[1]), "Other row conversions must be disabled while busy.");
            return Task.CompletedTask;
        };

        await command.ExecuteAsync(row);

        Assert.IsTrue(startingCalled, "Single-file conversion must use the existing settings flush hook.");
        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual(MediaRowState.Converted, row.State);
        Assert.IsTrue(row.Result!.Output!.EndsWith(" - DV P8.1.mkv"));
        Assert.IsNull(originalRows[1].Result);
        Assert.AreSame(originalRows[1], model.Focused);
        CollectionAssert.AreEqual(selections, originalRows.Select(file => file.IsSelected).ToArray());
        Assert.IsFalse(model.Files[1].IsSelected);
        Assert.AreEqual(row.Result.Output, model.Files[1].Path);
        Assert.AreEqual(1, model.BatchProgress.Total);
        Assert.IsTrue(command.CanExecute(originalRows[1]));
    }

    [TestMethod]
    public async Task ConvertRowDv81HonorsFelPolicyWithoutSelection()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv"]);
        var row = model.Files.Single();
        row.IsSelected = false;

        await model.ConvertRowCommand.InvokeAsync(row);

        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual(MediaRowState.Skipped, row.State);
        StringAssert.Contains(row.Notice, "Include Simple FEL");
    }

    [TestMethod]
    public async Task FailedConversionKeepsAnalysisAndReportsTheOperationFailure()
    {
        using var runtime = new TestRuntime
        {
            DuringConversion = _ => throw new InvalidDataException("Verification failed: tags metadata changed.")
        };
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = model.Files.Single();
        string classification = row.Classification;

        await model.ConvertRowCommand.InvokeAsync(row);

        Assert.AreEqual(MediaRowState.Failed, row.State);
        Assert.AreEqual("Conversion failed", row.Status);
        Assert.AreEqual(classification, row.Classification, "A failed conversion must not relabel the completed analysis.");
        Assert.IsFalse(row.HasAnalysisError);
        Assert.IsFalse(row.DetailNotes.Contains("Analysis failed"), row.DetailNotes);
        StringAssert.Contains(row.Result!.Message, "tags metadata changed");
        StringAssert.Contains(row.StatusToolTip, "tags metadata changed");
    }

    [TestMethod]
    public async Task InspectionFailureWhilePreparingConversionIsReportedAsAnalysisFailure()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);
        var row = model.Files.Single();
        runtime.DuringAnalysis = _ => throw new IOException("Fixture inspection failure.");

        await model.ConvertRowCommand.InvokeAsync(row);

        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual(MediaRowState.Failed, row.State);
        Assert.AreEqual("Analysis failed", row.Classification);
        StringAssert.Contains(row.AnalysisError, "Fixture inspection failure.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelConversionClearsPlanAndKeepsOriginalBatchTotal(bool cancelBatch)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { IncludeSimple = true, ConversionWorkers = 1 }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv", @"C:\Media\Mountain.mkv"]);
        var first = model.Files[0];
        var second = model.Files[1];
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DuringConversion = async token =>
        {
            if (runtime.Conversions == 1)
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                finally
                {
                    recovering.TrySetResult();
                    await releaseCleanup.Task;
                }
            }
            else
            {
                nextStarted.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };

        var conversion = model.ConvertCommand.InvokeAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(first.HasWarning);
            if (cancelBatch)
            {
                model.CancelCommand.Invoke();
            }
            else
            {
                model.CancelFileCommand.Invoke(first);
            }

            await recovering.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(first.HasWarning, "Cancellation must clear the warning before cleanup finishes.");
            releaseCleanup.TrySetResult();
            if (!cancelBatch)
            {
                await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual("1 of 2 files processed · 1 cancelled", model.ActiveProgressSummary);
                release.TrySetResult();
            }

            await conversion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreEqual(MediaRowState.Cancelled, first.State);
            Assert.AreEqual("Conversion cancelled", first.Status);
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
            Assert.IsTrue(model.ConvertCommand.CanExecute(null));
            Assert.AreEqual(2, model.BatchProgress.Total);
            Assert.AreEqual(cancelBatch ? 1 : 2, model.BatchProgress.Processed);
            Assert.AreEqual(cancelBatch ? 50d : 100d, model.BatchProgress.Percent);
            runtime.DuringConversion = null;
            await model.ConvertCommand.InvokeAsync();
            Assert.AreEqual(MediaRowState.Converted, first.State, "Retry must work without selecting the file again.");
        }
        finally
        {
            model.CancelCommand.Invoke();
            releaseCleanup.TrySetResult();
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

        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(1, runtime.Messages.Count);
        StringAssert.Contains(runtime.Messages[0], "The configured temporary storage folder does not exist");
        StringAssert.Contains(runtime.Messages[0], nonExistentPath);
        Assert.AreEqual(0, runtime.Conversions);

        await runtime.UpdateAsync(s => s with { RemoveDolbyVision = true }, default);
        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(2, runtime.Messages.Count);
        Assert.AreEqual(0, runtime.Conversions);
    }

    [TestMethod]
    public async Task ConvertDv81InBatchPreparesAndConvertsEligibleCandidatesAndSkipsFelByDefault()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        var originalRows = model.Files.ToArray();
        originalRows[1].IsSelected = true;

        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", originalRows[0].Status);
        Assert.AreEqual(MediaRowState.Converted, originalRows[0].State);
        Assert.IsTrue(originalRows[0].CanOpenResult);

        Assert.AreEqual("Conversion skipped", originalRows[1].Status);
        Assert.AreEqual(MediaRowState.Skipped, originalRows[1].State);
        StringAssert.Contains(originalRows[1].Notice, "Simple FEL conversion requires enabling 'Include Simple FEL' in Settings.");
        Assert.IsFalse(originalRows[1].HasWarning, "A skip reason is not a conversion warning.");
        Assert.AreEqual(originalRows[1].Notice, originalRows[1].StatusToolTip);
        originalRows[1].IsSelected = true;
        Assert.IsFalse(originalRows[1].HasWarning);
        Assert.AreEqual(originalRows[1].Notice, originalRows[1].StatusToolTip);
    }

    [TestMethod]
    public async Task RowsOfferConvertOnlyWhereTheFelSettingsAllowIt()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        var (mel, simple, complex) = (model.Files[0], model.Files[1], model.Files[2]);
        Assert.AreEqual("Inspect", simple.NextStepText, "The scan recommends inspecting the Simple FEL file.");

        model.UpdateSettingsSummary(new DoViFixer.Application.Settings.UserSettings());
        Assert.IsTrue(mel.OffersConversion, "MEL always converts.");
        Assert.IsFalse(simple.OffersConversion, "Convert would only skip Simple FEL while Settings skip it.");
        Assert.IsFalse(complex.OffersConversion);

        model.UpdateSettingsSummary(new DoViFixer.Application.Settings.UserSettings { IncludeSimple = true });
        Assert.IsTrue(simple.OffersConversion, "Converting inspects the Simple FEL file in full first.");
        Assert.IsFalse(complex.OffersConversion);

        model.UpdateSettingsSummary(new DoViFixer.Application.Settings.UserSettings { IncludeSimple = true, ForceComplex = true });
        Assert.IsTrue(complex.OffersConversion);
        var added = new MediaRow(@"C:\Media\Added.mkv");
        model.Files.Add(added);
        Assert.IsTrue(added.ConvertsSimpleFel && added.ConvertsComplexFel, "Rows added later follow the current settings.");
    }

    [TestMethod]
    public async Task ConvertDv81WithIncludeSimpleConvertsSimpleFelAndSkipsComplexFel()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { IncludeSimple = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Ocean.mkv", @"C:\Media\City.mkv"]);
        var originalRows = model.Files.ToArray();
        originalRows[1].IsSelected = true;

        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", originalRows[0].Status);
        Assert.IsFalse(originalRows[0].HasWarning);
        Assert.AreEqual(MediaRowState.Converted, originalRows[0].State);

        Assert.AreEqual("Conversion skipped", originalRows[1].Status);
        Assert.AreEqual(MediaRowState.Skipped, originalRows[1].State);
        StringAssert.Contains(originalRows[1].Notice, "Complex FEL conversion requires enabling 'Allow Complex FEL conversion' in Settings.");
        Assert.IsFalse(originalRows[1].HasWarning, "A skip reason is not a conversion warning.");
        Assert.AreEqual(originalRows[1].Notice, originalRows[1].StatusToolTip);
    }

    [TestMethod]
    public async Task ConvertDv81WithForceComplexConvertsComplexFel()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { ForceComplex = true }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\City.mkv"]);
        model.Files[0].IsSelected = true;

        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files[0].Status);
        Assert.IsFalse(model.Files[0].HasWarning);
        Assert.AreEqual(MediaRowState.Converted, model.Files[0].State);
        Assert.AreEqual(model.Files[0].Result!.Output, model.Files[1].Path);
    }

    [TestMethod]
    public async Task ConvertHdrConvertsToHdrTargetDirectly()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);

        await runtime.UpdateAsync(s => s with { RemoveDolbyVision = true }, default);
        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(1, runtime.Conversions);
        Assert.AreEqual("Converted", model.Files[0].Status);
        Assert.AreEqual(MediaRowState.Converted, model.Files[0].State);
        Assert.IsTrue(model.Files[0].CanOpenResult);
        Assert.IsTrue(model.Files[0].Result!.Output!.EndsWith(" - HDR10.mkv"));
        Assert.AreEqual(model.Files[0].Result!.Output, model.Files[1].Path);
        Assert.AreEqual("HDR", model.Files[1].Format);
    }

    [TestMethod]
    public async Task ConvertSkipsCandidateWhenInsufficientDiskSpaceBeforeFullInspection()
    {
        using var runtime = new TestRuntime();
        runtime.FailAvailableSpace = true;
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);

        await model.ConvertCommand.InvokeAsync();

        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual(0, runtime.FullAnalyses, "Long operation full inspection must NOT run when disk space check fails beforehand.");
        Assert.AreEqual("Conversion skipped", model.Files.Single().Status);
        Assert.AreEqual(MediaRowState.Skipped, model.Files.Single().State);
        StringAssert.Contains(model.Files.Single().Notice, "Insufficient temporary disk space");
        Assert.IsFalse(model.Files.Single().HasWarning, "A skip reason is not a conversion warning.");
        Assert.AreEqual(model.Files.Single().Notice, model.Files.Single().StatusToolTip);
    }
}
