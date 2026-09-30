using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DoViFixer.App.Presentation.Common;

/// <summary>
/// Makes a GridView list sortable: header clicks call <see cref="IColumnSort.SortBy"/> with the
/// column's <c>Key</c>, the comparer is applied to the list's collection view (the source order is
/// unchanged), and each column's <c>Direction</c> is updated for header templates to show.
/// </summary>
public static class GridViewSort
{
    public static readonly DependencyProperty SortProperty = DependencyProperty.RegisterAttached(
        "Sort", typeof(IColumnSort), typeof(GridViewSort), new PropertyMetadata(null, OnSortChanged));

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(object), typeof(GridViewSort));

    public static readonly DependencyProperty DirectionProperty = DependencyProperty.RegisterAttached(
        "Direction", typeof(ListSortDirection?), typeof(GridViewSort));

    /// <summary>
    /// Set on an element inside a column header (for example a sort indicator) to mirror that
    /// column's <c>Direction</c> onto the element, so styles can use plain property triggers.
    /// </summary>
    public static readonly DependencyProperty ShowsColumnDirectionProperty = DependencyProperty.RegisterAttached(
        "ShowsColumnDirection", typeof(bool), typeof(GridViewSort), new PropertyMetadata(false, OnShowsColumnDirectionChanged));

    private static readonly DependencyProperty ChangeHandlerProperty = DependencyProperty.RegisterAttached(
        "ChangeHandler", typeof(PropertyChangedEventHandler), typeof(GridViewSort));

    public static IColumnSort? GetSort(DependencyObject element) => (IColumnSort?)element.GetValue(SortProperty);
    public static void SetSort(DependencyObject element, IColumnSort? value) => element.SetValue(SortProperty, value);
    public static object? GetKey(DependencyObject element) => element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, object? value) => element.SetValue(KeyProperty, value);
    public static ListSortDirection? GetDirection(DependencyObject element) => (ListSortDirection?)element.GetValue(DirectionProperty);
    public static void SetDirection(DependencyObject element, ListSortDirection? value) => element.SetValue(DirectionProperty, value);
    public static bool GetShowsColumnDirection(DependencyObject element) => (bool)element.GetValue(ShowsColumnDirectionProperty);
    public static void SetShowsColumnDirection(DependencyObject element, bool value) => element.SetValue(ShowsColumnDirectionProperty, value);

    private static void OnShowsColumnDirectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            BindingOperations.ClearBinding(sender, DirectionProperty);
            return;
        }

        // Built in code: an attached-property path with a namespace prefix inside a shared
        // resource-dictionary style is not resolved by WPF and silently binds to nothing.
        BindingOperations.SetBinding(sender, DirectionProperty, new Binding
        {
            Path = new PropertyPath("Column.(0)", DirectionProperty),
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(GridViewColumnHeader), 1)
        });
    }

    private static void OnSortChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ListView list)
        {
            return;
        }

        Unsubscribe(list, (IColumnSort?)e.OldValue);
        list.RemoveHandler(GridViewColumnHeader.ClickEvent, (RoutedEventHandler)OnHeaderClick);
        list.Loaded -= OnLoaded;
        list.Unloaded -= OnUnloaded;
        if (e.NewValue is null)
        {
            return;
        }

        list.AddHandler(GridViewColumnHeader.ClickEvent, (RoutedEventHandler)OnHeaderClick);
        list.Loaded += OnLoaded;
        list.Unloaded += OnUnloaded;
        if (list.IsLoaded)
        {
            OnLoaded(list, new RoutedEventArgs());
        }
    }

    // Subscribe only while loaded so a long-lived sort model does not keep unloaded lists alive.
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var list = (ListView)sender;
        if (GetSort(list) is not { } sort)
        {
            return;
        }

        Unsubscribe(list, sort);
        PropertyChangedEventHandler handler = (_, _) => Apply(list);
        list.SetValue(ChangeHandlerProperty, handler);
        sort.PropertyChanged += handler;
        Apply(list);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var list = (ListView)sender;
        Unsubscribe(list, GetSort(list));
    }

    private static void Unsubscribe(ListView list, IColumnSort? sort)
    {
        if (sort is not null && list.GetValue(ChangeHandlerProperty) is PropertyChangedEventHandler handler)
        {
            sort.PropertyChanged -= handler;
        }

        list.ClearValue(ChangeHandlerProperty);
    }

    private static void Apply(ListView list)
    {
        if (GetSort(list) is not { } sort)
        {
            return;
        }

        if (list.ItemsSource is not null
            && CollectionViewSource.GetDefaultView(list.ItemsSource) is ListCollectionView view
            && !ReferenceEquals(view.CustomSort, sort.Comparer))
        {
            view.CustomSort = sort.Comparer;
        }

        if (list.View is GridView grid)
        {
            foreach (var column in grid.Columns)
            {
                bool sorted = sort.Column is not null && Equals(GetKey(column), sort.Column);
                SetDirection(column, sorted ? sort.Direction : null);
            }
        }
    }

    private static void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        // Buttons inside headers and rows raise the same bubbling Click event.
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column }
            || GetKey(column) is not { } key
            || GetSort((DependencyObject)sender) is not { } sort)
        {
            return;
        }

        sort.SortBy(key);
        e.Handled = true;
    }
}
