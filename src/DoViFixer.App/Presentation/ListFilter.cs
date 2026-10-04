using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DoViFixer.App.Presentation;

/// <summary>
/// Filters a list's own collection view with a bound predicate. Binding a new predicate re-filters the list, and
/// items whose <c>LiveProperty</c> changes are re-filtered as it changes, so rows enter or leave without a refresh.
/// </summary>
/// <remarks>
/// While the list is loaded, an <see cref="IList"/> items source is presented through a view owned by the list,
/// so other lists bound to the same source are not filtered. An items source that already is a
/// <see cref="ListCollectionView"/>, such as the view <see cref="GridViewSort"/> presents, is filtered directly.
/// </remarks>
public static class ListFilter
{
    /// <summary>Returns whether an item is shown, or <see langword="null"/> to show every item.</summary>
    public static readonly DependencyProperty PredicateProperty = DependencyProperty.RegisterAttached(
        "Predicate", typeof(Predicate<object>), typeof(ListFilter), new PropertyMetadata(null, OnPredicateChanged));

    /// <summary>The item property the predicate reads; when it changes on an item, that item is filtered again.</summary>
    public static readonly DependencyProperty LivePropertyProperty = DependencyProperty.RegisterAttached(
        "LiveProperty", typeof(string), typeof(ListFilter), new PropertyMetadata(null, OnPredicateChanged));

    private static readonly DependencyProperty AttachmentProperty = DependencyProperty.RegisterAttached(
        "Attachment", typeof(Attachment), typeof(ListFilter));

    private static readonly DependencyPropertyDescriptor ItemsSourceDescriptor =
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ItemsControl));

    public static Predicate<object>? GetPredicate(DependencyObject element) => (Predicate<object>?)element.GetValue(PredicateProperty);

    public static void SetPredicate(DependencyObject element, Predicate<object>? value) => element.SetValue(PredicateProperty, value);

    public static string? GetLiveProperty(DependencyObject element) => (string?)element.GetValue(LivePropertyProperty);

    public static void SetLiveProperty(DependencyObject element, string? value) => element.SetValue(LivePropertyProperty, value);

    private static void OnPredicateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ItemsControl list)
        {
            return;
        }

        list.Loaded -= OnLoaded;
        list.Unloaded -= OnUnloaded;
        list.Loaded += OnLoaded;
        list.Unloaded += OnUnloaded;
        if (list.GetValue(AttachmentProperty) is Attachment attachment)
        {
            attachment.Apply();
        }
        else if (list.IsLoaded)
        {
            OnLoaded(list, new RoutedEventArgs());
        }
    }

    // Attach only while loaded so a long-lived source collection does not keep unloaded lists alive.
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var list = (ItemsControl)sender;
        Detach(list);
        list.SetValue(AttachmentProperty, new Attachment(list));
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((ItemsControl)sender);

    private static void Detach(ItemsControl list)
    {
        if (list.GetValue(AttachmentProperty) is Attachment attachment)
        {
            attachment.Detach();
            list.ClearValue(AttachmentProperty);
        }
    }

    /// <summary>Connects one loaded list to its predicate and owns the list's collection view when it creates one.</summary>
    private sealed class Attachment
    {
        private readonly ItemsControl list;
        private ListCollectionView? ownedView;

        public Attachment(ItemsControl list)
        {
            this.list = list;
            ItemsSourceDescriptor.AddValueChanged(list, OnItemsSourceChanged);
            PresentSource();
            Apply();
        }

        public void Detach()
        {
            ItemsSourceDescriptor.RemoveValueChanged(list, OnItemsSourceChanged);
            if (ownedView is not null)
            {
                var view = ownedView;
                ownedView = null;
                if (ReferenceEquals(list.ItemsSource, view))
                {
                    // Return the bound source so a later load presents a fresh view of it.
                    list.SetCurrentValue(ItemsControl.ItemsSourceProperty, view.SourceCollection);
                }

                view.DetachFromSourceCollection();
            }
        }

        private void OnItemsSourceChanged(object? sender, EventArgs e)
        {
            if (ownedView is not null && ReferenceEquals(list.ItemsSource, ownedView))
            {
                return;
            }

            // Another behavior or the binding replaced the source: release the view of the previous source.
            ownedView?.DetachFromSourceCollection();
            ownedView = null;
            PresentSource();
            Apply();
        }

        private void PresentSource()
        {
            if (list.ItemsSource is IList source and not ICollectionView)
            {
                ownedView = new ListCollectionView(source);
                list.SetCurrentValue(ItemsControl.ItemsSourceProperty, ownedView);
            }
        }

        public void Apply()
        {
            if (list.ItemsSource is not ListCollectionView view)
            {
                return;
            }

            if (GetLiveProperty(list) is { Length: > 0 } property && view.CanChangeLiveFiltering && !view.LiveFilteringProperties.Contains(property))
            {
                view.LiveFilteringProperties.Add(property);
                view.IsLiveFiltering = true;
            }

            var predicate = GetPredicate(list);
            if (!ReferenceEquals(view.Filter, predicate))
            {
                view.Filter = predicate;
            }
        }
    }
}
