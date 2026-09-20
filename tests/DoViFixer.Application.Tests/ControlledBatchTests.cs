using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;
[TestClass]
public sealed class ControlledBatchTests
{
    [TestMethod]
    public async Task CancelCurrentWaitsForRecoveryThenContinuesAndSkipNeverStarts()
    {
        using var control = new BatchControl(["a", "b", "c"]);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var visited = new List<string>();
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance);
        var run = service.ExecuteAsync(new[]
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
        var service = new ControlledBatchService(NullLogger<ControlledBatchService>.Instance);
        var result = await service.ExecuteAsync(new[]
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
