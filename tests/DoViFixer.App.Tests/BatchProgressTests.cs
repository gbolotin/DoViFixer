using DoViFixer.App.ViewModels;
using DoViFixer.Application.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class BatchProgressTests
{
    [TestMethod]
    public void BatchCountsFinishedFilesSeparatelyFromStageProgressAndResetsForNextBatch()
    {
        var previous = SynchronizationContext.Current;
        var context = new QueuedContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var model = new BatchProgressViewModel();
            model.Begin(4, "Conversion");
            model.Complete();
            var row = new MediaRow(@"C:\Media\Ocean.mkv");
            var progress = model.Start(row, "Extracting video");
            progress.Report(new(Guid.NewGuid(), "Extracting video", row.Path, 75));
            context.Drain();
            Assert.AreEqual(25, model.Percent);
            Assert.AreEqual("1 of 4 files processed", model.Summary);
            Assert.AreEqual(75, model.StagePercent);
            Assert.AreSame(row, model.CurrentJob);
            Assert.IsFalse(model.IsIndeterminate);

            progress.Report(new(Guid.NewGuid(), "Converting metadata", row.Path));
            context.Drain();
            Assert.IsTrue(model.IsIndeterminate);
            Assert.AreEqual("Working…", model.StageProgressText);
            Assert.AreEqual(25, model.Percent);

            model.Complete();
            model.Complete();
            model.Complete();
            Assert.AreEqual(100, model.Percent);
            model.End();
            Assert.IsFalse(model.IsRunning);
            Assert.IsFalse(model.IsIndeterminate);
            Assert.IsNull(model.CurrentJob);
            model.Begin(2, "Scan");
            Assert.AreEqual(0, model.Percent);
            Assert.AreEqual("0 of 2 files processed", model.Summary);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [TestMethod]
    public void LateReportsCannotOverwriteNextFileOrRetryOfSameFile()
    {
        var previous = SynchronizationContext.Current;
        var context = new QueuedContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var model = new BatchProgressViewModel();
            var first = new MediaRow("first.mkv");
            model.Begin(2, "Scan");
            var oldProgress = model.Start(first, "Scanning");
            oldProgress.Report(new(Guid.Empty, "Old sample", first.Path, 90));
            model.Complete();
            var second = new MediaRow("second.mkv");
            model.Start(second, "Inspecting");
            context.Drain();
            Assert.AreSame(second, model.CurrentJob);
            Assert.AreEqual("Inspecting", model.Stage);
            Assert.AreEqual(50, model.Percent);

            model.End();
            model.Begin(1, "Scan");
            model.Start(first, "Retrying");
            oldProgress.Report(new(Guid.Empty, "Old sample", first.Path, 100));
            context.Drain();
            Assert.AreEqual("Retrying", model.Stage);
            Assert.IsTrue(model.IsIndeterminate);
            Assert.AreEqual(0, model.Percent);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<Action> callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue(() => callback(state));
        public void Drain()
        {
            while (callbacks.TryDequeue(out var callback))
            {
                callback();
            }
        }
    }
}
