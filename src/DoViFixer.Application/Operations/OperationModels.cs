namespace DoViFixer.Application.Operations;

public enum OperationStatus { Completed, Partial, Failed, Cancelled, Skipped }
public sealed record OperationProgress(Guid OperationId, string Stage, string? File = null, double? Percent = null);
public sealed record FileResult(string Input, OperationStatus Status, string? Output, string Message);
public sealed record BatchResult(IReadOnlyList<FileResult> Files)
{
    public OperationStatus Status => Files.Count == 0 ? OperationStatus.Skipped
        : Files.Any(f => f.Status == OperationStatus.Cancelled) ? OperationStatus.Cancelled
        : Files.All(f => f.Status == OperationStatus.Completed) ? OperationStatus.Completed
        : Files.Any(f => f.Status is OperationStatus.Completed or OperationStatus.Partial) ? OperationStatus.Partial : OperationStatus.Failed;
}
public sealed class DependencyNotReadyException(string message) : InvalidOperationException(message);
