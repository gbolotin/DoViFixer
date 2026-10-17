using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Operations;
/// <summary>Runs selected items sequentially; per-item cancellation awaits recovery before continuing.</summary>
public sealed class ControlledBatchService(ILogger<ControlledBatchService> logger)
{
    public async Task<BatchResult> ExecuteAsync<T>(
        IReadOnlyList<T> items,
        Func<T, string> itemKey,
        Func<T, CancellationToken, Task<OperationItemResult>> execute,
        BatchControl control,
        IProgress<OperationItemResult>? completed,
        CancellationToken cancellationToken)
    {
        var results = new List<OperationItemResult>();
        foreach (var item in items)
        {
            string key = itemKey(item);
            OperationItemResult result;
            try
            {
                var token = await control.StartAsync(key, cancellationToken);
                result = token is null
                    ? new(key, OperationStatus.Skipped, null, "Deselected before starting.")
                    : await execute(item, token.Value);
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

            results.Add(result);
            OperationLog.Result(logger, result);
            completed?.Report(result);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new(results.AsReadOnly());
    }

    /// <summary>Processes every selected item through a FIFO queue with at most <paramref name="workers"/> active jobs.</summary>
    public async Task<BatchResult> ExecuteQueuedAsync<T>(
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
            return await ExecuteAsync(items, itemKey, execute, control, completed, cancellationToken);
        }

        var results = new OperationItemResult[items.Count];
        int nextIndex = -1;

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
                string key = itemKey(item);
                OperationItemResult result;
                try
                {
                    var token = await control.StartAsync(key, cancellationToken);
                    result = token is null
                        ? new(key, OperationStatus.Skipped, null, "Deselected before starting.")
                        : await execute(item, token.Value);
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

                results[index] = result;
                OperationLog.Result(logger, result);
                completed?.Report(result);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(workers, items.Count)).Select(_ => WorkAsync()));
        return new(results);
    }
}
