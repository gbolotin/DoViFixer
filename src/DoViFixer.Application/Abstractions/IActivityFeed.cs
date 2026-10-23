using DoViFixer.Application.Activity;

namespace DoViFixer.Application.Abstractions;

/// <summary>
/// What the user's operations did this session, for people to read. The diagnostic log keeps the technical record;
/// this feed keeps only the outcomes worth showing.
/// </summary>
public interface IActivityFeed
{
    /// <summary>The entries, least recently reported first.</summary>
    IReadOnlyList<ActivityEntry> Entries { get; }

    /// <summary>Raised on the reporting thread after an entry is added or replaced.</summary>
    event EventHandler<ActivityEntry>? Reported;

    /// <summary>Adds the entry as the newest, replacing the entry with the same id.</summary>
    void Report(ActivityEntry entry);
}
