using DoViFixer.App.ViewModels;
using DoViFixer.Application.Conversion;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class ProgressViewModelTests
{
    [TestMethod]
    public void ProgressStartsIndeterminateAndResetsForRetry()
    {
        var model = new ProgressViewModel();
        Assert.IsFalse(model.IsIndeterminate);
        Assert.AreEqual("", model.StageProgressText);

        model.Start("Extracting");
        Assert.IsTrue(model.IsIndeterminate);
        Assert.AreEqual("Working…", model.StageProgressText);

        model.Update("Extracting", 45);
        model.End();
        Assert.IsFalse(model.IsIndeterminate);
        Assert.AreEqual(45, model.StagePercent);
        Assert.AreEqual("45%", model.StageProgressText);

        model.Start("Retrying");
        Assert.AreEqual("Retrying", model.Stage);
        Assert.AreEqual(0, model.StagePercent);
        Assert.IsTrue(model.IsIndeterminate);
        Assert.AreEqual("Working…", model.StageProgressText);

        model.End();
        Assert.IsFalse(model.IsIndeterminate);
        Assert.AreEqual("", model.StageProgressText);
    }

    [TestMethod]
    [DataRow(-10d, 0d)]
    [DataRow(125d, 100d)]
    [DataRow(42.5d, 42.5d)]
    public void MeasurablePercentagesAreClamped(double reported, double expected)
    {
        var model = new ProgressViewModel();
        model.Start("Extracting");
        model.Update("Extracting", reported);

        Assert.AreEqual(expected, model.StagePercent);
        Assert.IsFalse(model.IsIndeterminate);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    public void MissingOrNonFinitePercentagesAreIndeterminate(double? reported)
    {
        var model = new ProgressViewModel();
        model.Start("Extracting");
        model.Update("Extracting", 75);
        model.Update("Converting metadata", reported);

        Assert.AreEqual("Converting metadata", model.Stage);
        Assert.AreEqual(0, model.StagePercent);
        Assert.IsTrue(model.IsIndeterminate);
        Assert.AreEqual("Working…", model.StageProgressText);
    }

    [TestMethod]
    public void ProgressNotifiesBindingsWhenStagePercentageOrLifecycleChanges()
    {
        var model = new ProgressViewModel();
        var changed = new List<string>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        model.Start("Scanning");
        CollectionAssert.Contains(changed, nameof(model.IsIndeterminate));
        CollectionAssert.Contains(changed, nameof(model.StageProgressText));

        changed.Clear();
        model.Update("Inspecting", 25);
        CollectionAssert.Contains(changed, nameof(model.Stage));
        CollectionAssert.Contains(changed, nameof(model.StagePercent));
        CollectionAssert.Contains(changed, nameof(model.IsIndeterminate));
        CollectionAssert.Contains(changed, nameof(model.StageProgressText));

        model.Update("Converting metadata", null);
        changed.Clear();
        model.End();
        CollectionAssert.Contains(changed, nameof(model.IsIndeterminate));
        CollectionAssert.Contains(changed, nameof(model.StageProgressText));
    }

    [TestMethod]
    public void CanonicalPropertiesMatchAliasesAndOperationProgressUpdatesState()
    {
        var model = new ProgressViewModel();
        Assert.AreEqual(0, model.Percent);
        Assert.AreEqual(model.Percent, model.StagePercent);
        Assert.IsFalse(model.IsIndeterminate);
        Assert.AreEqual("", model.ProgressText);
        Assert.AreEqual(model.ProgressText, model.StageProgressText);
        Assert.IsFalse(model.IsRunning);

        model.Start("Starting");
        Assert.IsTrue(model.IsRunning);
        Assert.IsTrue(model.IsIndeterminate);
        Assert.AreEqual("Working…", model.ProgressText);

        var progress = new DoViFixer.Application.Operations.OperationProgress(Guid.NewGuid(), "Injecting", "test.mkv", 60.5);
        model.Update(progress);

        Assert.AreEqual("Injecting", model.Stage);
        Assert.AreEqual(60.5, model.Percent);
        Assert.AreEqual(60.5, model.StagePercent);
        Assert.IsFalse(model.IsIndeterminate);
        Assert.AreEqual("61%", model.ProgressText);
        Assert.AreEqual("61%", model.StageProgressText);

        model.Reset();
        Assert.IsFalse(model.IsRunning);
        Assert.AreEqual("", model.Stage);
        Assert.AreEqual(0, model.Percent);
        Assert.IsFalse(model.IsIndeterminate);
        Assert.AreEqual("", model.ProgressText);
    }

    [TestMethod]
    public void ConversionStepsFollowTheReportedStagesAndKeepTheirStateWhileCancelling()
    {
        var model = new ProgressViewModel();
        model.Start("Preparing conversion");
        model.Describe(ConversionJob.Details(Plan(archive: true)), ConversionJob.Steps(Plan(archive: true)));
        CollectionAssert.AreEqual(new[] { "Back up EL", "Convert", "Remux", "Verify" }, model.Steps.Select(step => step.Name).ToArray());
        Assert.IsTrue(model.Steps.Take(3).All(step => !step.IsLast));
        Assert.IsTrue(model.Steps[^1].IsLast);
        Assert.IsTrue(model.Steps.All(step => step.State == JobStepState.Pending), "Planning comes before the first step.");

        model.Update("Backing up enhancement layer", 40);
        AssertStates(model, JobStepState.Active, JobStepState.Pending, JobStepState.Pending, JobStepState.Pending);

        model.Update("Streaming conversion", 10);
        AssertStates(model, JobStepState.Completed, JobStepState.Active, JobStepState.Pending, JobStepState.Pending);

        model.Update("Verifying", 72);
        AssertStates(model, JobStepState.Completed, JobStepState.Completed, JobStepState.Completed, JobStepState.Active);
        Assert.AreEqual("In progress", model.Steps[3].StateText);
        Assert.AreEqual("Completed", model.Steps[0].StateText);

        // Verification failure retries the conversion in safe mode.
        model.Update("Retrying with safe disk extraction", null);
        AssertStates(model, JobStepState.Completed, JobStepState.Active, JobStepState.Pending, JobStepState.Pending);
        model.Update("Extracting video", 5);
        AssertStates(model, JobStepState.Completed, JobStepState.Active, JobStepState.Pending, JobStepState.Pending);

        model.Update("Cancelling…", null);
        AssertStates(model, JobStepState.Completed, JobStepState.Active, JobStepState.Pending, JobStepState.Pending);

        model.Start("Scanning");
        Assert.IsFalse(model.HasSteps);
        Assert.AreEqual(0, model.Details.Count);
    }

    [TestMethod]
    public void ConversionDetailsNameTheTargetTheSourceLayerAndTheOriginal()
    {
        CollectionAssert.AreEqual(
            new[] { "Converting to Profile 8.1 (Simple FEL)", "Original: Keep" },
            ConversionJob.Details(Plan()).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Converting to HDR10 (MEL)", "Original: Replace · EL archive" },
            ConversionJob.Details(Plan(ConversionTarget.Hdr10, AnalysisVerdict.Mel, archive: true, replace: true)).ToArray());
        CollectionAssert.AreEqual(new[] { "Convert", "Remux", "Verify" }, ConversionJob.Steps(Plan()).Select(step => step.Name).ToArray());
    }

    private static void AssertStates(ProgressViewModel model, params JobStepState[] expected) =>
        CollectionAssert.AreEqual(expected, model.Steps.Select(step => step.State).ToArray());

    private static ConversionPlan Plan(ConversionTarget target = ConversionTarget.Profile81, AnalysisVerdict verdict = AnalysisVerdict.SimpleFel, bool archive = false, bool replace = false)
    {
        var media = new MediaInfo(new FileIdentity(@"C:\Media\Ocean.mkv", 1, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 3840, 2160, 1000, 23.976, 5772, 0, 1000, [], 0, 1, null, "{}");
        var evidence = new RpuEvidence(AnalysisMethod.SampledRpu, EnhancementLayer.Fel, 1000, 1000, 10, 10);
        return new ConversionPlan(Guid.NewGuid(), new MediaAnalysis(media, evidence, verdict, "Test"), target, @"C:\Media\Ocean - DV P8.1.mkv", archive ? @"C:\Media\Ocean.dovi" : null, null, 0, "Test", DeleteBackup: replace);
    }
}
