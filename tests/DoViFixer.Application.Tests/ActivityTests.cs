using DoViFixer.Application.Activity;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;

/// <summary>The activity feed: what people see of their operations, worded for them rather than for diagnosis.</summary>
[TestClass]
public sealed class ActivityTests
{
    private readonly ActivityFeed feed = new(TimeProvider.System);

    [TestMethod]
    public void AnOperationKeepsOneEntryAndTheOldestEntriesGoFirst()
    {
        var id = Guid.NewGuid();
        feed.Report(ActivityText.Started(id, ActivityOperation.Conversion, @"C:\Media\Mountain.mkv"));
        feed.Report(ActivityText.Finished(id, ActivityOperation.Conversion, new(@"C:\Media\Mountain.mkv", OperationStatus.Completed, @"C:\Out\Mountain.mkv", "Original retained."), TimeSpan.FromMinutes(3)));

        var entry = feed.Entries.Single();
        Assert.AreEqual("Converted Mountain.mkv in 3 min", entry.Title);
        Assert.AreEqual(ActivitySeverity.Success, entry.Severity);
        Assert.IsFalse(entry.IsRunning);
        Assert.AreEqual(@"C:\Out\Mountain.mkv", entry.Location);
        Assert.AreNotEqual(default, entry.Time);

        for (int i = 0; i < ActivityFeed.Capacity; i++)
        {
            feed.Report(new(Guid.NewGuid(), ActivitySeverity.Info, $"Entry {i}"));
        }

        Assert.AreEqual(ActivityFeed.Capacity, feed.Entries.Count);
        Assert.AreEqual("Entry 0", feed.Entries[0].Title);
    }

    [TestMethod]
    public async Task ABatchReportsEachFileItStartedAndASummary()
    {
        string[] files = [@"C:\Media\a.mkv", @"C:\Media\b.mkv", @"C:\Media\c.mkv"];
        using var control = new BatchControl(files);
        Assert.IsTrue(control.Skip(files[2]));
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, feed);

        var result = await service.ExecuteAsync(ActivityOperation.Scan, files, file => file, (file, _) => file == files[1]
            ? throw new IOException("The drive is full.")
            : Task.FromResult(new OperationItemResult(file, OperationStatus.Completed, null, "MEL.")), control, null, CancellationToken.None);

