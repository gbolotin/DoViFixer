using DoViFixer.App.ViewModels;
using DoViFixer.Application.Operations;
using WpfFoundation.Operations;

namespace DoViFixer.App.Presentation.Application;
public abstract partial class OperationViewModel : ObservableObject
{
    private static readonly Dictionary<ViewStatus, string> statusTexts = new()
    {
        [ViewStatus.Ready] = "Ready",
        [ViewStatus.FileListCleared] = "File list cleared.",
        [ViewStatus.Cancelled] = "Cancelled; cleanup finished.",
        [ViewStatus.ToolsUnavailable] = "Required tools are unavailable.",
        [ViewStatus.BackupNotApproved] = "Backup not approved.",
        [ViewStatus.RestoreNotApproved] = "Restore not approved.",
        [ViewStatus.ConversionNotStarted] = "Conversion cancelled; the existing file was kept.",
        [ViewStatus.CleanupNotApproved] = "Cleanup not approved.",
        [ViewStatus.NoBackupsFound] = "No retained backup files found.",
        [ViewStatus.SettingsLoaded] = "Settings loaded.",
        [ViewStatus.SettingsSaved] = "Settings saved.",
        [ViewStatus.SettingsDiscarded] = "Unsaved changes discarded. Using the last saved settings.",
        [ViewStatus.ToolsReady] = "All tools ready.",
        [ViewStatus.ToolsNeedAttention] = "Tools need attention. Set a validated path or open dependency setup.",
        [ViewStatus.DependencySetupIncomplete] = "Dependency setup incomplete.",
        [ViewStatus.ToolPathSaved] = "Validated tool path saved and applied.",
        [ViewStatus.ToolOverrideReset] = "Tool override reset; tools checked again."
    };

    private readonly IOperationFeedback? feedback;
    private IOperationActivity? activity;
    private bool itemsFailed;
    private CancellationTokenSource? cancellation;
    private ViewStatus status = ViewStatus.Ready;
    private string statusMessage = "";

    public ProgressViewModel Progress { get; } = new();

    /// <param name="feedback">Reports named operations on the taskbar and in notifications; <see langword="null"/> for none.</param>
    protected OperationViewModel(IOperationFeedback? feedback = null)
    {
        this.feedback = feedback;
        CancelCommand = new(() => cancellation?.Cancel(), () => IsBusy);
        Progress.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProgressViewModel.Percent) or nameof(ProgressViewModel.StagePercent))
            {
                OnPropertyChanged(nameof(Percent));
                ReportActivityProgress();
            }

            if (e.PropertyName is nameof(ProgressViewModel.IsIndeterminate))
            {
                OnPropertyChanged(nameof(IsIndeterminate));
                ReportActivityProgress();
            }

            if (e.PropertyName is nameof(ProgressViewModel.Stage))
            {
                OnPropertyChanged(nameof(Stage));
            }
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    partial void OnIsBusyChanged(bool value) => CommandsChanged();

    public bool IsIdle => !IsBusy;
    public ViewStatus Status => status;
    public string StatusMessage => statusMessage;
    public string StatusText => statusTexts.TryGetValue(Status, out string? text) ? text : StatusMessage;
    public virtual IReadOnlyList<StatusItem> StatusItems => [new(StatusText)];

    public void SetStatus(ViewStatus value, string message = "")
    {
        bool statusChanged = status != value;
        bool messageChanged = statusMessage != message;
        status = value;
        statusMessage = message;
        if (statusChanged)
        {
            OnPropertyChanged(nameof(Status));
        }
        if (messageChanged)
        {
            OnPropertyChanged(nameof(StatusMessage));
        }
        if (statusChanged || messageChanged)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusItems));
        }
    }

    public string Stage => Progress.Stage;
    public double Percent => Progress.Percent;
    public bool IsIndeterminate => Progress.IsIndeterminate;

    public RelayCommand CancelCommand
    {
        get;
    }
    public Task Completion
    {
        get;
        private set;
    }
    = Task.CompletedTask;

    protected virtual void CommandsChanged()
    {
    }

    protected virtual void OnProgress(OperationProgress progress)
    {
    }

    /// <summary>Runs a short operation, such as loading or saving settings, without taskbar progress or a notification.</summary>
    protected Task RunAsync(Func<CancellationToken, IProgress<OperationProgress>, Task> action) => RunAsync(null, action);

    /// <summary>
    /// Runs a long operation and reports it outside the window under <paramref name="activityTitle"/>: progress on the
    /// taskbar button and, when it ends while the window is not active, a notification with the final status text.
    /// </summary>
    protected Task RunAsync(string? activityTitle, Func<CancellationToken, IProgress<OperationProgress>, Task> action)
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        Completion = RunCoreAsync(activityTitle, action);
        return Completion;
    }

    /// <summary>The fraction of the running operation done, from 0 to 1, or <see langword="null"/> while it is unknown.</summary>
    protected virtual double? ActivityProgress => Progress.IsIndeterminate ? null : Progress.Percent / 100;

    /// <summary>Sends <see cref="ActivityProgress"/> to the taskbar.</summary>
    protected void ReportActivityProgress() =>
        activity?.Report(ActivityProgress is { } fraction ? Math.Clamp(fraction, 0, 1) : null);

    /// <summary>Shows the running operation as paused or running again on the taskbar.</summary>
    protected void SetActivityPaused(bool paused) => activity?.SetPaused(paused);

    /// <summary>Records that an item of the running batch failed, so the operation ends as finished with errors.</summary>
    protected void ReportItemFailed() => itemsFailed = true;

    // Statuses an operation ends with when it stopped before doing its work.
    private static OperationOutcome OutcomeOf(ViewStatus status) => status switch
    {
        ViewStatus.ConversionNotStarted or ViewStatus.BackupNotApproved or ViewStatus.RestoreNotApproved or ViewStatus.CleanupNotApproved => OperationOutcome.Cancelled,
        ViewStatus.Error or ViewStatus.ToolsUnavailable or ViewStatus.DependencySetupIncomplete => OperationOutcome.Failed,
        _ => OperationOutcome.Succeeded
    };

    private async Task RunCoreAsync(string? activityTitle, Func<CancellationToken, IProgress<OperationProgress>, Task> action)
    {
        using var source = new CancellationTokenSource();
        cancellation = source;
        IsBusy = true;
        itemsFailed = false;
        activity = activityTitle is null ? null : feedback?.Start(activityTitle);
        var outcome = OperationOutcome.Succeeded;
        Progress.Start("Starting…");
        // Callback lifetime is bounded; queued progress cannot overwrite a later operation.
        var progress = new Progress<OperationProgress>(p =>
        {
            if (cancellation != source)
            {
                return;
            }

            SetStatus(ViewStatus.Progress, string.IsNullOrWhiteSpace(p.Item) ? p.Stage : $"{p.Stage}  {p.Item}");
            Progress.Update(p);
            OnProgress(p);
        });
        try
        {
            await action(source.Token, progress);
            outcome = itemsFailed ? OperationOutcome.CompletedWithErrors : OutcomeOf(Status);
        }
        catch (OperationCanceledException)
        {
            SetStatus(ViewStatus.Cancelled);
            outcome = OperationOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            SetStatus(ViewStatus.Error, ex.Message);
            outcome = OperationOutcome.Failed;
        }
        finally
        {
            cancellation = null;
            activity?.Complete(outcome, StatusText);
            activity = null;
            Progress.End();
            IsBusy = false;
        }
    }
}
