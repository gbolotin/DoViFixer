using DoViFixer.Application.Operations;

namespace DoViFixer.App.Presentation;
public abstract class OperationViewModel : BindableBase
{
    private CancellationTokenSource? cancellation;
    private bool isBusy;
    private string status = "Ready";
    private double percent;
    private bool indeterminate;
    protected OperationViewModel()
    {
        CancelCommand = new(() => cancellation?.Cancel(), () => IsBusy);
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
    public double Percent
    {
        get => percent;
        private set => SetProperty(ref percent, value);
    }
    public bool Indeterminate
    {
        get => indeterminate;
        private set => SetProperty(ref indeterminate, value);
    }
    public DelegateCommand CancelCommand
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
        Percent = 0;
        Indeterminate = true;
        // Callback lifetime is bounded; queued progress cannot overwrite a later operation.
        var progress = new Progress<OperationProgress>(p =>
        {
            if (cancellation != source)
            {
                return;
            }

            Status = $"{p.Stage}  {p.File}";
            Percent = p.Percent ?? 0;
            Indeterminate = p.Percent is null;
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
            Indeterminate = false;
            IsBusy = false;
        }
    }
}