        var entries = feed.Entries;
        Assert.AreEqual(3, entries.Count, "The skipped file never started, so only the batch summary counts it.");
        Assert.AreEqual("Scanned 1 of 3 files", entries[2].Title, "The summary finished last, so it is the newest entry.");
        Assert.AreEqual(ActivitySeverity.Warning, entries[2].Severity);
        Assert.AreEqual("1 failed · 1 skipped · took less than a second", entries[2].Detail);
        Assert.AreEqual("Scanned a.mkv in less than a second", entries[0].Title);
        Assert.AreEqual("MEL.", entries[0].Detail);
        Assert.AreEqual(files[0], entries[0].FilePath);
        Assert.AreEqual("Couldn't scan b.mkv", entries[1].Title);
        Assert.AreEqual(ActivitySeverity.Error, entries[1].Severity);
        Assert.AreEqual("The drive is full.", entries[1].Detail);
        Assert.IsFalse(entries.Any(entry => entry.IsRunning));
        Assert.AreEqual(entries[2].Text, result.Activity?.Text, "The result carries the summary, so the status text reads the same.");
    }

    [TestMethod]
    public async Task AOneFileBatchIsSummedUpByItsFile()
    {
        string[] files = [@"C:\Media\a.mkv"];
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, feed);
        using (var control = new BatchControl(files))
        {
            var result = await service.ExecuteAsync(ActivityOperation.Conversion, files, file => file,
                (file, _) => throw new IOException("mkvmerge stopped."), control, null, CancellationToken.None);
            Assert.AreEqual("Couldn't convert a.mkv · mkvmerge stopped.", result.Activity?.Text);
        }

        using (var control = new BatchControl(files))
        {
            control.Skip(files[0]);
            var result = await service.ExecuteAsync(ActivityOperation.Conversion, files, file => file,
                (file, _) => Task.FromResult(new OperationItemResult(file, OperationStatus.Completed)), control, null, CancellationToken.None);
            Assert.AreEqual("Skipped a.mkv · Skipped before starting.", result.Activity?.Text);
            Assert.AreEqual(1, feed.Entries.Count, "A file that never started is not reported.");
        }
    }

    [TestMethod]
    public void TextFlattensTheDetailOntoOneLine() =>
        Assert.AreEqual("Couldn't remove a.dovi · Access denied. Check permissions.",
            new ActivityEntry(Guid.NewGuid(), ActivitySeverity.Error, "Couldn't remove a.dovi", $"Access denied.{Environment.NewLine}Check permissions.").Text);

    [TestMethod]
    public async Task AOneFileBatchHasNoSummary()
    {
        string[] files = [@"C:\Media\a.mkv"];
        using var control = new BatchControl(files);
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, feed);

        await service.ExecuteQueuedAsync(ActivityOperation.Restoration, files, 2, file => file,
            (file, _) => Task.FromResult(new OperationItemResult(file, OperationStatus.Completed, @"C:\Media\a.p7.mkv")), control, null, CancellationToken.None);

        Assert.AreEqual("Restored a.mkv in less than a second", feed.Entries.Single().Title);
    }

    [TestMethod]
    public void FailuresSayWhereTheOriginalIs()
    {
        var failed = new OperationItemResult(@"C:\Media\Mountain.mkv", OperationStatus.Failed, null, "mkvmerge stopped with an error. Original restored.")
        {
            Original = @"C:\Media\Mountain.mkv"
        };

        var entry = ActivityText.Finished(Guid.NewGuid(), ActivityOperation.Conversion, failed, TimeSpan.FromSeconds(42));

        Assert.AreEqual("Couldn't convert Mountain.mkv", entry.Title);
        Assert.AreEqual("mkvmerge stopped with an error. Original restored.", entry.Detail);
        Assert.AreEqual(@"C:\Media\Mountain.mkv", entry.Location);
    }

    [TestMethod]
    public void BatchSummariesCountWhatHappened()
    {
        OperationItemResult Result(OperationStatus status) => new("x", status);

        var allDone = ActivityText.BatchFinished(Guid.NewGuid(), ActivityOperation.Conversion, 2, [Result(OperationStatus.Completed), Result(OperationStatus.Completed)], TimeSpan.FromMinutes(72));
        Assert.AreEqual("Converted 2 files", allDone.Title);
        Assert.AreEqual("took 1 h 12 min", allDone.Detail);
        Assert.AreEqual(ActivitySeverity.Success, allDone.Severity);

        var allFailed = ActivityText.BatchFinished(Guid.NewGuid(), ActivityOperation.Cleanup, 2, [Result(OperationStatus.Failed), Result(OperationStatus.Failed)], TimeSpan.FromSeconds(3));
        Assert.AreEqual("Couldn't remove 2 backup files", allFailed.Title);
        Assert.AreEqual(ActivitySeverity.Error, allFailed.Severity);

        var cancelled = ActivityText.BatchFinished(Guid.NewGuid(), ActivityOperation.Inspection, 4, [Result(OperationStatus.Completed), Result(OperationStatus.Cancelled)], TimeSpan.FromSeconds(65));
        Assert.AreEqual("Inspected 1 of 4 files", cancelled.Title);
        Assert.AreEqual("1 cancelled · 2 not started · took 1 min 5 s", cancelled.Detail);
        Assert.AreEqual(ActivitySeverity.Info, cancelled.Severity);
    }

    [TestMethod]
    [DataRow(0.4, "less than a second")]
    [DataRow(42.9, "42 s")]
    [DataRow(120, "2 min")]
    [DataRow(185, "3 min 5 s")]
    [DataRow(3600, "1 h")]
    [DataRow(4320, "1 h 12 min")]
    public void DurationsReadNaturally(double seconds, string expected) =>
        Assert.AreEqual(expected, ActivityText.Duration(TimeSpan.FromSeconds(seconds)));
}
