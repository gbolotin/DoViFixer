using DoViFixer.App.Presentation;
using DoViFixer.Application.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class OperationViewModelTests
{
    private sealed class TestOperationViewModel : OperationViewModel
    {
        public Task ExecuteAsync(Func<CancellationToken, IProgress<OperationProgress>, Task> action) => RunAsync(action);
    }

    [TestMethod]
    public async Task RunAsyncUpdatesProgressAndNotifiesProperties()
    {
        var model = new TestOperationViewModel();
        var changed = new List<string>();
        model.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);

        Assert.IsFalse(model.IsBusy);
        Assert.IsTrue(model.IsIdle);
        Assert.AreEqual(0, model.Percent);
        Assert.IsFalse(model.IsIndeterminate);
        Assert.AreEqual("", model.Stage);
        Assert.AreEqual("Ready", model.Status);

        TaskCompletionSource step1 = new();
        TaskCompletionSource step2 = new();

        var task = model.ExecuteAsync(async (token, progress) =>
        {
            progress.Report(new(Guid.NewGuid(), "Demuxing", "movie.mkv", 25));
            await step1.Task;
            progress.Report(new(Guid.NewGuid(), "Encoding", null, null));
            await step2.Task;
        });

        // Let the Progress<T> callback dispatch
        await Task.Yield();
        for (int i = 0; i < 50 && model.Percent != 25; i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(model.IsBusy);
        Assert.AreEqual(25, model.Percent);
        Assert.AreEqual(25, model.Progress.Percent);
        Assert.AreEqual("Demuxing", model.Stage);
        Assert.AreEqual("Demuxing", model.Progress.Stage);
        Assert.AreEqual("Demuxing  movie.mkv", model.Status);
        Assert.IsFalse(model.IsIndeterminate);
        CollectionAssert.Contains(changed, nameof(model.Percent));
        CollectionAssert.Contains(changed, nameof(model.Stage));

        step1.SetResult();

        for (int i = 0; i < 50 && !model.IsIndeterminate; i++)
        {
            await Task.Delay(10);
        }

        Assert.AreEqual("Encoding", model.Stage);
        Assert.AreEqual("Encoding", model.Status);
        Assert.IsTrue(model.IsIndeterminate);

        step2.SetResult();
        await task;

        Assert.IsFalse(model.IsBusy);
        Assert.IsTrue(model.IsIdle);
        Assert.IsFalse(model.IsIndeterminate);
    }

    [TestMethod]
    public async Task RunAsyncHandlesCancellationGracefully()
    {
        var model = new TestOperationViewModel();
        TaskCompletionSource started = new();

        var task = model.ExecuteAsync(async (token, progress) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });

        await started.Task;
        Assert.IsTrue(model.IsBusy);
        Assert.IsTrue(model.CancelCommand.CanExecute());

        model.CancelCommand.Execute();
        await task;

        Assert.IsFalse(model.IsBusy);
        Assert.AreEqual("Cancelled; cleanup finished.", model.Status);
        Assert.IsFalse(model.Progress.IsRunning);
    }
}
