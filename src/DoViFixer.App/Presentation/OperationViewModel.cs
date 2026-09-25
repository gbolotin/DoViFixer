using DoViFixer.App.ViewModels;
using DoViFixer.Application.Operations;

namespace DoViFixer.App.Presentation;
public abstract class OperationViewModel : ObservableObject
{
    private CancellationTokenSource? cancellation;
    private bool isBusy;
    private string status = "Ready";

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
    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
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

            Status = string.IsNullOrWhiteSpace(p.Item) ? p.Stage : $"{p.Stage}  {p.Item}";
            Progress.Update(p);
            OnProgress(p);
        });
        try
        {
            await action(source.Token, progress);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled; cleanup finished.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            cancellation = null;
            Progress.End();
            IsBusy = false;
        }
    }
}
