using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Operations;

/// <summary>Structured event conventions shared by both presentation hosts; no logging provider dependency.</summary>
public static class OperationLog
{
    public static async Task<T> RunAsync<T>(ILogger logger, string operation, Guid id, string? input,
        Func<Task<T>> execute, CancellationToken cancellationToken, Func<T, OperationStatus>? status = null)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationId"] = id, ["Operation"] = operation, ["Input"] = input
        });
        long started = Stopwatch.GetTimestamp();
        History(logger, "Started", 0);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            T result = await execute();
            History(logger, (status?.Invoke(result) ?? OperationStatus.Completed).ToString(), Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            History(logger, "Cancelled", Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex);
            throw;
        }
        catch (Exception ex)
        {
            History(logger, "Failed", Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex);
            throw;
        }
    }

    public static void History(ILogger logger, string outcome, double elapsedMilliseconds, Exception? exception = null)
    {
        using var kind = logger.BeginScope(new Dictionary<string, object> { ["LogKind"] = "History" });
        logger.Log(outcome is "Failed" or "Partial" ? LogLevel.Error : LogLevel.Information, new EventId(1000, "OperationOutcome"), exception,
            "Operation {Outcome} after {ElapsedMilliseconds} ms", outcome, elapsedMilliseconds);
    }

    public static void Result(ILogger logger, FileResult result)
    {
        using var kind = logger.BeginScope(new Dictionary<string, object> { ["LogKind"] = "History" });
        logger.LogInformation(new EventId(1001, "FileResult"), "File {Input} {Outcome}; output {Output}; {Reason}",
            result.Input, result.Status.ToString(), result.Output, result.Message);
    }

    public static void Audit(ILogger logger, string action, string target, string outcome, Guid? planId = null)
    {
        using var kind = logger.BeginScope(new Dictionary<string, object> { ["LogKind"] = "Audit" });
        logger.LogInformation(new EventId(2000, "Audit"), "{Action} {Target}: {Outcome}; plan {PlanId}", action, target, outcome, planId);
    }
}
