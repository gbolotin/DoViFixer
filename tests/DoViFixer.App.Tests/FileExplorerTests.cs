using DoViFixer.App.Startup;
using DoViFixer.App.ViewModels;
using DoViFixer.Application.FileExplorer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

/// <summary>Files and folders that File Explorer's right-click menu sends to the open window.</summary>
[TestClass]
public sealed class FileExplorerTests
{
    [TestMethod]
    public async Task OpenAddsTheFilesWithoutConverting()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        model.Open(new([@"C:\Media\Mountain.mkv", @"C:\Media\Notes.txt"], Convert: false));
        await model.FileExplorerCompletion;

        Assert.AreEqual(@"C:\Media\Mountain.mkv", model.Files.Single().Path, "Only files the app can add join the list.");
        Assert.IsNotNull(model.Files[0].Analysis, "Added files are scanned as Add files scans them.");
        Assert.AreEqual(0, runtime.Conversions);
    }

    [TestMethod]
    public async Task ConvertConvertsTheFilesConversionWouldNotSkip()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        model.Open(new([@"C:\Media\Mountain.mkv", @"C:\Media\Sdr.mkv"], Convert: true));
        await model.FileExplorerCompletion;

        Assert.AreEqual(1, runtime.Conversions, "A file without Dolby Vision is added but not converted.");
        Assert.IsTrue(model.Files.Single(row => row.Path == @"C:\Media\Mountain.mkv").Result!.Output!.EndsWith(" - DV P8.1.mkv", StringComparison.Ordinal));
        Assert.IsTrue(model.Files.Any(row => row.Path == @"C:\Media\Sdr.mkv"));
        Assert.AreEqual("Conversion", runtime.Feedback.Activities.Last().Title);
    }

    [TestMethod]
    public async Task ConvertSaysWhenNoFileCanBeConverted()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();

        model.Open(new([@"C:\Media\Sdr.mkv"], Convert: true));
        await model.FileExplorerCompletion;

        Assert.AreEqual(0, runtime.Conversions);
        Assert.AreEqual("Sdr.mkv was not converted. Its details say why.", model.StatusMessage);
    }

    [TestMethod]
    public async Task RequestsWaitWhileHeldAndFilesOfOneKindFormOneBatch()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        model.HoldAutomaticWork(true);

        // File Explorer starts the app once for each selected file.
        model.Open(new([@"C:\Media\Mountain.mkv"], Convert: true));
        model.Open(new([@"C:\Media\City.mkv"], Convert: false));
        model.Open(new([@"C:\Media\Mountain2.mkv"], Convert: true));
        Assert.AreEqual(0, model.Files.Count, "Requests wait while Settings is open.");

        model.HoldAutomaticWork(false);
        var conversion = model.FileExplorerCompletion;
        await conversion;
        await model.FileExplorerCompletion;

        Assert.AreEqual(2, runtime.Conversions);
        Assert.AreEqual(1, runtime.Feedback.Activities.Count(activity => activity.Title == "Conversion"), "Both files convert in one batch.");
        Assert.IsTrue(model.Files.Any(row => row.Path == @"C:\Media\City.mkv"), "The Open request runs after the conversion.");
    }

    [TestMethod]
    public async Task TheListShowsEveryFileSoTheSentFilesAreVisible()
    {
        using var runtime = new TestRuntime();
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        model.FileFilter = MediaFileFilter.Profile81;
        model.FilterText = "Ocean";

        model.Open(new([@"C:\Media\Mountain.mkv"], Convert: false));
        await model.FileExplorerCompletion;

        Assert.AreEqual(MediaFileFilter.All, model.FileFilter);
        Assert.AreEqual("", model.FilterText);
    }

    [TestMethod]
    public async Task SettingsClosesSoTheMediaPageShowsTheFiles()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        await shell.OpenSettingsCommand.InvokeAsync();

        await shell.OpenFromFileExplorerAsync(new([@"C:\Media\Mountain.mkv"], Convert: false));
        await shell.Media.FileExplorerCompletion;

        Assert.IsFalse(shell.IsSettingsOpen);
        Assert.AreEqual(1, shell.Media.Files.Count);
    }

    [TestMethod]
    public async Task ALaterStartSendsItsCommandLineToTheFirstInstance()
    {
        string name = $"DoViFixer.Tests.{Guid.NewGuid():N}";
        using var first = await SingleInstance.StartAsync(name, [], TimeSpan.FromSeconds(10));
        Assert.IsNotNull(first, "The first start keeps running.");
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Listen(arguments => received.TrySetResult(arguments));

        var later = await StartOnOwnThreadAsync(name, ["--convert", @"C:\Media\Mountain Ä.mkv"]);

        Assert.IsNull(later, "A later start hands over and exits.");
        CollectionAssert.AreEqual(new[] { "--convert", @"C:\Media\Mountain Ä.mkv" }, (await received.Task.WaitAsync(TimeSpan.FromSeconds(10))).ToArray());
    }

    [TestMethod]
    public async Task AStartRunsOnItsOwnWhenTheFirstInstanceDoesNotAnswer()
    {
        string name = $"DoViFixer.Tests.{Guid.NewGuid():N}";
        using var first = await SingleInstance.StartAsync(name, [], TimeSpan.FromSeconds(10));

        // The first instance has not started listening, as if it hung while starting.
        using var later = await StartOnOwnThreadAsync(name, [@"C:\Media\Mountain.mkv"], TimeSpan.FromMilliseconds(200));

        Assert.IsNotNull(later);
    }

    // A mutex belongs to the thread that took it, which here stays the test's. A later start runs on a thread of its own,
    // as it would in its own process.
    private static Task<SingleInstance?> StartOnOwnThreadAsync(string name, IReadOnlyList<string> arguments, TimeSpan? timeout = null) =>
        Task.Factory.StartNew(() => SingleInstance.StartAsync(name, arguments, timeout ?? TimeSpan.FromSeconds(10)), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
}
