namespace DoViFixer.Application.Diagnostics;

public enum FailureCategory
{
    Unknown, FileInUse, AccessDenied, DiskFull, MissingFile, MissingTool, InvalidMedia, Timeout, NativeToolFailed, DestinationExists
}

public sealed record LockHolder(int ProcessId, string Name);
public sealed record FileLockReport(IReadOnlyList<LockHolder> Holders, string? UnavailableReason = null);

public sealed record FailureDiagnosis(FailureCategory Category, string Operation, string? Path, Guid? ToolInvocationId, string TechnicalDetail)
{
    public FileLockReport? FileLock { get; init; }

    public string Guidance => FileLock is { Holders.Count: > 0 }
        ? "Close applications using this file, then retry."
        : Category switch
    {
        FailureCategory.FileInUse => "Close applications using this file, then retry.",
        FailureCategory.AccessDenied => "Check folder permissions and whether the file is read-only.",
        FailureCategory.DiskFull => "Free space on the output and temporary drives, then retry.",
        FailureCategory.MissingFile => "Check that the file and its drive are still available.",
        FailureCategory.MissingTool => "Check native tool paths in Settings or run dependencies check.",
        FailureCategory.InvalidMedia => "Check that the input is complete and supported by the selected tool.",
        FailureCategory.Timeout => "Check the tool logs and available disk space before retrying.",
        FailureCategory.DestinationExists => "Choose a different output filename.",
        _ => "Review the diagnostic log or export a diagnostic ZIP for investigation."
    };

    public string Message
    {
        get
        {
            string holders = FileLock is { Holders.Count: > 0 }
                ? " Applications using the file: " + string.Join(", ", FileLock.Holders.Select(h => $"{h.Name} (PID {h.ProcessId})")) + "."
                : FileLock is not null ? " No lock holder could be identified." : "";
            return $"{Operation}: {TechnicalDetail}{(Path is null ? "" : $" Path: {Path}.")}{holders} {Guidance}";
        }
    }

    public static FailureDiagnosis? FromException(Exception exception) => exception switch
    {
        OperationFailureException failure => failure.Diagnosis,
        OperationTimeoutException timeout => timeout.Diagnosis,
        ToolStartFailureException startup => startup.Diagnosis,
        _ => null
    };
}

public sealed class OperationFailureException(FailureDiagnosis diagnosis, Exception? innerException = null)
    : IOException(diagnosis.Message, innerException)
{
    public FailureDiagnosis Diagnosis { get; } = diagnosis;
}

// Preserve timeout handling and keep startup failures out of recoverable I/O retry paths.
public sealed class OperationTimeoutException(FailureDiagnosis diagnosis, TimeoutException innerException)
    : TimeoutException(diagnosis.Message, innerException)
{
    public FailureDiagnosis Diagnosis { get; } = diagnosis;
}

public sealed class ToolStartFailureException(FailureDiagnosis diagnosis, Exception innerException)
    : Exception(diagnosis.Message, innerException)
{
    public FailureDiagnosis Diagnosis { get; } = diagnosis;
}
