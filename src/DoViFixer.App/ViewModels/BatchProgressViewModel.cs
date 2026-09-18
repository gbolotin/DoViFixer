using DoViFixer.Application.Operations;

namespace DoViFixer.App.ViewModels;

public sealed class BatchProgressViewModel : BindableBase
{
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
    public double Percent => total == 0 ? 0 : 100.0 * processed / total;
    public string Summary => $"{processed} of {total} files processed";
    public MediaRow? CurrentJob { get => currentJob; private set => SetProperty(ref currentJob, value); }

    public void Begin(int fileCount, string operationName)
    {
        revision++;
        CurrentJob?.Progress.End();
        total = fileCount;
        processed = 0;
        operation = operationName;
        CurrentJob = null;
        IsRunning = true;
        RaisePropertyChanged(nameof(Total));
        RaisePropertyChanged(nameof(Operation));
        NotifyBatchProgress();
    }

    public IProgress<OperationProgress> Start(MediaRow row, string initialStage)
    {
        int jobRevision = ++revision;
        CurrentJob?.Progress.End();
        CurrentJob = row;
        row.Progress.Start(initialStage);
        // Each job owns its callback, so late reports cannot update another file or batch.
        return new Progress<OperationProgress>(progress =>
        {
            if (!IsRunning || revision != jobRevision)
            {
                return;
            }

            row.Progress.Update(progress.Stage, progress.Percent);
            if (row.LastAnalysisMethod is null)
            {
                row.Status = progress.Stage;
            }
        });
    }

    public void Complete()
    {
        revision++;
        processed++;
        CurrentJob?.Progress.End();
        CurrentJob = null;
        NotifyBatchProgress();
    }

    public void End()
    {
        revision++;
        IsRunning = false;
        CurrentJob?.Progress.End();
        CurrentJob = null;
    }

    private void NotifyBatchProgress()
    {
        RaisePropertyChanged(nameof(Processed));
        RaisePropertyChanged(nameof(Percent));
        RaisePropertyChanged(nameof(Summary));
    }
}
