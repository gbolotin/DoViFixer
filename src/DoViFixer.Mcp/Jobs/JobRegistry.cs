using System.Collections.Concurrent;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Mcp.Jobs;
public enum JobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record JobSnapshot(Guid JobId, string Operation, string? Input, JobState State, string? Stage, string? Item, double? Percent, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, object? Result, string? Error);

/// <summary>
/// Runs media operations in the background so a long conversion outlives the tool call that started it.
/// Jobs run one at a time, as in the app, so two conversions never compete for the same disks.
/// </summary>
public sealed class JobRegistry(ILogger<JobRegistry> logger, TimeProvider time, TimeSpan inlineWait) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, Job> jobs = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stopping = new();

    /// <summary>Starts a job and waits briefly for it, so quick work returns its result in the same call.</summary>
    public async Task<JobSnapshot> StartAsync(string operation, string? input, Func<IProgress<OperationProgress>, CancellationToken, Task<object>> work, CancellationToken cancellationToken)
    {
        var job = new Job(operation, input, time.GetUtcNow(), CancellationTokenSource.CreateLinkedTokenSource(stopping.Token));
        jobs[job.Id] = job;
        job.Completion = Task.Run(() => RunAsync(job, work), CancellationToken.None);
        try
        {
            await job.Completion.WaitAsync(inlineWait, time, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Still running; the caller polls get_job.
        }

        return job.Snapshot(includeResult: true);
    }

    public JobSnapshot? Get(Guid id) => jobs.TryGetValue(id, out var job) ? job.Snapshot(includeResult: true) : null;
    public IReadOnlyList<JobSnapshot> List() => [.. jobs.Values.Select(job => job.Snapshot(includeResult: false)).OrderBy(job => job.StartedAt)];

    public bool Cancel(Guid id)
    {
        if (!jobs.TryGetValue(id, out var job) || job.IsFinished)
        {
            return false;
        }

        job.Cancellation.Cancel();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        // Cancel running work and let it delete its temporary files before the process exits.
        await stopping.CancelAsync();
        try
        {
            await Task.WhenAll(jobs.Values.Select(job => job.Completion)).WaitAsync(TimeSpan.FromSeconds(30), time);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Jobs were still stopping when the server exited");
        }

        foreach (var job in jobs.Values)
        {
            job.Cancellation.Dispose();
        }

        stopping.Dispose();
        gate.Dispose();
    }

    private async Task RunAsync(Job job, Func<IProgress<OperationProgress>, CancellationToken, Task<object>> work)
    {
        var cancellationToken = job.Cancellation.Token;
        try
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                job.Start();
                job.Finish(JobState.Completed, await work(job, cancellationToken), null, time.GetUtcNow());
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            job.Finish(JobState.Cancelled, null, "Cancelled. Original media retained.", time.GetUtcNow());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} {Operation} failed", job.Id, job.Operation);
            job.Finish(JobState.Failed, null, ex.Message, time.GetUtcNow());
        }
    }

    private sealed class Job(string operation, string? input, DateTimeOffset startedAt, CancellationTokenSource cancellation) : IProgress<OperationProgress>
    {
        private readonly Lock gate = new();
        private JobState state = JobState.Queued;
        private OperationProgress? progress;
        private object? result;
        private string? error;
        private DateTimeOffset? finishedAt;

        public Guid Id { get; } = Guid.NewGuid();
        public string Operation => operation;
        public CancellationTokenSource Cancellation => cancellation;
        public Task Completion { get; set; } = Task.CompletedTask;
        public bool IsFinished
        {
            get
            {
                lock (gate)
                {
                    return finishedAt is not null;
                }
            }
        }

        public void Report(OperationProgress value)
        {
            lock (gate)
            {
                progress = value;
            }
        }

        public void Start()
        {
            lock (gate)
            {
                state = JobState.Running;
            }
        }

        public void Finish(JobState finalState, object? value, string? message, DateTimeOffset at)
        {
            lock (gate)
            {
                state = finalState;
                result = value;
                error = message;
                finishedAt = at;
            }
        }

        public JobSnapshot Snapshot(bool includeResult)
        {
            lock (gate)
            {
                return new(Id, operation, input, state, progress?.Stage, progress?.Item, progress?.Percent, startedAt, finishedAt, includeResult ? result : null, error);
            }
        }
    }
}
