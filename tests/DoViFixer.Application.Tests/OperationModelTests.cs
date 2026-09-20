using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;

[TestClass]
public sealed class OperationModelTests
{
    [TestMethod]
    public void OperationProgressSupportsItemAndDefaults()
    {
        var id = Guid.NewGuid();
        var progressWithItem = new OperationProgress(id, "Downloading", "dovi_tool", 45.5);
        Assert.AreEqual(id, progressWithItem.OperationId);
        Assert.AreEqual("Downloading", progressWithItem.Stage);
        Assert.AreEqual("dovi_tool", progressWithItem.Item);
        Assert.AreEqual(45.5, progressWithItem.Percent);

        var progressWithoutItem = new OperationProgress(id, "Initializing");
        Assert.IsNull(progressWithoutItem.Item);
        Assert.IsNull(progressWithoutItem.Percent);
    }

    [TestMethod]
    public void OperationItemResultImplementsInterface()
    {
        var result = new OperationItemResult("item-1", OperationStatus.Completed, "out-1", "Done");
        Assert.AreEqual("item-1", result.Item);
        Assert.AreEqual(OperationStatus.Completed, result.Status);
        Assert.AreEqual("out-1", result.Output);
        Assert.AreEqual("Done", result.Message);

        IOperationItemResult itemInterface = result;
        Assert.AreEqual("item-1", itemInterface.Item);
        Assert.AreEqual("out-1", itemInterface.Output);

        IOperationResult resultInterface = result;
        Assert.AreEqual(OperationStatus.Completed, resultInterface.Status);
        Assert.AreEqual("Done", resultInterface.Message);
    }

    [TestMethod]
    public void OperationItemResultDeconstructsAndPropertiesMatch()
    {
        var result = new OperationItemResult("input.mkv", OperationStatus.Completed, "output.mkv", "Success");
        var (item, status, output, message) = result;
        Assert.AreEqual("input.mkv", item);
        Assert.AreEqual(OperationStatus.Completed, status);
        Assert.AreEqual("output.mkv", output);
        Assert.AreEqual("Success", message);
    }

    [TestMethod]
    public void InstallationOutcomeImplementsIOperationResult()
    {
        var success = new InstallationOutcome("ffmpeg", true, "Installed");
        Assert.IsInstanceOfType<IOperationResult>(success);
        Assert.AreEqual(OperationStatus.Completed, success.Status);
        Assert.AreEqual("Installed", success.Message);

        var failure = new InstallationOutcome("ffmpeg", false, "Failed");
        Assert.AreEqual(OperationStatus.Failed, failure.Status);
    }

    [TestMethod]
    public void OperationStatusAggregatorAggregatesCorrectly()
    {
        Assert.AreEqual(OperationStatus.Skipped, OperationStatusAggregator.Aggregate([]));

        Assert.AreEqual(OperationStatus.Cancelled, OperationStatusAggregator.Aggregate([
            OperationStatus.Completed,
            OperationStatus.Cancelled,
            OperationStatus.Failed
        ]));

        Assert.AreEqual(OperationStatus.Completed, OperationStatusAggregator.Aggregate([
            OperationStatus.Completed,
            OperationStatus.Completed
        ]));

        Assert.AreEqual(OperationStatus.Partial, OperationStatusAggregator.Aggregate([
            OperationStatus.Completed,
            OperationStatus.Failed
        ]));

        Assert.AreEqual(OperationStatus.Partial, OperationStatusAggregator.Aggregate([
            OperationStatus.Partial,
            OperationStatus.Failed
        ]));

        Assert.AreEqual(OperationStatus.Failed, OperationStatusAggregator.Aggregate([
            OperationStatus.Failed,
            OperationStatus.Failed
        ]));
    }

    [TestMethod]
    public void BatchResultGenericSupportsCustomOperationResults()
    {
        var outcomes = new InstallationOutcome[]
        {
            new("ffmpeg", true, "Installed"),
            new("dovi_tool", true, "Installed")
        };

        var batch = new BatchResult<InstallationOutcome>(outcomes);
        Assert.AreEqual(OperationStatus.Completed, batch.Status);
        Assert.AreEqual(2, batch.Items.Count);

        var partialOutcomes = new InstallationOutcome[]
        {
            new("ffmpeg", true, "Installed"),
            new("dovi_tool", false, "Checksum failed")
        };
        var partialBatch = new BatchResult<InstallationOutcome>(partialOutcomes);
        Assert.AreEqual(OperationStatus.Partial, partialBatch.Status);
    }

    [TestMethod]
    public void BatchResultNonGenericSupportsOperationItemResult()
    {
        var item1 = new OperationItemResult("a.mkv", OperationStatus.Completed, "out_a.mkv", "Done");
        var item2 = new OperationItemResult("b.mkv", OperationStatus.Failed, null, "Failed");

        var batch = new BatchResult([item1, item2]);
        Assert.AreEqual(OperationStatus.Partial, batch.Status);
        Assert.AreEqual(2, batch.Items.Count);
        Assert.AreSame(item1, batch.Items[0]);
        Assert.AreSame(item2, batch.Items[1]);
    }
}

