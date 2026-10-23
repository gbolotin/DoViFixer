namespace DoViFixer.Application.WatchFolder;

/// <summary>What a file looks like on disk while it may still be arriving.</summary>
/// <param name="IsInUse">Another process has the file open for writing, as a copy or download does until it ends.</param>
public readonly record struct ArrivingFile(bool Exists, long Length, bool IsInUse);

/// <summary>
/// Decides when a file that appeared in a watched folder has finished arriving: its size has not changed for the settle
/// time and no other process has it open for writing. A transfer still in progress is therefore never read half-written.
/// Not thread-safe; the watcher calls it under its own lock.
/// </summary>
public sealed class ArrivalTracker(TimeSpan settleTime)
{
    private readonly Dictionary<string, Arrival> arrivals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a file must stay unchanged and closed; changing it keeps the files still arriving.</summary>
    public TimeSpan SettleTime { get; set; } = settleTime;

    private sealed record Arrival(long Length, DateTimeOffset Since);

    public bool IsEmpty => arrivals.Count == 0;

    /// <summary>Starts, or restarts, the wait for a file that was created, renamed into the folder or written to.</summary>
    public void Notice(string path, DateTimeOffset now) => arrivals[path] = new(-1, now);

    /// <summary>Checks every file still arriving and returns those that have finished; they are no longer tracked.</summary>
    public IReadOnlyList<string> Poll(DateTimeOffset now, Func<string, ArrivingFile> probe)
    {
        List<string> arrived = [];
        foreach (var (path, arrival) in arrivals.ToArray())
        {
            var file = probe(path);
            if (!file.Exists)
            {
                // Deleted, or renamed away, such as a download client's partial file.
                arrivals.Remove(path);
            }
            else if (file.Length != arrival.Length)
            {
                arrivals[path] = new(file.Length, now);
            }
            else if (!file.IsInUse && now - arrival.Since >= SettleTime)
            {
                arrivals.Remove(path);
                arrived.Add(path);
            }
        }

        return arrived;
    }
}
