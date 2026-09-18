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
    private string stage = "";
    private double? stagePercent;

    public bool IsRunning { get => isRunning; private set => SetProperty(ref isRunning, value); }
    public int Total => total;
    public int Processed => processed;
    public string Operation => operation;
    public double Percent => total == 0 ? 0 : 100.0 * processed / total;
    public string Summary => $"{processed} of {total} files processed";
    public MediaRow? CurrentJob { get => currentJob; private set => SetProperty(ref currentJob, value); }
    public string Stage { get => stage; private set => SetProperty(ref stage, value); }
    public double StagePercent => stagePercent ?? 0;
    public bool IsIndeterminate => stagePercent is null && CurrentJob is not null;
    public string StageProgressText => stagePercent is { } value ? $"{value:0}%" : "Working…";

    public void Begin(int fileCount, string operationName)
    {
        revision++;
        total = fileCount;
        processed = 0;
        operation = operationName;
        CurrentJob = null;
        Stage = "Waiting to start";
        SetStagePercent(null);
        IsRunning = true;
        RaisePropertyChanged(nameof(Total));
        RaisePropertyChanged(nameof(Operation));
        NotifyBatchProgress();
    }

    public IProgress<OperationProgress> Start(MediaRow row, string initialStage)
    {
        int jobRevision = ++revision;
        CurrentJob = row;
        Stage = initialStage;
        SetStagePercent(null);
        // Each job owns its callback, so late reports cannot update another file or batch.
        return new Progress<OperationProgress>(progress =>
        {
            if (!IsRunning || revision != jobRevision)
            {
                return;
            }

            Stage = progress.Stage;
            if (row.LastAnalysisMethod is null)
            {
                row.Status = progress.Stage;
            }
            SetStagePercent(progress.Percent);
        });
    }

    public void Complete()
    {
        revision++;
        processed++;
        CurrentJob = null;
        Stage = "Waiting for next file";
        SetStagePercent(null);
        NotifyBatchProgress();
    }

    public void End()
    {
        revision++;
        IsRunning = false;
        CurrentJob = null;
        SetStagePercent(null);
    }

    private void SetStagePercent(double? value)
    {
        stagePercent = value is { } percent && double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : null;
        RaisePropertyChanged(nameof(StagePercent));
        RaisePropertyChanged(nameof(IsIndeterminate));
        RaisePropertyChanged(nameof(StageProgressText));
    }

    private void NotifyBatchProgress()
    {
        RaisePropertyChanged(nameof(Processed));
        RaisePropertyChanged(nameof(Percent));
        RaisePropertyChanged(nameof(Summary));
    }
}
