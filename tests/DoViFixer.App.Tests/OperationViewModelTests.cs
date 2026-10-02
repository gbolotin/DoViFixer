using DoViFixer.App.Presentation.Application;
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
        Assert.AreEqual(ViewStatus.Ready, model.Status);
        Assert.AreEqual("Ready", model.StatusText);
        Assert.AreEqual("", model.StatusMessage);

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
        Assert.AreEqual(ViewStatus.Progress, model.Status);
        Assert.AreEqual("Demuxing  movie.mkv", model.StatusMessage);
        Assert.IsFalse(model.IsIndeterminate);
        CollectionAssert.Contains(changed, nameof(model.Percent));
        CollectionAssert.Contains(changed, nameof(model.Stage));

        step1.SetResult();

        for (int i = 0; i < 50 && !model.IsIndeterminate; i++)
        {
            await Task.Delay(10);
        }

        Assert.AreEqual("Encoding", model.Stage);
        Assert.AreEqual(ViewStatus.Progress, model.Status);
        Assert.AreEqual("Encoding", model.StatusMessage);
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
        Assert.IsTrue(model.CancelCommand.CanExecute(null));

        model.CancelCommand.Invoke();
        await task;

        Assert.IsFalse(model.IsBusy);
        Assert.AreEqual(ViewStatus.Cancelled, model.Status);
        Assert.AreEqual("Cancelled; cleanup finished.", model.StatusText);
        Assert.AreEqual("", model.StatusMessage);
        Assert.IsFalse(model.Progress.IsRunning);
    }

    [TestMethod]
    public async Task ErrorsPreserveDetailsAndTypedStatusChangesClearOldMessages()
    {
        var model = new TestOperationViewModel();
        await model.ExecuteAsync((_, _) => throw new IOException("Folder unavailable"));
        Assert.AreEqual(ViewStatus.Error, model.Status);
        Assert.AreEqual("Folder unavailable", model.StatusMessage);
        Assert.AreEqual("Folder unavailable", model.StatusText);

        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) =>
        {
            notifications.Add(e.PropertyName);
            Assert.AreEqual(ViewStatus.FileListCleared, model.Status);
            Assert.AreEqual("", model.StatusMessage, "Observers must see the new state and message together.");
            Assert.AreEqual("File list cleared.", model.StatusText);
        };
        model.SetStatus(ViewStatus.FileListCleared);
        CollectionAssert.AreEqual(new[] { "File list cleared." }, model.StatusItems.Select(item => item.Text).ToArray());
        CollectionAssert.AreEquivalent(new[] { nameof(model.Status), nameof(model.StatusMessage), nameof(model.StatusText), nameof(model.StatusItems) }, notifications);
    }

    [TestMethod]
    public void StatusTextNotifiesWhenOnlyTheMessageChanges()
    {
        var model = new TestOperationViewModel();
        model.SetStatus(ViewStatus.Progress, "Scanning first file");
        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        model.SetStatus(ViewStatus.Progress, "Scanning second file");
        Assert.AreEqual("Scanning second file", model.StatusText);
        CollectionAssert.AreEqual(new[] { "Scanning second file" }, model.StatusItems.Select(item => item.Text).ToArray());
        CollectionAssert.AreEquivalent(new[] { nameof(model.StatusMessage), nameof(model.StatusText), nameof(model.StatusItems) }, notifications);

        notifications.Clear();
        model.SetStatus(ViewStatus.Progress, "Scanning second file");
        Assert.IsEmpty(notifications);
    }
}
