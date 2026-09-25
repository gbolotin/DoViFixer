using DoViFixer.Application.Operations;

namespace DoViFixer.App.ViewModels;

public sealed class BatchProgressViewModel : ObservableObject
{
    private readonly object gate = new();
    private int revision;
    private bool isRunning;
    private int total;
    private int processed;
    private string operation = "";
    private MediaRow? currentJob;

    public bool IsRunning { get => isRunning; private set => SetProperty(ref isRunning, value); }
    public int Total => total;
    public int Processed => processed;
    public string Operation => operation;
    public double Percent => total == 0 ? 0 : (100.0 * processed + (CurrentJob?.Progress.Percent ?? 0)) / total;
    public string Summary => $"{processed} of {total} files processed";
    public MediaRow? CurrentJob { get => currentJob; private set => SetProperty(ref currentJob, value); }

    public void Begin(int fileCount, string operationName)
    {
        lock (gate)
        {
            revision++;
            CurrentJob?.Progress.End();
            total = fileCount;
            processed = 0;
            operation = operationName;
            CurrentJob = null;
            IsRunning = true;
        }

        RaisePropertyChanged(nameof(Total));
        RaisePropertyChanged(nameof(Operation));
        NotifyBatchProgress();
    }

    public IProgress<OperationProgress> Start(MediaRow row, string initialStage)
    {
        int jobRevision;
        lock (gate)
        {
            jobRevision = ++revision;
            CurrentJob?.Progress.End();
            CurrentJob = row;
        }

        row.Progress.Start(initialStage);
        RaisePropertyChanged(nameof(Percent));
        // Each job owns its callback, so late reports cannot update another file or batch.
        return new Progress<OperationProgress>(progress =>
        {
            lock (gate)
            {
                if (!IsRunning || revision != jobRevision || CurrentJob != row || row.IsCancellationRequested)
                {
                    return;
                }

                row.Progress.Update(progress);
                RaisePropertyChanged(nameof(Percent));
                if (row.LastAnalysisMethod is null || row.CurrentOperation == "Conversion planning")
                {
                    row.Status = progress.Stage;
                }
            }
        });
    }

    public void Complete()
    {
        lock (gate)
        {
            revision++;
            processed++;
            CurrentJob?.Progress.End();
            CurrentJob = null;
        }

        NotifyBatchProgress();
    }

    public void End()
    {
        lock (gate)
        {
            revision++;
            IsRunning = false;
            CurrentJob?.Progress.End();
            CurrentJob = null;
        }

        RaisePropertyChanged(nameof(Percent));
    }

    private void NotifyBatchProgress()
    {
        RaisePropertyChanged(nameof(Processed));
        RaisePropertyChanged(nameof(Percent));
        RaisePropertyChanged(nameof(Summary));
    }
}
