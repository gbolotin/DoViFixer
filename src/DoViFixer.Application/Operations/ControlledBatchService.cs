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
                result = new(key, OperationStatus.Failed, null, ex.Message);
            }
            finally
            {
                control.Finish();
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
}
