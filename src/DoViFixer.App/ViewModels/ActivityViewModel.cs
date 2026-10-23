using System.Collections.ObjectModel;
using DoViFixer.App.Dialogs;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Activity;

namespace DoViFixer.App.ViewModels;

/// <summary>
/// The session's activity in the status bar's flyout, newest first. Problems reported while the flyout is closed
/// count on its button until it opens.
/// </summary>
public sealed partial class ActivityViewModel : ObservableObject, IDisposable
{
    private readonly IActivityFeed feed;
    private readonly IFileExplorer explorer;
    private readonly ITextClipboard clipboard;
    private readonly SynchronizationContext? uiContext;

    public ActivityViewModel(IActivityFeed feed, IFileExplorer explorer, ITextClipboard clipboard)
    {
        this.feed = feed;
        this.explorer = explorer;
        this.clipboard = clipboard;
        uiContext = SynchronizationContext.Current;
        foreach (var entry in feed.Entries.Reverse())
        {
            Entries.Add(entry);
        }

        feed.Reported += OnReported;
    }

    /// <summary>The entries, most recently reported first.</summary>
    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            UnseenProblems = 0;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnseenProblems), nameof(ToolTip))]
    public partial int UnseenProblems { get; private set; }

    public bool HasUnseenProblems => UnseenProblems > 0;

    public string ToolTip => UnseenProblems switch
    {
        0 => "Show what happened this session",
        1 => "1 new problem. Show what happened this session",
        _ => $"{UnseenProblems} new problems. Show what happened this session"
    };

    [RelayCommand]
    private void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ShowLocation(ActivityEntry? entry)
    {
        if (entry?.Location is { } location)
        {
            explorer.ShowInFolder(location);
        }
    }

    [RelayCommand]
    private void Copy(ActivityEntry? entry)
    {
        if (entry is not null)
        {
            clipboard.SetText(string.IsNullOrWhiteSpace(entry.Detail) ? entry.Title : $"{entry.Title}{Environment.NewLine}{entry.Detail}");
        }
    }

    private void OnReported(object? sender, ActivityEntry entry)
    {
        if (uiContext is null || SynchronizationContext.Current == uiContext)
        {
            Apply(entry);
        }
        else
        {
            uiContext.Post(_ => Apply(entry), null);
        }
    }

    private void Apply(ActivityEntry entry)
    {
        int index = Entries.ToList().FindIndex(existing => existing.Id == entry.Id);
        if (index >= 0)
        {
            // Posted reports can arrive out of order; a running entry never replaces its outcome.
            if (entry.IsRunning && !Entries[index].IsRunning)
            {
                return;
            }

            // As in the feed, the latest report comes first: a finished batch moves above its files.
            Entries.RemoveAt(index);
        }

        Entries.Insert(0, entry);
        if (Entries.Count > ActivityFeed.Capacity)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        OnPropertyChanged(nameof(IsEmpty));

        if (entry.IsProblem && !IsOpen)
        {
            UnseenProblems++;
        }
    }

    public void Dispose() => feed.Reported -= OnReported;
}
