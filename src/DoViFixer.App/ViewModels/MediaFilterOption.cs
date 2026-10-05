namespace DoViFixer.App.ViewModels;

/// <summary>One filter choice above the media list, labeled with how many listed files it shows.</summary>
public sealed class MediaFilterOption(MediaFileFilter filter, string name, Action<MediaFileFilter> select) : ObservableObject
{
    private int count;
    private bool selected;

    public MediaFileFilter Filter { get; } = filter;
    public string Label => $"{name} ({count})";

    /// <summary>Other profiles are rare, so their choice appears only while it shows files or is chosen.</summary>
    public bool IsVisible => Filter != MediaFileFilter.OtherProfiles || count > 0 || selected;

    public int Count
    {
        get => count;
        set
        {
            if (SetProperty(ref count, value))
            {
                OnPropertyChanged(nameof(Label));
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    public bool IsSelected
    {
        get => selected;
        set
        {
            if (SetProperty(ref selected, value))
            {
                OnPropertyChanged(nameof(IsVisible));
                if (value)
                {
                    select(Filter);
                }
            }
        }
    }
}
