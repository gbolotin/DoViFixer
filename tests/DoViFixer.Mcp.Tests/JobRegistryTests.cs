using DoViFixer.Application.Operations;
using DoViFixer.Mcp.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Mcp.Tests;
[TestClass]
public sealed class JobRegistryTests
{
    [TestMethod]
    public async Task QuickWorkReturnsItsResultInTheSameCall()
    {
        await using var jobs = Create(TimeSpan.FromSeconds(10));
        var job = await jobs.StartAsync("Inspect", "movie.mkv", (_, _) => Task.FromResult<object>("analysis"), CancellationToken.None);
        Assert.AreEqual(JobState.Completed, job.State);
        Assert.AreEqual("analysis", job.Result);
        Assert.IsNotNull(job.FinishedAt);
    }

    [TestMethod]
    public async Task SlowWorkReturnsARunningJobWithItsLatestProgress()
    {
        await using var jobs = Create(TimeSpan.FromMilliseconds(200));
        var release = new TaskCompletionSource();
        var job = await jobs.StartAsync("Convert", "movie.mkv", async (progress, token) =>
        {
            progress.Report(new OperationProgress(Guid.NewGuid(), "Extracting RPU", "movie.mkv", 40));
            await release.Task.WaitAsync(token);
            return "done";
        }, CancellationToken.None);
        Assert.AreEqual(JobState.Running, job.State);
        Assert.IsNull(job.Result);
        var running = jobs.Get(job.JobId)!;
        Assert.AreEqual("Extracting RPU", running.Stage);
        Assert.AreEqual(40, running.Percent);
        release.SetResult();
        Assert.AreEqual("done", (await WaitForEndAsync(jobs, job.JobId)).Result);
    }

    [TestMethod]
    public async Task JobsRunOneAtATime()
    {
        await using var jobs = Create(TimeSpan.FromMilliseconds(100));
        var release = new TaskCompletionSource();
        var first = await jobs.StartAsync("Convert", "a.mkv", async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return "a";
        }, CancellationToken.None);
        var second = await jobs.StartAsync("Scan", "b", (_, _) => Task.FromResult<object>("b"), CancellationToken.None);
        Assert.AreEqual(JobState.Queued, second.State);
        release.SetResult();
        Assert.AreEqual(JobState.Completed, (await WaitForEndAsync(jobs, first.JobId)).State);
        Assert.AreEqual("b", (await WaitForEndAsync(jobs, second.JobId)).Result);
    }

    [TestMethod]
    public async Task CancellingStopsTheJob()
    {
        await using var jobs = Create(TimeSpan.FromMilliseconds(100));
        var job = await jobs.StartAsync("Convert", "movie.mkv", async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return "never";
        }, CancellationToken.None);
        Assert.IsTrue(jobs.Cancel(job.JobId));
        var cancelled = await WaitForEndAsync(jobs, job.JobId);
        Assert.AreEqual(JobState.Cancelled, cancelled.State);
        Assert.IsFalse(jobs.Cancel(job.JobId));
    }

    [TestMethod]
    public async Task FailuresKeepTheirMessage()
    {
        await using var jobs = Create(TimeSpan.FromSeconds(10));
        var job = await jobs.StartAsync("Inspect", "movie.mkv", (_, _) => Task.FromException<object>(new DependencyNotReadyException("DoviTool: Missing")), CancellationToken.None);
        Assert.AreEqual(JobState.Failed, job.State);
        Assert.AreEqual("DoviTool: Missing", job.Error);
    }

    [TestMethod]
    public async Task ListingLeavesOutResults()
    {
        await using var jobs = Create(TimeSpan.FromSeconds(10));
        await jobs.StartAsync("Inspect", "movie.mkv", (_, _) => Task.FromResult<object>("analysis"), CancellationToken.None);
        var listed = jobs.List().Single();
        Assert.AreEqual("Inspect", listed.Operation);
        Assert.IsNull(listed.Result);
        Assert.IsNull(jobs.Get(Guid.NewGuid()));
    }

    private static JobRegistry Create(TimeSpan inlineWait) => new(NullLogger<JobRegistry>.Instance, TimeProvider.System, inlineWait);

    private static async Task<JobSnapshot> WaitForEndAsync(JobRegistry jobs, Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var job = jobs.Get(id)!;
            if (job.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
            {
                return job;
            }

            await Task.Delay(20, timeout.Token);
        }
    }
}
