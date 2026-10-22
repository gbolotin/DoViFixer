using DoViFixer.App.ViewModels;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class SelectionModeTests
{
    private const string Mountain = @"C:\Media\Mountain.mkv";
    private const string Ocean = @"C:\Media\Ocean.mkv";
    private const string City = @"C:\Media\City.mkv";

    private static string[] Selected(MediaViewModel model) => [.. model.Files.Where(row => row.IsSelected).Select(row => row.Path)];

    private static string[] Highlighted(MediaViewModel model) => [.. model.SelectedRows.Cast<MediaRow>().Select(row => row.Path)];

    [TestMethod]
    public void TheListSelectsLikeFileExplorerByDefault()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        Assert.IsFalse(new UserSettings().ShowSelectionCheckBoxes);
        Assert.IsFalse(model.ShowsCheckBoxes);
    }

    [TestMethod]
    public async Task HighlightedRowsAreTheSelectedFiles()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean]);
        CollectionAssert.AreEqual(new[] { Mountain, Ocean }, Highlighted(model));

        await model.AddAsync([City]);
        CollectionAssert.AreEqual(new[] { City }, Selected(model), "As in File Explorer, the files just added become the selection.");
        CollectionAssert.AreEqual(new[] { City }, Highlighted(model));

        model.SelectedRows = [model.Files[0], model.Files[1]];
        CollectionAssert.AreEqual(new[] { Mountain, Ocean }, Selected(model));
        Assert.AreEqual("3 items | 2 items selected", model.SelectionSummary);

        model.SelectedRows = [];
        Assert.IsFalse(model.ConvertCommand.CanExecute(null), "Convert needs a selected file.");
        Assert.IsFalse(model.SelectAllCommand.CanExecute(null), "Without check boxes, the list handles Ctrl+A itself.");
    }

    [TestMethod]
    public async Task RowsHighlightedDuringAnOperationAreSelectedWhenItEnds()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean]);
        using var discovering = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        runtime.DiscoverFiles = input =>
        {
            discovering.Release();
            release.Wait();
            return [input];
        };

        // Adding a file already listed keeps the operation running without changing the selection itself.
        var adding = model.AddAsync([Mountain]);
        await discovering.WaitAsync();
        model.SelectedRows = [model.Files[1]];
        CollectionAssert.AreEqual(new[] { Mountain, Ocean }, Selected(model), "Selecting rows while busy never skips a queued file.");

        release.Release();
        await adding;
        CollectionAssert.AreEqual(new[] { Ocean }, Selected(model));
    }

    [TestMethod]
    public async Task SwitchingToCheckBoxesKeepsTheSelectedFilesAndSavesTheChoice()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean]);
        model.SelectedRows = [model.Files[1]];

        model.ShowsCheckBoxes = true;
        await model.FlushSelectionModeAsync();
        Assert.IsTrue(runtime.Settings.ShowSelectionCheckBoxes);
        CollectionAssert.AreEqual(new[] { Ocean }, Selected(model), "The highlighted file stays checked.");

        model.SelectAllCommand.Invoke();
        CollectionAssert.AreEqual(new[] { Mountain, Ocean }, Selected(model), "Ctrl+A checks every shown file.");

        model.ShowsCheckBoxes = false;
        CollectionAssert.AreEqual(new[] { Mountain, Ocean }, Highlighted(model), "The checked files become the highlighted rows.");
        await model.FlushSelectionModeAsync();
        Assert.IsFalse(runtime.Settings.ShowSelectionCheckBoxes);
    }

    [TestMethod]
    public async Task TheSettingsPageAndTheViewMenuShareOneChoice()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();

        await shell.OpenSettingsCommand.InvokeAsync();
        Assert.IsFalse(shell.Settings.ShowSelectionCheckBoxes);
        shell.Settings.ShowSelectionCheckBoxes = true;
        await shell.Settings.SaveAsync();
        Assert.IsTrue(runtime.Settings.ShowSelectionCheckBoxes);
        Assert.IsTrue(shell.Media.ShowsCheckBoxes, "Saving the setting switches the list at once.");
        await shell.CloseSettingsCommand.InvokeAsync();

        shell.Media.ShowsCheckBoxes = false;
        await shell.Media.FlushSelectionModeAsync();
        await shell.OpenSettingsCommand.InvokeAsync();
        Assert.IsFalse(shell.Settings.ShowSelectionCheckBoxes, "Settings shows the choice made on the View menu.");
    }

    [TestMethod]
    public async Task DeleteRemovesTheHighlightedRowsOrWithCheckBoxesTheFocusedRow()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        await model.AddAsync([Mountain, Ocean, City]);

        model.SelectedRows = [model.Files[0], model.Files[2]];
        await model.RemoveListSelectionCommand.InvokeAsync();
        CollectionAssert.AreEqual(new[] { Ocean }, model.Files.Select(row => row.Path).ToArray());

        await model.AddAsync([Mountain]);
        model.ShowsCheckBoxes = true;
        model.Focused = model.Files[0];
        await model.RemoveListSelectionCommand.InvokeAsync();
        CollectionAssert.AreEqual(new[] { Mountain }, model.Files.Select(row => row.Path).ToArray(), "With check boxes, Delete removes the row with focus, checked or not.");
        await model.FlushSelectionModeAsync();
    }
}
