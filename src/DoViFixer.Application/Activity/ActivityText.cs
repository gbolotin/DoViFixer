using System.Globalization;
using DoViFixer.Application.Operations;

namespace DoViFixer.Application.Activity;

/// <summary>
/// Words activity entries for people: file names rather than paths, readable durations, and what happened to the
/// user's files. Paths, ids and tool output stay in the diagnostic log.
/// </summary>
public static class ActivityText
{
    private sealed record Words(string Running, string Done, string Verb, string Noun, string Item);

    private static Words For(ActivityOperation operation) => operation switch
    {
        ActivityOperation.Scan => new("Scanning", "Scanned", "scan", "scan", "file"),
        ActivityOperation.Inspection => new("Inspecting", "Inspected", "inspect", "inspection", "file"),
        ActivityOperation.DeepInspection => new("Deep inspecting", "Deep inspected", "deep inspect", "deep inspection", "file"),
        ActivityOperation.Conversion => new("Converting", "Converted", "convert", "conversion", "file"),
        ActivityOperation.Restoration => new("Restoring", "Restored", "restore", "restoration", "file"),
        ActivityOperation.Cleanup => new("Removing", "Removed", "remove", "cleanup", "backup file"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    public static ActivityEntry Started(Guid id, ActivityOperation operation, string path) =>
        new(id, ActivitySeverity.Info, $"{For(operation).Running} {Name(path)}…", FilePath: path) { IsRunning = true };

    /// <summary>The outcome of one file. Messages already say what happened to the original, so they become the detail.</summary>
    public static ActivityEntry Finished(Guid id, ActivityOperation operation, OperationItemResult result, TimeSpan elapsed)
    {
        var words = For(operation);
        string name = Name(result.Item);
        var (severity, title) = result.Status switch
        {
            OperationStatus.Completed => (ActivitySeverity.Success, $"{words.Done} {name} in {Duration(elapsed)}"),
            OperationStatus.Partial => (ActivitySeverity.Warning, $"{words.Done} {name}, with a problem"),
            OperationStatus.Skipped => (ActivitySeverity.Info, $"Skipped {name}"),
            OperationStatus.Cancelled => (ActivitySeverity.Info, $"Cancelled the {words.Noun} of {name}"),
            _ => (ActivitySeverity.Error, $"Couldn't {words.Verb} {name}")
        };
        return new(id, severity, title, result.Message.Trim(), result.Item, result.Output ?? result.Original);
    }

    public static ActivityEntry BatchStarted(Guid id, ActivityOperation operation, int count) =>
        new(id, ActivitySeverity.Info, $"{For(operation).Running} {Count(count, For(operation).Item)}…") { IsRunning = true };

    /// <summary>
    /// A batch's summary. Files the batch never reached, because it was cancelled, count as not started.
    /// </summary>
    public static ActivityEntry BatchFinished(Guid id, ActivityOperation operation, int count, IReadOnlyCollection<OperationItemResult> results, TimeSpan elapsed)
    {
        var words = For(operation);
        int completed = results.Count(r => r.Status == OperationStatus.Completed);
        int partial = results.Count(r => r.Status == OperationStatus.Partial);
        int failed = results.Count(r => r.Status == OperationStatus.Failed);
        int skipped = results.Count(r => r.Status == OperationStatus.Skipped);
        int cancelled = results.Count(r => r.Status == OperationStatus.Cancelled);
        int notStarted = Math.Max(0, count - results.Count);
        int done = completed + partial;

        var parts = new List<string>();
        AddPart(parts, partial, "with a problem");
        AddPart(parts, failed, "failed");
        AddPart(parts, skipped, "skipped");
        AddPart(parts, cancelled, "cancelled");
        AddPart(parts, notStarted, "not started");
        parts.Add($"took {Duration(elapsed)}");

        string title = done == count
            ? $"{words.Done} {Count(count, words.Item)}"
            : done == 0 && failed > 0
                ? $"Couldn't {words.Verb} {Count(failed, words.Item)}"
                : $"{words.Done} {Number(done)} of {Count(count, words.Item)}";
        var severity = failed > 0
            ? done > 0 ? ActivitySeverity.Warning : ActivitySeverity.Error
            : partial > 0 ? ActivitySeverity.Warning
            : done == count ? ActivitySeverity.Success : ActivitySeverity.Info;
        return new(id, severity, title, string.Join(" · ", parts));
    }

    /// <summary>A readable duration: "less than a second", "42 s", "3 min 5 s" or "1 h 12 min".</summary>
    public static string Duration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            return "less than a second";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{(int)elapsed.TotalSeconds} s";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return elapsed.Seconds == 0 ? $"{elapsed.Minutes} min" : $"{elapsed.Minutes} min {elapsed.Seconds} s";
        }

        return elapsed.Minutes == 0 ? $"{(int)elapsed.TotalHours} h" : $"{(int)elapsed.TotalHours} h {elapsed.Minutes} min";
    }

    private static string Name(string path) => Path.GetFileName(path) is { Length: > 0 } name ? name : path;

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Count(int value, string item) => value == 1 ? $"1 {item}" : $"{Number(value)} {item}s";

    private static void AddPart(List<string> parts, int value, string text)
    {
        if (value > 0)
        {
            parts.Add($"{Number(value)} {text}");
        }
    }
}
