namespace DoViFixer.Application.Activity;

public enum ActivitySeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>The job an operation runs, which decides how its activity entries are worded.</summary>
public enum ActivityOperation
{
    Scan,
    Inspection,
    DeepInspection,
    Conversion,
    /// <summary>A conversion the watch folder started without asking.</summary>
    AutoConversion,
    Restoration,
    Cleanup
}

/// <summary>
/// One line of the activity feed, worded for people rather than for diagnosis. An operation keeps one entry: reporting
/// an entry with the same <paramref name="Id"/> again replaces it, so "Converting…" becomes "Converted".
/// </summary>
/// <param name="FilePath">The file the entry is about, or <see langword="null"/> for a batch.</param>
/// <param name="Location">The file worth showing in File Explorer: the output, or the original the operation kept.</param>
public sealed record ActivityEntry(Guid Id, ActivitySeverity Severity, string Title, string Detail = "", string? FilePath = null, string? Location = null)
{
    /// <summary>When the entry was last reported; the feed sets it.</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>The operation is still running, and a later report will replace this entry.</summary>
    public bool IsRunning { get; init; }

    public bool IsProblem => Severity is ActivitySeverity.Warning or ActivitySeverity.Error;

    /// <summary>The title and detail on one line, for a status bar or a notification.</summary>
    public string Text => Detail.Length == 0 ? Title : $"{Title} · {Detail.ReplaceLineEndings(" ")}";
}
