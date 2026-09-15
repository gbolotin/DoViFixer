using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Operations;
/// <summary>Runs selected items sequentially; per-file cancellation awaits recovery before continuing.</summary>
public sealed class ControlledBatchService(ILogger<ControlledBatchService> logger)
{
    public async Task<BatchResult> ExecuteAsync<T>(IReadOnlyList<T> items, Func<T, string> path, Func<T, CancellationToken, Task<FileResult>> execute, BatchControl control, IProgress<FileResult>? completed, CancellationToken cancellationToken)
    {
        var results = new List<FileResult>();
        foreach (var item in items)
        {
            string input = path(item);
            FileResult result;
            try
            {
                var token = control.Start(input, cancellationToken);
                result = token is null ? new(input, OperationStatus.Skipped, null, "Deselected before starting.") : await execute(item, token.Value);
            }
            catch (OperationCanceledException)
            {
                result = new(input, OperationStatus.Cancelled, null, "Cancelled; operation cleanup has finished.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Batch item failed: {Input}", input);
                result = new(input, OperationStatus.Failed, null, ex.Message);
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
