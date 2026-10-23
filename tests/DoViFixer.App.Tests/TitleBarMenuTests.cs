using DoViFixer.App.Dialogs;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Presentation.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class TitleBarMenuTests
{
    [TestMethod]
    public async Task AddingFilesFromTheMenuListsThemOnMedia()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        CollectionAssert.AreEqual(new[] { shell.Media }, shell.Pages.ToArray(), "Media is the only page.");
        Assert.IsFalse(shell.ExportResultsCommand.CanExecute(null), "There are no results to export yet.");

        runtime.PickedFiles = [@"C:\Media\Mountain.mkv"];
        await shell.AddFilesCommand.InvokeAsync();

        Assert.AreSame(shell.Media, shell.CurrentPage);
        var added = shell.Media.Files.Single();
        Assert.AreEqual(@"C:\Media\Mountain.mkv", added.Path);

        added.IsSelected = false;
        Assert.IsFalse(shell.ExportResultsCommand.CanExecute(null), "Export results needs a checked file.");
        added.IsSelected = true;
        Assert.IsTrue(shell.ExportResultsCommand.CanExecute(null), "The checked file can be exported.");
    }

    [TestMethod]
    public async Task FileMenuActsOnTheCheckedFiles()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        Assert.IsFalse(shell.RescanCommand.CanExecute(null), "There is nothing to rescan yet.");
        runtime.PickedFiles = [@"C:\Media\Mountain.mkv"];
        await shell.AddFilesCommand.InvokeAsync();
        var row = shell.Media.Files.Single();

        // Cached results would answer a rescan without analyzing again.
        await runtime.UpdateAsync(settings => settings with { UseCachedResults = false }, default);
        int analyses = runtime.Analyses;
        await shell.RescanCommand.InvokeAsync();
        Assert.IsGreaterThan(analyses, runtime.Analyses, "Rescan analyzes the listed files again.");

        IRelayCommand[] selectionCommands = [shell.InspectCommand, shell.DeepInspectCommand, shell.ConvertCommand, shell.RemoveSelectedCommand];
        row.IsSelected = false;
        Assert.IsTrue(selectionCommands.All(command => !command.CanExecute(null)), "Selection commands need a checked file.");
        Assert.IsTrue(shell.RescanCommand.CanExecute(null), "Rescan covers every listed file, checked or not.");
        row.IsSelected = true;
        Assert.IsTrue(selectionCommands.All(command => command.CanExecute(null)));
        Assert.IsFalse(shell.RestoreSelectedCommand.CanExecute(null), "A Profile 7 file has no archive to restore from.");

        int fullAnalyses = runtime.FullAnalyses;
        await shell.InspectCommand.InvokeAsync();
        Assert.AreEqual(fullAnalyses + 1, runtime.FullAnalyses);

        await shell.RemoveSelectedCommand.InvokeAsync();
        Assert.IsEmpty(shell.Media.Files);
        Assert.IsFalse(shell.RescanCommand.CanExecute(null));
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
        Assert.IsFalse(shell.CleanupBackupsCommand.CanExecute(null));
        Assert.IsFalse(shell.RescanCommand.CanExecute(null));
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

    [TestMethod]
    public async Task HelpOpensTheOnlineDocumentationEvenOverSettings()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();

        shell.OpenDocumentationCommand.Invoke();
        Assert.AreEqual(DefaultBrowser.DocumentationAddress, runtime.OpenedAddress);

        await shell.ShowAboutCommand.InvokeAsync();
        Assert.IsTrue(shell.OpenDocumentationCommand.CanExecute(null), "F1 still works while the Settings frame is open.");
    }
}
