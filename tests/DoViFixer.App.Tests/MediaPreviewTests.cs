using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;
using DoViFixer.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class MediaPreviewTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AddAndRescanGenerateAllPreviewsWithoutKeepingTheInterfaceBusy(bool automaticScan)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = automaticScan }, default);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var model = shell.Pages.OfType<MediaViewModel>().Single();
        string[] paths = [@"C:\Media\Mountain.mkv", @"C:\Media\Broken.mkv", @"C:\Media\Ocean.mkv"];
        byte[] image = CreateImage();
        foreach (bool rescan in new[] { false, true })
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var visited = new ConcurrentDictionary<string, byte>();
            runtime.Preview = async (path, token) =>
            {
                started.TrySetResult();
                await release.Task.WaitAsync(token);
                visited.TryAdd(path, 0);
                if (visited.Count == paths.Length)
                {
                    finished.TrySetResult();
                }
                if (path == paths[1])
                {
                    throw new IOException("One failed preview must not stop the remaining files.");
                }
                return image;
            };

            await (rescan ? model.ScanCommand.InvokeAsync() : model.AddAsync(paths)).WaitAsync(TimeSpan.FromSeconds(5));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(model.IsBusy, "Preview generation must not extend the scan/add busy state.");
            Assert.IsTrue(shell.CanNavigate);
            Assert.IsTrue(model.AddFilesCommand.CanExecute(null));
            Assert.IsTrue(model.ClearAllCommand.CanExecute(null));
            Assert.IsTrue(model.ScanCommand.CanExecute(null));
            release.SetResult();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await model.CancelPreviewsAsync().WaitAsync(TimeSpan.FromSeconds(5));
            CollectionAssert.AreEquivalent(paths, visited.Keys.ToArray());
            foreach (var row in model.Files)
            {
                row.IsSelected = false;
            }
        }
    }

    [TestMethod]
    [DataRow("files")]
    [DataRow("cache")]
    [DataRow("shutdown")]
    public async Task ClearingOrClosingCancelsBackgroundPreviews(string action)
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        var model = shell.Pages.OfType<MediaViewModel>().Single();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new ConcurrentBag<CancellationToken>();
        runtime.Preview = async (path, token) =>
        {
            tokens.Add(token);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return [];
        };
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\Ocean.mkv"]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (action == "files")
        {
            model.ClearAllCommand.Invoke();
        }
        else if (action == "cache")
        {
            await shell.Settings.ClearCacheCommand.InvokeAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            await shell.CancelAndWaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.IsTrue(tokens.All(token => token.IsCancellationRequested));
        await model.CancelPreviewsAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsNull(model.FramePreview);
    }

    [TestMethod]
    public async Task FocusChangesCancelOldPreviewsAndClearImagesOnFailureOrDeselection()
    {
        using var runtime = new TestRuntime();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] image = CreateImage();
        runtime.Preview = async (path, token) =>
        {
            if (path == "first.mkv")
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.SetResult(token);
                await Task.Delay(Timeout.Infinite, token);
            }
            if (path == "broken.mkv")
            {
                throw new IOException("Cannot decode video.");
            }
            return image;
        };
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        model.Focused = new MediaRow("first.mkv");
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(model.PreviewStatus) && model.FramePreview is not null && model.PreviewStatus == "")
            {
                loaded.TrySetResult();
            }
            if (e.PropertyName == nameof(model.PreviewStatus) && model.PreviewStatus.StartsWith("Frame preview unavailable"))
            {
                failed.TrySetResult();
            }
        };
        model.Focused = new MediaRow("second.mkv");
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsTrue(model.FramePreview!.IsFrozen);
        Assert.AreEqual(2, model.FramePreview.PixelWidth);
        model.Focused = new MediaRow("broken.mkv");
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsNull(model.FramePreview);
        model.Focused = null;
        Assert.AreEqual("", model.PreviewStatus);
        Assert.IsNull(model.FramePreview);
    }

    internal static byte[] CreateImage()
    {
        var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Rgb24, null, new byte[] { 25, 110, 190, 240, 160, 40 }, 6);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
