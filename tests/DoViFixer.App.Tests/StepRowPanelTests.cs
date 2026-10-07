using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DoViFixer.App.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class StepRowPanelTests
{
    [TestMethod]
    public Task LastStepEndsAtTheRightEdgeAndTheOthersShareTheRestEqually() => DispatcherThread.RunAsync(() =>
    {
        var panel = new StepRowPanel();
        for (int i = 0; i < 4; i++)
        {
            panel.Children.Add(new Border { Width = i == 3 ? 60 : 20, Height = 10 });
        }

        panel.Measure(new Size(600, 100));
        panel.Arrange(new Rect(0, 0, 600, 100));

        Assert.AreEqual(600, panel.ActualWidth);
        var bounds = panel.Children.Cast<Border>().Select(step => LayoutInformation.GetLayoutSlot(step)).ToArray();
        CollectionAssert.AreEqual(new[] { 0d, 180d, 360d, 540d }, bounds.Select(slot => slot.X).ToArray());
        CollectionAssert.AreEqual(new[] { 180d, 180d, 180d, 60d }, bounds.Select(slot => slot.Width).ToArray());
        return Task.CompletedTask;
    }, TimeSpan.FromSeconds(10));
}
