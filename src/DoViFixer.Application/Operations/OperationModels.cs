namespace DoViFixer.Application.Operations;
public enum OperationStatus
{
    Completed,
    Partial,
    Failed,
    Cancelled,
    Skipped
}

public interface IOperationResult
{
    OperationStatus Status { get; }
    string Message { get; }
}

public interface IOperationItemResult : IOperationResult
{
    string Item { get; }
    string? Output { get; }
}

public static class OperationStatusAggregator
{
    public static OperationStatus Aggregate(IEnumerable<OperationStatus> statuses)
    {
        var list = statuses as IReadOnlyCollection<OperationStatus> ?? statuses.ToList();
        if (list.Count == 0)
        {
            return OperationStatus.Skipped;
        }

        if (list.Any(s => s == OperationStatus.Cancelled))
        {
            return OperationStatus.Cancelled;
        }

        if (list.All(s => s == OperationStatus.Completed))
        {
            return OperationStatus.Completed;
        }

        if (list.Any(s => s is OperationStatus.Completed or OperationStatus.Partial))
        {
            return OperationStatus.Partial;
        }

        return OperationStatus.Failed;
    }
}

public sealed record OperationProgress(Guid OperationId, string Stage, string? Item = null, double? Percent = null);

public sealed record OperationItemResult(string Item, OperationStatus Status, string? Output = null, string Message = "") : IOperationItemResult;

public record BatchResult<T>(IReadOnlyList<T> Items) where T : IOperationResult
{
    public BatchResult(IEnumerable<T> items) : this(items as IReadOnlyList<T> ?? items.ToArray())
    {
    }

    public OperationStatus Status => OperationStatusAggregator.Aggregate(Items.Select(i => i.Status));
}

public sealed record BatchResult(IReadOnlyList<OperationItemResult> Items) : BatchResult<OperationItemResult>(Items)
{
    public BatchResult(IEnumerable<OperationItemResult> items) : this(items as IReadOnlyList<OperationItemResult> ?? items.ToArray())
    {
    }
}

public sealed class DependencyNotReadyException(string message) : InvalidOperationException(message);
