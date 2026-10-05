namespace DoViFixer.App.ViewModels;

/// <summary>Which files the media list shows, grouped by what their last scan found.</summary>
public enum MediaFileFilter
{
    All,
    Profile7,
    Profile81,
    NoDolbyVision,
    OtherProfiles,
    NotScanned
}
