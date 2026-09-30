using System.ComponentModel;
using System.Globalization;

namespace DoViFixer.App.ViewModels;

/// <summary>Orders media rows by a list column; blank values stay last and ties fall back to the file name.</summary>
public sealed class MediaRowComparer(MediaSortColumn column, ListSortDirection direction) : IComparer<MediaRow>
{
    private static readonly StringComparer Text = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    public int Compare(MediaRow? x, MediaRow? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? 1 : -1;
        }

        int result = column switch
        {
            MediaSortColumn.Classification => CompareText(x.Classification, y.Classification),
            MediaSortColumn.Status => CompareText(x.Status, y.Status),
            _ => Direct(Text.Compare(x.Name, y.Name))
        };

        if (result != 0)
        {
            return result;
        }

        int byName = Text.Compare(x.Name, y.Name);
        return byName != 0 ? byName : Text.Compare(x.Path, y.Path);
    }

    private int CompareText(string x, string y)
    {
        bool xBlank = string.IsNullOrWhiteSpace(x);
        bool yBlank = string.IsNullOrWhiteSpace(y);
        if (xBlank || yBlank)
        {
            return xBlank == yBlank ? 0 : xBlank ? 1 : -1;
        }

        return Direct(Text.Compare(x, y));
    }

    private int Direct(int result) => direction == ListSortDirection.Descending ? -result : result;
}
