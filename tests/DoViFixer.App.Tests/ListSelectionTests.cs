using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using DoViFixer.App.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed partial class ListSelectionTests
{
    private sealed partial class Source : ObservableObject
    {
        [ObservableProperty]
        public partial IReadOnlyList<object> Rows { get; set; } = [];

        [ObservableProperty]
        public partial object? Current { get; set; }
    }

    private static (ListBox List, Source Source) Create(SelectionMode mode)
    {
        var source = new Source();
        var list = new ListBox { ItemsSource = new[] { "Mountain", "Ocean", "City" }, SelectionMode = mode, DataContext = source };
        BindingOperations.SetBinding(list, ListSelection.SelectedItemsProperty, new Binding(nameof(Source.Rows)));
        BindingOperations.SetBinding(list, ListSelection.CurrentItemProperty, new Binding(nameof(Source.Current)));
        return (list, source);
    }

    [TestMethod]
    public Task ExtendedSelectionMirrorsHighlightedRowsBothWays() => DispatcherThread.RunAsync(() =>
    {
        var (list, source) = Create(SelectionMode.Extended);

        list.SelectedItems.Add("Mountain");
        list.SelectedItems.Add("City");
        CollectionAssert.AreEqual(new[] { "Mountain", "City" }, source.Rows.ToArray());
        Assert.AreEqual("City", source.Current, "The details show the row selected last.");

        source.Rows = ["Ocean", "City"];
        CollectionAssert.AreEquivalent(new[] { "Ocean", "City" }, list.SelectedItems.Cast<object>().ToArray());
        Assert.AreEqual("City", source.Current, "Selecting rows from the view model leaves the details alone.");

        source.Current = "Mountain";
        CollectionAssert.AreEquivalent(new[] { "Ocean", "City" }, list.SelectedItems.Cast<object>().ToArray(), "Showing another row's details keeps the selection.");

        list.SelectedItems.Remove("City");
        CollectionAssert.AreEqual(new[] { "Ocean" }, source.Rows.ToArray());
        return Task.CompletedTask;
    }, TimeSpan.FromSeconds(10));

    [TestMethod]
    public Task SingleSelectionFollowsTheCurrentRowAndIgnoresHighlightedRows() => DispatcherThread.RunAsync(() =>
    {
        var (list, source) = Create(SelectionMode.Single);

        source.Current = "Ocean";
        Assert.AreEqual("Ocean", list.SelectedItem);

        list.SelectedItem = "City";
        Assert.AreEqual("City", source.Current);

        source.Rows = ["Mountain", "Ocean"];
        Assert.AreEqual("City", list.SelectedItem, "Check boxes, not the list, choose files in single selection.");
        return Task.CompletedTask;
    }, TimeSpan.FromSeconds(10));
}
