using System.Windows;
using System.Windows.Controls;

namespace DoViFixer.App.Views;

/// <summary>
/// Connects a list box's selection to its view model in either selection mode. <c>CurrentItem</c> is the row whose
/// details show: in single selection it is the selected row, both ways; in extended selection it is the row the user
/// selected last, and setting it does not change the selection. <c>SelectedItems</c> mirrors every highlighted row
/// both ways in extended selection, and is ignored in single selection.
/// </summary>
public static class ListSelection
{
    public static readonly DependencyProperty CurrentItemProperty = DependencyProperty.RegisterAttached(
        "CurrentItem",
        typeof(object),
        typeof(ListSelection),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCurrentItemChanged));

    /// <summary>The highlighted rows. Bind it to a list that is never null, so the list box is connected from the start.</summary>
    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.RegisterAttached(
        "SelectedItems",
        typeof(IReadOnlyList<object>),
        typeof(ListSelection),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemsChanged));

    private static readonly DependencyProperty IsConnectedProperty =
        DependencyProperty.RegisterAttached("IsConnected", typeof(bool), typeof(ListSelection), new PropertyMetadata(false));

    // Set while this class changes one side, so the change is not sent back to where it came from.
    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(ListSelection), new PropertyMetadata(false));

    public static object? GetCurrentItem(DependencyObject element) => element.GetValue(CurrentItemProperty);

    public static void SetCurrentItem(DependencyObject element, object? value) => element.SetValue(CurrentItemProperty, value);

    public static IReadOnlyList<object>? GetSelectedItems(DependencyObject element) => (IReadOnlyList<object>?)element.GetValue(SelectedItemsProperty);

    public static void SetSelectedItems(DependencyObject element, IReadOnlyList<object>? value) => element.SetValue(SelectedItemsProperty, value);

    private static void OnCurrentItemChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ListBox list || !Connect(list) || list.SelectionMode != SelectionMode.Single)
        {
            return;
        }

        Update(list, () => list.SetCurrentValue(ListBox.SelectedItemProperty, e.NewValue));
    }

    private static void OnSelectedItemsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ListBox list || !Connect(list) || list.SelectionMode == SelectionMode.Single)
        {
            return;
        }

        var items = (IReadOnlyList<object>?)e.NewValue ?? [];
        Update(list, () =>
        {
            var wanted = new HashSet<object>(items);
            for (int i = list.SelectedItems.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(list.SelectedItems[i]!))
                {
                    list.SelectedItems.RemoveAt(i);
                }
            }

            var selected = new HashSet<object>(list.SelectedItems.Cast<object>());
            foreach (var item in items)
            {
                // A row the list's filter hides cannot be selected.
                if (!selected.Contains(item) && list.Items.Contains(item))
                {
                    list.SelectedItems.Add(item);
                }
            }
        });
    }

    /// <summary>Returns false while this class is changing the list, whose own changes need no further work.</summary>
    private static bool Connect(ListBox list)
    {
        if ((bool)list.GetValue(IsUpdatingProperty))
        {
            return false;
        }

        if (!(bool)list.GetValue(IsConnectedProperty))
        {
            list.SetValue(IsConnectedProperty, true);
            list.SelectionChanged += OnSelectionChanged;
        }

        return true;
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selectors inside the rows raise their own SelectionChanged, which bubbles to the list.
        if (sender is not ListBox list || !ReferenceEquals(e.OriginalSource, list) || (bool)list.GetValue(IsUpdatingProperty))
        {
            return;
        }

        Update(list, () =>
        {
            if (list.SelectionMode == SelectionMode.Single)
            {
                list.SetCurrentValue(CurrentItemProperty, list.SelectedItem);
                return;
            }

            list.SetCurrentValue(SelectedItemsProperty, list.SelectedItems.Cast<object>().ToArray());
            object? current = GetCurrentItem(list);
            if (e.AddedItems.Count > 0)
            {
                current = e.AddedItems[e.AddedItems.Count - 1];
            }
            else if (current is not null && !list.SelectedItems.Contains(current))
            {
                current = list.SelectedItem ?? current;
            }

            list.SetCurrentValue(CurrentItemProperty, current);
        });
    }

    private static void Update(ListBox list, Action update)
    {
        list.SetValue(IsUpdatingProperty, true);
        try
        {
            update();
        }
        finally
        {
            list.SetValue(IsUpdatingProperty, false);
        }
    }
}
