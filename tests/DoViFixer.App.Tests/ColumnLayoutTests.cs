using System.ComponentModel;
using System.Text.Json;
using DoViFixer.App.Presentation.Application;
using DoViFixer.App.ViewModels;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WpfFoundation.Behaviors;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class ColumnLayoutTests
{
    [TestMethod]
    public async Task LayoutAndSortRoundTripThroughSettingsAndResetWithoutChangingPreferences()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.OnActivatedAsync(default);
        bool includeSimple = runtime.Settings.IncludeSimple;
        model.ColumnLayout = new[] { new GridViewColumnLayout("Result", 180), new GridViewColumnLayout("Name", 260), new GridViewColumnLayout("Actions", null) };
        model.FileSort.Restore(MediaSortColumn.Status, ListSortDirection.Descending);
        await model.FlushColumnLayoutAsync();
        var saved = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(runtime.Settings))!;
        Assert.AreEqual(includeSimple, saved.IncludeSimple);
        Assert.AreEqual("Status", saved.MediaListLayout!.SortColumn);

        using var reopenedRuntime = new TestRuntime();
        await reopenedRuntime.UpdateAsync(_ => saved, default);
        var reopened = reopenedRuntime.Container.GetRequiredService<MediaViewModel>();
        await reopened.OnActivatedAsync(default);
        CollectionAssert.AreEqual(new[] { "Result", "Name", "Actions" }, reopened.ColumnLayout!.Select(column => column.Id).ToArray());
        Assert.AreEqual(260, reopened.ColumnLayout![1].Width);
        Assert.AreEqual(MediaSortColumn.Status, reopened.FileSort.Column);
        Assert.AreEqual(ListSortDirection.Descending, reopened.FileSort.Direction);
        reopened.ResetColumnLayoutCommand.Invoke();
        await reopened.FlushColumnLayoutAsync();
        Assert.IsNull(reopened.ColumnLayout);
        Assert.IsNull(reopened.FileSort.Column);
        Assert.IsNull(reopenedRuntime.Settings.MediaListLayout!.SortColumn);
        Assert.IsTrue(reopenedRuntime.Settings.MediaListLayout.Columns.IsEmpty);
        await model.CancelPreviewsAsync();
        await reopened.CancelPreviewsAsync();
    }

    [TestMethod]
    public async Task UnknownSavedSortIsIgnoredAndAResizeBurstSavesTheLatestWidth()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { MediaListLayout = new([], "DeletedColumn", true) }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.OnActivatedAsync(default);
        Assert.IsNull(model.FileSort.Column);
        for (int width = 120; width <= 240; width += 4)
        {
            model.ColumnLayout = new[] { new GridViewColumnLayout("Name", width) };
        }
        await model.FlushColumnLayoutAsync();
        Assert.AreEqual(240, runtime.Settings.MediaListLayout!.Columns.Single().Width);
        await model.CancelPreviewsAsync();
    }
}
