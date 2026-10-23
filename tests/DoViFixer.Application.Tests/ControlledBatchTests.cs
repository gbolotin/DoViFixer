using DoViFixer.Application.Activity;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Channels;

namespace DoViFixer.Application.Tests;
[TestClass]
public sealed class ControlledBatchTests
{
    [TestMethod]
    public async Task QueuedBatchRunsAtMostTwoJobsAndDrainsAllFiveFiles()
    {
        string[] files = ["a", "b", "c", "d", "e"];
        using var control = new BatchControl(files);
        var started = Channel.CreateUnbounded<string>();
        var releases = files.ToDictionary(file => file, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        int active = 0;
        int peak = 0;
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, new ActivityFeed(TimeProvider.System));
        var run = service.ExecuteQueuedAsync(ActivityOperation.Conversion, files, 2, file => file, async (file, token) =>
        {
            int count = Interlocked.Increment(ref active);
            peak = Math.Max(peak, count);
            started.Writer.TryWrite(file);
            await releases[file].Task.WaitAsync(token);
            Interlocked.Decrement(ref active);
            return new OperationItemResult(file, OperationStatus.Completed, file + ".out", "Done");
        }, control, null, CancellationToken.None);

        Assert.AreEqual("a", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("b", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsFalse(started.Reader.TryRead(out _));
        releases["a"].SetResult();
        Assert.AreEqual("c", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        releases["b"].SetResult();
        releases["c"].SetResult();
        Assert.AreEqual("d", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("e", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        releases["d"].SetResult();
        releases["e"].SetResult();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, peak);
        CollectionAssert.AreEqual(files, result.Items.Select(item => item.Item).ToArray());
        Assert.IsTrue(result.Items.All(item => item.Status == OperationStatus.Completed));
    }

    [TestMethod]
    public async Task QueuedBatchPauseAndPerFileCancellationKeepOtherJobsRunning()
    {
        string[] files = ["a", "b", "c"];
        using var control = new BatchControl(files);
        var started = Channel.CreateUnbounded<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, new ActivityFeed(TimeProvider.System));
        var run = service.ExecuteQueuedAsync(ActivityOperation.Conversion, files, 2, file => file, async (file, token) =>
        {
            started.Writer.TryWrite(file);
            await release.Task.WaitAsync(token);
            return new OperationItemResult(file, OperationStatus.Completed, null, "Done");
        }, control, null, CancellationToken.None);

        Assert.AreEqual("a", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("b", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        control.Pause();
        Assert.IsTrue(control.Cancel("a"));
        await Task.Delay(50);
        Assert.IsFalse(started.Reader.TryRead(out _));
        control.Resume();
        Assert.AreEqual("c", await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        release.SetResult();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[] { OperationStatus.Cancelled, OperationStatus.Completed, OperationStatus.Completed }, result.Items.Select(item => item.Status).ToArray());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PauseFinishesCurrentJobAndWaitsForResumeOrBatchCancellation(bool cancelBatch)
    {
        using var cancellation = new CancellationTokenSource();
        using var control = new BatchControl(["a", "b", "c"]);
        var visited = new List<string>();
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, new ActivityFeed(TimeProvider.System));
        var run = service.ExecuteAsync(ActivityOperation.Conversion, new[] { "a", "b", "c" }, x => x, (path, token) =>
        {
            visited.Add(path);
            if (path == "a")
            {
                control.Pause();
                control.Pause();
                Assert.IsFalse(token.IsCancellationRequested);
            }

            return Task.FromResult(new OperationItemResult(path, OperationStatus.Completed, null, "Done"));
        }, control, null, cancellation.Token);

        Assert.IsTrue(control.IsPaused);
        Assert.IsFalse(run.IsCompleted);
        CollectionAssert.AreEqual(new[] { "a" }, visited);
        Assert.IsTrue(control.Skip("b"));
        if (cancelBatch)
        {
            cancellation.Cancel();
        }
        else
        {
            control.Resume();
            Assert.IsFalse(control.IsPaused);
        }

        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(OperationStatus.Completed, result.Items[0].Status);
        CollectionAssert.AreEqual(cancelBatch ? new[] { "a" } : new[] { "a", "c" }, visited);
        Assert.AreEqual(cancelBatch ? OperationStatus.Cancelled : OperationStatus.Skipped, result.Items[1].Status);
    }

    [TestMethod]
    public async Task CancelCurrentWaitsForRecoveryThenContinuesAndSkipNeverStarts()
    {
        using var control = new BatchControl(["a", "b", "c"]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var visited = new List<string>();
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, new ActivityFeed(TimeProvider.System));
        var run = service.ExecuteAsync(ActivityOperation.Conversion, new[]
        {
            "a", "b", "c"
        }, x => x, async (path, token) =>
        {
            visited.Add(path);
            if (path == "a")
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                finally
                {
                    cancellationObserved.SetResult();
                    await recovery.Task;
                }
            }

            return new(path, OperationStatus.Completed, path + ".out", "Verified");
        }, control, null, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(control.Skip("a"));
        Assert.IsTrue(control.Skip("b"));
        Assert.IsTrue(control.Cancel("a"));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.HasCount(1, visited);
        recovery.SetResult();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new[]
        {
            "a", "c"
        }, visited);
        CollectionAssert.AreEqual(new[]
        {
            OperationStatus.Cancelled, OperationStatus.Skipped, OperationStatus.Completed
        }, result.Items.Select(f => f.Status).ToArray());
        Assert.IsFalse(control.Cancel("a"));
    }

    [TestMethod]
    public async Task BatchCancellationStopsFollowingItemsAndFailuresContinue()
    {
        using var cancellation = new CancellationTokenSource();
        using var control = new BatchControl(["a", "b", "c"]);
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance, new ActivityFeed(TimeProvider.System));
        var result = await service.ExecuteAsync(ActivityOperation.Conversion, new[]
        {
            "a", "b", "c"
        }, x => x, (path, _) =>
        {
            if (path == "a")
            {
                throw new IOException("broken");
            }

            cancellation.Cancel();
            return Task.FromResult(new OperationItemResult(path, OperationStatus.Cancelled, null, "Cancelled"));
        }, control, null, cancellation.Token);
        Assert.HasCount(2, result.Items);
        Assert.AreEqual(OperationStatus.Failed, result.Items[0].Status);
        Assert.AreEqual(OperationStatus.Cancelled, result.Items[1].Status);
    }
}
