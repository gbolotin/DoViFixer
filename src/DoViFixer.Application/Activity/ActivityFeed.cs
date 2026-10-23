using DoViFixer.Application.Abstractions;

namespace DoViFixer.Application.Activity;

/// <summary>The session's activity, kept in memory. Batch workers report from several threads at once.</summary>
public sealed class ActivityFeed(TimeProvider time) : IActivityFeed
{
    /// <summary>The most entries kept; the oldest go first.</summary>
    public const int Capacity = 500;

    private readonly Lock gate = new();
    private readonly List<ActivityEntry> entries = [];

    public event EventHandler<ActivityEntry>? Reported;

    public IReadOnlyList<ActivityEntry> Entries
    {
        get
        {
            lock (gate)
            {
                return entries.ToArray();
            }
        }
    }

    public void Report(ActivityEntry entry)
    {
        var stamped = entry with { Time = time.GetUtcNow() };
        lock (gate)
        {
            // The entries stay in the order they were last reported, so a finished batch follows its files.
            entries.RemoveAll(existing => existing.Id == entry.Id);
            entries.Add(stamped);
            if (entries.Count > Capacity)
            {
                entries.RemoveAt(0);
            }
        }

        Reported?.Invoke(this, stamped);
    }
}
