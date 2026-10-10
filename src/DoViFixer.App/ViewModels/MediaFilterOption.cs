namespace DoViFixer.App.ViewModels;

/// <summary>One filter choice above the media list, labeled with how many listed files it shows.</summary>
public sealed partial class MediaFilterOption(MediaFileFilter filter, string name, Action<MediaFileFilter> select) : ObservableObject
{
    public MediaFileFilter Filter { get; } = filter;
    public string Label => $"{name} ({Count})";

    /// <summary>Other profiles are rare, so their choice appears only while it shows files or is chosen.</summary>
    public bool IsVisible => Filter != MediaFileFilter.OtherProfiles || Count > 0 || IsSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(IsVisible))]
    public partial int Count { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            select(Filter);
        }
    }
}
