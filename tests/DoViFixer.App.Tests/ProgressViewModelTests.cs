using DoViFixer.App.ViewModels;
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
}
