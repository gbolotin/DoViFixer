using DoViFixer.App.ViewModels;
using DoViFixer.App.Presentation.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class TitleBarMenuTests
{
    [TestMethod]
    public async Task AddingFilesFromAnotherPageShowsMediaWithTheFiles()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var archive = shell.Pages.OfType<ArchiveViewModel>().Single();
        await shell.ShowPageCommand.InvokeAsync(archive);
        Assert.AreSame(archive, shell.CurrentPage);
        Assert.IsFalse(shell.ShowPageCommand.CanExecute(archive), "The current page is not offered again.");
        Assert.IsFalse(shell.ExportResultsCommand.CanExecute(null), "There are no results to export yet.");

        runtime.PickedFiles = [@"C:\Media\Mountain.mkv"];
        await shell.AddFilesCommand.InvokeAsync();

        Assert.AreSame(shell.Media, shell.CurrentPage, "The menu shows Media, where the added files are.");
        var added = shell.Media.Files.Single();
        Assert.AreEqual(@"C:\Media\Mountain.mkv", added.Path);

        added.IsSelected = false;
        Assert.IsFalse(shell.ExportResultsCommand.CanExecute(null), "Export results needs a checked file.");
        added.IsSelected = true;
        Assert.IsTrue(shell.ExportResultsCommand.CanExecute(null), "The checked file can be exported.");
    }

    [TestMethod]
    public async Task SettingsCommandsOpenTheSectionThatShowsTheirResult()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();

        await shell.CheckDependenciesCommand.InvokeAsync();
        Assert.IsTrue(shell.IsSettingsOpen);
        Assert.IsInstanceOfType<NativeToolsSettingsSection>(shell.Settings.Sections.CurrentPage);
        Assert.AreEqual(ViewStatus.ToolsReady, shell.Settings.Status, "The check ran and reports in the frame.");

        Assert.IsFalse(shell.AddFilesCommand.CanExecute(null), "The pages behind the frame take no commands.");
        Assert.IsFalse(shell.ShowPageCommand.CanExecute(shell.Pages.OfType<ArchiveViewModel>().Single()));
        Assert.IsFalse(shell.ShowAboutCommand.CanExecute(null), "The frame is already open.");

        await shell.CloseSettingsCommand.InvokeAsync();
        Assert.IsTrue(shell.AddFilesCommand.CanExecute(null));
        await shell.ShowAboutCommand.InvokeAsync();
        Assert.IsInstanceOfType<AboutSettingsSection>(shell.Settings.Sections.CurrentPage);

        await shell.CloseSettingsCommand.InvokeAsync();
        await shell.CleanLibraryCommand.InvokeAsync();
        Assert.IsInstanceOfType<PerformanceSettingsSection>(shell.Settings.Sections.CurrentPage);
        Assert.AreEqual(ViewStatus.Result, shell.Settings.Status);
    }
}
