using DoViFixer.App.ViewModels;
using DoViFixer.Application.Operations;

namespace DoViFixer.App.Presentation.Application;
public abstract class OperationViewModel : ObservableObject
{
    private static readonly Dictionary<ViewStatus, string> statusTexts = new()
    {
        [ViewStatus.Ready] = "Ready",
        [ViewStatus.FileListCleared] = "File list cleared.",
        [ViewStatus.Cancelled] = "Cancelled; cleanup finished.",
        [ViewStatus.ToolsUnavailable] = "Required tools are unavailable.",
        [ViewStatus.BackupNotApproved] = "Backup not approved.",
        [ViewStatus.RestoreNotApproved] = "Restore not approved.",
        [ViewStatus.CleanupNotApproved] = "Cleanup not approved.",
        [ViewStatus.NoBackupsFound] = "No retained backup files found.",
        [ViewStatus.SettingsLoaded] = "Settings loaded.",
        [ViewStatus.SettingsSaved] = "Settings saved.",
        [ViewStatus.SettingsDiscarded] = "Unsaved changes discarded. Using the last saved settings.",
        [ViewStatus.ToolsReady] = "All tools ready.",
        [ViewStatus.ToolsNeedAttention] = "Tools need attention. Set a validated path or open dependency setup.",
        [ViewStatus.DependencySetupIncomplete] = "Dependency setup incomplete.",
        [ViewStatus.ToolPathSaved] = "Validated tool path saved and applied.",
        [ViewStatus.ToolOverrideReset] = "Tool override reset. Check tools to rediscover."
    };

    private CancellationTokenSource? cancellation;
    private bool isBusy;
    private ViewStatus status = ViewStatus.Ready;
    private string statusMessage = "";

    public ProgressViewModel Progress { get; } = new();

    protected OperationViewModel()
    {
        CancelCommand = new(() => cancellation?.Cancel(), () => IsBusy);
        Progress.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProgressViewModel.Percent) or nameof(ProgressViewModel.StagePercent))
            {
                RaisePropertyChanged(nameof(Percent));
            }

            if (e.PropertyName is nameof(ProgressViewModel.IsIndeterminate))
            {
                RaisePropertyChanged(nameof(IsIndeterminate));
            }

            if (e.PropertyName is nameof(ProgressViewModel.Stage))
            {
                RaisePropertyChanged(nameof(Stage));
            }
        };
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            SetProperty(ref isBusy, value);
            RaisePropertyChanged(nameof(IsIdle));
            CommandsChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !IsBusy;
    public ViewStatus Status => status;
    public string StatusMessage => statusMessage;
    public string StatusText => statusTexts.TryGetValue(Status, out string? text) ? text : StatusMessage;
    public virtual IReadOnlyList<string> StatusItems => [StatusText];

    public void SetStatus(ViewStatus value, string message = "")
    {
        bool statusChanged = status != value;
        bool messageChanged = statusMessage != message;
        status = value;
        statusMessage = message;
        if (statusChanged)
        {
            RaisePropertyChanged(nameof(Status));
        }
        if (messageChanged)
        {
            RaisePropertyChanged(nameof(StatusMessage));
        }
        if (statusChanged || messageChanged)
        {
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(StatusItems));
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

    protected Task RunAsync(Func<CancellationToken, IProgress<OperationProgress>, Task> action)
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        Completion = RunCoreAsync(action);
        return Completion;
    }

    private async Task RunCoreAsync(Func<CancellationToken, IProgress<OperationProgress>, Task> action)
    {
        using var source = new CancellationTokenSource();
        cancellation = source;
        IsBusy = true;
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
        }
        catch (OperationCanceledException)
        {
            SetStatus(ViewStatus.Cancelled);
        }
        catch (Exception ex)
        {
            SetStatus(ViewStatus.Error, ex.Message);
        }
        finally
        {
            cancellation = null;
            Progress.End();
            IsBusy = false;
        }
    }
}
