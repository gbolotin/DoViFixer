using System.Collections;
using System.ComponentModel;

namespace DoViFixer.App.Presentation.Common;

/// <summary>Sort state that <see cref="GridViewSort"/> reads from and updates for a sortable list.</summary>
public interface IColumnSort : INotifyPropertyChanged
{
    object? Column { get; }
    ListSortDirection Direction { get; }
    IComparer? Comparer { get; }
    void SortBy(object column);
}

/// <summary>
/// Column sort state for a list: clicking the sorted column toggles the direction and
/// clicking another column sorts it ascending. The comparer orders the view, not the source.
/// </summary>
/// <param name="canSort">
/// When it returns false, <see cref="SortBy"/> is ignored; for example while a batch is processing
/// rows in the current display order.
/// </param>
public sealed class ColumnSort<TItem>(Func<object, ListSortDirection, IComparer<TItem>> createComparer, Func<bool>? canSort = null)
    : ObservableObject, IColumnSort
{
    private object? column;
    private ListSortDirection direction;
    private Comparer<TItem>? comparer;

    public object? Column => column;
    public ListSortDirection Direction => direction;
    public IComparer<TItem>? Comparer => comparer;
    IComparer? IColumnSort.Comparer => comparer;

    public void SortBy(object column)
    {
        if (canSort?.Invoke() == false)
        {
            return;
        }

        direction = Equals(this.column, column) && direction == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        this.column = column;
        RaisePropertyChanged(nameof(Column));
        RaisePropertyChanged(nameof(Direction));
        Refresh();
    }

    /// <summary>Publishes a new comparer so views re-sort items whose values changed since the last sort.</summary>
    public void Refresh()
    {
        if (column is null)
        {
            return;
        }

        comparer = Comparer<TItem>.Create(createComparer(column, direction).Compare);
        RaisePropertyChanged(nameof(Comparer));
    }

    /// <summary>Returns items in the sorted order, or in their original order when nothing is sorted.</summary>
    public TItem[] Order(IEnumerable<TItem> items) => comparer is null ? [.. items] : [.. items.Order(comparer)];
}
