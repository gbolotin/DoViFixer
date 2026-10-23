using DoViFixer.App.ViewModels;
using DoViFixer.Application.Activity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

/// <summary>The status bar's Activity flyout and the activity in a file's details.</summary>
[TestClass]
public sealed class ActivityTests
{
    [TestMethod]
    public async Task InspectingFilesShowsEachFileAndTheBatchAndTheFocusedFilesOwnEntries()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(value => value with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        var activity = runtime.Container.GetRequiredService<ActivityViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\River.mkv"]);
        foreach (var row in model.Files)
        {
            row.IsSelected = true;
        }

        await model.InspectCommand.InvokeAsync();

        CollectionAssert.AreEqual(new[] { "Inspected 2 files", "Inspected River.mkv", "Inspected Mountain.mkv" },
            activity.Entries.Select(entry => entry.Title.Split(" in ")[0]).ToArray());
        Assert.IsFalse(activity.Entries.Any(entry => entry.IsRunning));
        // The status bar and the notification read the same as the batch summary.
        StringAssert.StartsWith(model.StatusMessage, "Inspected 2 files · took ");
        Assert.AreEqual(activity.Entries[0].Text, model.StatusMessage);
        Assert.AreEqual((WpfFoundation.Operations.OperationOutcome.Succeeded, model.StatusMessage), runtime.Feedback.Activities.Last().Completion);
        model.Focused = model.Files.Single(row => row.Name == "River.mkv");
        Assert.IsTrue(model.HasFocusedActivity);
        StringAssert.StartsWith(model.FocusedActivity.Single().Title, "Inspected River.mkv");
    }

    [TestMethod]
    public async Task AOneFileOperationEndsWithThatFilesEntryInTheStatusAndNotification()
    {
        using var runtime = new TestRuntime();
        await runtime.UpdateAsync(value => value with { AutomaticallyScanAddedFiles = false }, default);
        var model = runtime.Container.GetRequiredService<MediaViewModel>();
        var activity = runtime.Container.GetRequiredService<ActivityViewModel>();
        await model.AddAsync([@"C:\Media\Mountain.mkv"]);

        await model.InspectRowCommand.InvokeAsync(model.Files.Single());

        var entry = activity.Entries.Single();
        StringAssert.StartsWith(entry.Title, "Inspected Mountain.mkv in ");
        Assert.AreEqual(entry.Text, model.StatusMessage);
        Assert.AreEqual(entry.Text, runtime.Feedback.Activities.Last().Completion?.Summary);
    }

    [TestMethod]
    public void EntriesReportedAtOnceWithoutAUiThreadAreAllKept()
    {
        // Tests have no UI thread, so batch workers report on their own threads at the same time.
        using var runtime = new TestRuntime();
        var feed = new ActivityFeed(TimeProvider.System);
        using var activity = new ActivityViewModel(feed, runtime, runtime);

        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            feed.Report(new(Guid.NewGuid(), i % 2 == 0 ? ActivitySeverity.Warning : ActivitySeverity.Success, $"File {i}")));

        Assert.HasCount(200, activity.Entries);
        Assert.HasCount(200, activity.Entries.Select(entry => entry.Id).Distinct());
        Assert.AreEqual(100, activity.UnseenProblems);
    }

    [TestMethod]
    public void ProblemsCountUntilTheFlyoutOpensAndEntriesStayInPlace()
    {
        using var runtime = new TestRuntime();
        var feed = new ActivityFeed(TimeProvider.System);
        using var activity = new ActivityViewModel(feed, runtime, runtime);
        var id = Guid.NewGuid();

        feed.Report(new(id, ActivitySeverity.Info, "Converting Mountain.mkv…") { IsRunning = true });
        feed.Report(new(id, ActivitySeverity.Error, "Couldn't convert Mountain.mkv", "Original restored.", @"C:\Media\Mountain.mkv", @"C:\Media\Mountain.mkv"));
        feed.Report(new(Guid.NewGuid(), ActivitySeverity.Warning, "Converted River.mkv, with a problem"));

        Assert.AreEqual(2, activity.Entries.Count);
        Assert.AreEqual("Converted River.mkv, with a problem", activity.Entries[0].Title);
        Assert.AreEqual(2, activity.UnseenProblems);
        Assert.AreEqual("2 new problems. Show what happened this session", activity.ToolTip);

        activity.OpenCommand.Invoke();
        Assert.IsFalse(activity.HasUnseenProblems);
        activity.CopyCommand.Invoke(activity.Entries[1]);
        Assert.AreEqual($"Couldn't convert Mountain.mkv{Environment.NewLine}Original restored.", runtime.ClipboardText);
    }

    [TestMethod]
    public void TheActivityButtonComesFirstInTheStatusBar()
    {
        using var runtime = new TestRuntime();
        var shell = runtime.Container.GetRequiredService<ShellViewModel>();
        Assert.AreSame(runtime.Container.GetRequiredService<ActivityViewModel>(), shell.StatusItems[0]);
    }
}
