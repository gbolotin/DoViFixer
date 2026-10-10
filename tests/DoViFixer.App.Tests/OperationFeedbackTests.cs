using DoViFixer.App.Presentation.Application;
using DoViFixer.App.ViewModels;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WpfFoundation.Notifications;
using WpfFoundation.Operations;

namespace DoViFixer.App.Tests;

/// <summary>How operations are reported on the taskbar and in notifications.</summary>
[TestClass]
public sealed class OperationFeedbackTests
{
    private sealed class TestOperationViewModel(IOperationFeedback feedback) : OperationViewModel(feedback)
    {
        public Task ExecuteAsync(string? title, Func<CancellationToken, IProgress<OperationProgress>, Task> action) => RunAsync(title, action);

        public void FailItem() => ReportItemFailed();

        public void Pause(bool paused) => SetActivityPaused(paused);
    }

    private readonly RecordingFeedback feedback = new();

    [TestMethod]
    public async Task ANamedOperationReportsProgressAndSucceedsWithItsStatusText()
    {
        var model = new TestOperationViewModel(feedback);
        await model.ExecuteAsync("Conversion", async (_, progress) =>
        {
            progress.Report(new(Guid.NewGuid(), "Encoding", "movie.mkv", 25));
            for (int i = 0; i < 50 && model.Percent != 25; i++)
            {
                await Task.Delay(10);
            }

            model.Pause(true);
            model.Pause(false);
            model.SetStatus(ViewStatus.Result, "Conversion to DV8.1: 1 converted");
        });

        var activity = feedback.Activities.Single();
        Assert.AreEqual("Conversion", activity.Title);
        Assert.IsNull(activity.Reports[0], "The operation starts without a percentage.");
        CollectionAssert.Contains(activity.Reports, 0.25);
        CollectionAssert.AreEqual(new[] { true, false }, activity.Pauses);
        Assert.AreEqual((OperationOutcome.Succeeded, "Conversion to DV8.1: 1 converted"), activity.Completion);
    }

    [TestMethod]
    public async Task CancellationFailuresAndFailedItemsMapToTheirOutcomes()
    {
        var model = new TestOperationViewModel(feedback);
        await model.ExecuteAsync("Scan", (token, _) => throw new OperationCanceledException());
        await model.ExecuteAsync("Backup", (_, _) => throw new IOException("Disk full"));
        await model.ExecuteAsync("Conversion", (_, _) =>
        {
            model.FailItem();
            model.SetStatus(ViewStatus.Result, "Conversion to HDR10: 2 converted, 1 failed");
            return Task.CompletedTask;
        });
        await model.ExecuteAsync("Conversion", (_, _) =>
        {
            model.SetStatus(ViewStatus.Result, "Conversion to HDR10: 1 converted");
            return Task.CompletedTask;
        });

        CollectionAssert.AreEqual(
            new (OperationOutcome, string?)?[]
            {
                (OperationOutcome.Cancelled, "Cancelled; cleanup finished."),
                (OperationOutcome.Failed, "Disk full"),
                (OperationOutcome.CompletedWithErrors, "Conversion to HDR10: 2 converted, 1 failed"),
                (OperationOutcome.Succeeded, "Conversion to HDR10: 1 converted")
            },
            feedback.Activities.Select(activity => activity.Completion).ToArray(),
            "A failed item affects only the operation it happened in.");
    }

    [TestMethod]
    [DataRow(ViewStatus.ConversionNotStarted, OperationOutcome.Cancelled)]
    [DataRow(ViewStatus.BackupNotApproved, OperationOutcome.Cancelled)]
    [DataRow(ViewStatus.RestoreNotApproved, OperationOutcome.Cancelled)]
    [DataRow(ViewStatus.CleanupNotApproved, OperationOutcome.Cancelled)]
    [DataRow(ViewStatus.ToolsUnavailable, OperationOutcome.Failed)]
    [DataRow(ViewStatus.Error, OperationOutcome.Failed)]
    [DataRow(ViewStatus.NoBackupsFound, OperationOutcome.Succeeded)]
    public async Task AnOperationThatStopsEarlyEndsByItsStatus(ViewStatus status, OperationOutcome outcome)
    {
        var model = new TestOperationViewModel(feedback);
        await model.ExecuteAsync("Backup", (_, _) =>
        {
            model.SetStatus(status);
            return Task.CompletedTask;
        });

        Assert.AreEqual(outcome, feedback.Activities.Single().Completion?.Outcome);
    }

    [TestMethod]
    public async Task UnnamedOperationsAreNotReported()
    {
        var model = new TestOperationViewModel(feedback);
        await model.ExecuteAsync(null, (_, _) => Task.CompletedTask);
        Assert.IsEmpty(feedback.Activities);
    }

    [TestMethod]
    public async Task MediaBatchesReportTheirShareDone()
    {
        using var runtime = new TestRuntime();
        var media = runtime.Container.GetRequiredService<MediaViewModel>();
        await runtime.UpdateAsync(settings => settings with { AutomaticallyScanAddedFiles = false }, default);
        await media.AddAsync([@"C:\Media\Mountain.mkv", @"C:\Media\River.mkv"]);
        await media.ScanAllAsync();

        var scan = runtime.Feedback.Activities.Last();
        Assert.AreEqual("Scan", scan.Title);
        Assert.IsNotNull(scan.Completion);
        Assert.IsTrue(scan.Reports.Any(fraction => fraction is > 0 and <= 1), "A batch reports the share of files done.");
    }

    [TestMethod]
    public async Task NotificationsFollowTheSetting()
    {
        using var runtime = new TestRuntime();
        var sent = new RecordingNotifications();
        var notifications = new CompletionNotifications(
            new Lazy<INotificationService>(sent),
            runtime.Container.GetRequiredService<SettingsService>(),
            NullLogger<CompletionNotifications>.Instance);

        await runtime.UpdateAsync(settings => settings with { ShowCompletionNotifications = true }, default);
        await notifications.ShowIfEnabledAsync("Conversion", "1 converted", NotificationKind.Success);
        await runtime.UpdateAsync(settings => settings with { ShowCompletionNotifications = false }, default);
        await notifications.ShowIfEnabledAsync("Scan", "40 scanned", NotificationKind.Success);

        CollectionAssert.AreEqual(new[] { ("Conversion", "1 converted", NotificationKind.Success) }, sent.Sent);
        Assert.IsTrue(new UserSettings().ShowCompletionNotifications, "Notifications are on by default.");
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<(string Title, string Message, NotificationKind Kind)> Sent { get; } = [];

        public void Register(string appId, string displayName, string? iconPath)
        {
        }

        public void Unregister()
        {
        }

        public void Show(string title, string message, NotificationKind kind = NotificationKind.Information) => Sent.Add((title, message, kind));
    }
}
