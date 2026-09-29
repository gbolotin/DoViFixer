using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DoViFixer.App.Presentation.Common;

public static class GridViewSizing
{
    public static readonly DependencyProperty LastColumnMinimumWidthProperty = DependencyProperty.RegisterAttached(
        "LastColumnMinimumWidth", typeof(double), typeof(GridViewSizing),
        new PropertyMetadata(double.NaN, OnMinimumWidthChanged),
        value => value is double width && (double.IsNaN(width) || double.IsFinite(width) && width >= 0));

    private static readonly DependencyProperty LayoutHandlerProperty = DependencyProperty.RegisterAttached(
        "LayoutHandler", typeof(EventHandler), typeof(GridViewSizing));

    public static double GetLastColumnMinimumWidth(DependencyObject element) => (double)element.GetValue(LastColumnMinimumWidthProperty);
    public static void SetLastColumnMinimumWidth(DependencyObject element, double value) => element.SetValue(LastColumnMinimumWidthProperty, value);

    private static void OnMinimumWidthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ListView list)
        {
            return;
        }

        list.Loaded -= OnLoaded;
        list.Unloaded -= OnUnloaded;
        OnUnloaded(list, new RoutedEventArgs());
        list.ClearValue(LayoutHandlerProperty);
        if (double.IsNaN((double)e.NewValue))
        {
            return;
        }

        // ponytail: one small list; use targeted events if per-layout work becomes measurable.
        // Layout also observes auto-sized and reordered columns.
        EventHandler handler = (_, _) => ResizeLastColumn(list);
        list.SetValue(LayoutHandlerProperty, handler);
        list.Loaded += OnLoaded;
        list.Unloaded += OnUnloaded;
        if (list.IsLoaded)
        {
            OnLoaded(list, new RoutedEventArgs());
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var list = (ListView)sender;
        var handler = (EventHandler)list.GetValue(LayoutHandlerProperty);
        list.LayoutUpdated -= handler;
        list.LayoutUpdated += handler;
        ResizeLastColumn(list);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var list = (ListView)sender;
        list.LayoutUpdated -= (EventHandler?)list.GetValue(LayoutHandlerProperty);
    }

    private static void ResizeLastColumn(ListView list)
    {
        if (!list.IsVisible || list.View is not GridView { Columns.Count: > 0 } grid
            || FindChild<ScrollViewer>(list) is not { ViewportWidth: > 0 } scroll)
        {
            return;
        }

        double available = scroll.ViewportWidth;
        if (scroll.Template.FindName("PART_ScrollContentPresenter", scroll) is ScrollContentPresenter viewport
            && scroll.Template.FindName("PART_VerticalScrollBar", scroll) is ScrollBar { IsVisible: true } bar)
        {
            // Fluent overlays its scrollbar; other themes already exclude it from the viewport.
            available = Math.Min(available, bar.TranslatePoint(new Point(), viewport).X);
        }

        double totalWidth = grid.Columns.Sum(column => column.ActualWidth);
        // GridView reserves two pixels for its trailing padding header.
        double spacing = 2;
        if (FindChild<ListViewItem>(list) is { } row)
        {
            spacing = Math.Max(spacing, row.DesiredSize.Width - totalWidth);
        }

        var lastColumn = grid.Columns[^1];
        double width = Math.Max(GetLastColumnMinimumWidth(list), available - spacing - (totalWidth - lastColumn.ActualWidth));
        if (double.IsNaN(lastColumn.Width) || Math.Abs(lastColumn.Width - width) > 0.1)
        {
            lastColumn.SetCurrentValue(GridViewColumn.WidthProperty, width);
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }
            if (FindChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }
        return null;
    }
}
