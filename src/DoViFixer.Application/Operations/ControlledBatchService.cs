using System.Diagnostics;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Activity;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Operations;
/// <summary>
/// Runs selected items sequentially; per-item cancellation awaits recovery before continuing. Each file that starts
/// gets one activity entry, and a batch of several files gets a summary entry. The result carries the entry that sums
/// the batch up, so the status text and notification read the same as the activity.
/// </summary>
public sealed class ControlledBatchService(ILogger<ControlledBatchService> logger, IActivityFeed activity)
{
    public async Task<BatchResult> ExecuteAsync<T>(
        ActivityOperation operation,
        IReadOnlyList<T> items,
        Func<T, string> itemKey,
        Func<T, CancellationToken, Task<OperationItemResult>> execute,
        BatchControl control,
        IProgress<OperationItemResult>? completed,
        CancellationToken cancellationToken)
    {
        var results = new List<OperationItemResult>();
        using var summary = new BatchSummary(activity, operation, items.Count, () => results);
        foreach (var item in items)
        {
            var (result, entry) = await RunItemAsync(operation, item, itemKey(item), execute, control, cancellationToken);
            results.Add(result);
            summary.Item = entry;
            completed?.Report(result);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new(results.AsReadOnly()) { Activity = summary.Finish() };
    }

    /// <summary>Processes every selected item through a FIFO queue with at most <paramref name="workers"/> active jobs.</summary>
    public async Task<BatchResult> ExecuteQueuedAsync<T>(
        ActivityOperation operation,
        IReadOnlyList<T> items,
        int workers,
        Func<T, string> itemKey,
        Func<T, CancellationToken, Task<OperationItemResult>> execute,
        BatchControl control,
        IProgress<OperationItemResult>? completed,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        if (workers == 1)
        {
            return await ExecuteAsync(operation, items, itemKey, execute, control, completed, cancellationToken);
        }

        var results = new OperationItemResult?[items.Count];
        int nextIndex = -1;
        using var summary = new BatchSummary(activity, operation, items.Count, () => results.OfType<OperationItemResult>().ToArray());

        async Task WorkAsync()
        {
            while (true)
            {
                int index = Interlocked.Increment(ref nextIndex);
                if (index >= items.Count)
                {
                    return;
                }

                T item = items[index];
                var (result, entry) = await RunItemAsync(operation, item, itemKey(item), execute, control, cancellationToken);
                results[index] = result;
                // Only a one-file batch uses its file's entry, and it has a single worker.
                summary.Item = entry;
                completed?.Report(result);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(workers, items.Count)).Select(_ => WorkAsync()));
        return new(results.Select(result => result!).ToArray()) { Activity = summary.Finish() };
    }

    /// <summary>Runs one item and words its outcome. The entry is reported only when the item started.</summary>
    private async Task<(OperationItemResult Result, ActivityEntry Entry)> RunItemAsync<T>(
        ActivityOperation operation,
        T item,
        string key,
        Func<T, CancellationToken, Task<OperationItemResult>> execute,
        BatchControl control,
        CancellationToken cancellationToken)
    {
        // Files that never start, because they were skipped or the batch was cancelled first, appear only in the summary.
        var entryId = Guid.NewGuid();
        long started = Stopwatch.GetTimestamp();
        bool reported = false;
        OperationItemResult result;
        try
        {
            var token = await control.StartAsync(key, cancellationToken);
            if (token is null)
            {
                result = new(key, OperationStatus.Skipped, null, "Skipped before starting.");
            }
            else
            {
                started = Stopwatch.GetTimestamp();
                reported = true;
                activity.Report(ActivityText.Started(entryId, operation, key));
                result = await execute(item, token.Value);
            }
        }
        catch (OperationCanceledException)
        {
            result = new(key, OperationStatus.Cancelled, null, "Cancelled; operation cleanup has finished.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch item failed: {Item}", key);
            result = new(key, OperationStatus.Failed, null, ex.Message) { Failure = Diagnostics.FailureDiagnosis.FromException(ex) };
        }
        finally
        {
            control.Finish(key);
        }

        var entry = ActivityText.Finished(entryId, operation, result, Stopwatch.GetElapsedTime(started));
        if (reported)
        {
            activity.Report(entry);
        }

        OperationLog.Result(logger, result);
        return (result, entry);
    }

    /// <summary>
    /// The summary entry of a batch of several files: running while the batch runs, then its outcome. A batch that
    /// ends with an exception still reports its outcome.
    /// </summary>
    private sealed class BatchSummary : IDisposable
    {
        private readonly IActivityFeed activity;
        private readonly ActivityOperation operation;
        private readonly int count;
        private readonly Func<IReadOnlyCollection<OperationItemResult>> results;
        private readonly Guid id = Guid.NewGuid();
        private readonly long started = Stopwatch.GetTimestamp();
        private bool finished;

        public BatchSummary(IActivityFeed activity, ActivityOperation operation, int count, Func<IReadOnlyCollection<OperationItemResult>> results)
        {
            this.activity = activity;
            this.operation = operation;
            this.count = count;
            this.results = results;
            if (count > 1)
            {
                activity.Report(ActivityText.BatchStarted(id, operation, count));
            }
        }

        /// <summary>The entry of the last item that finished.</summary>
        public ActivityEntry? Item { get; set; }

        /// <summary>Reports the summary of several files and returns the entry that sums the batch up.</summary>
        public ActivityEntry Finish()
        {
            finished = true;
            var summary = ActivityText.BatchFinished(id, operation, count, results(), Stopwatch.GetElapsedTime(started));
            if (count > 1)
            {
                activity.Report(summary);
                return summary;
            }

            return Item ?? summary;
        }

        public void Dispose()
        {
            if (!finished && count > 1)
            {
                Finish();
            }
        }
    }
}
