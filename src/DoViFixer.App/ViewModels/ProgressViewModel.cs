using DoViFixer.Application.Operations;

namespace DoViFixer.App.ViewModels;

public sealed class ProgressViewModel : BindableBase
{
    private bool isRunning;
    private string stage = "";
    private double? stagePercent;

    public bool IsRunning
    {
        get => isRunning;
        private set => SetProperty(ref isRunning, value);
    }

    public string Stage
    {
        get => stage;
        private set => SetProperty(ref stage, value);
    }

    public double Percent => stagePercent ?? 0;
    public double StagePercent => Percent;
    public bool IsIndeterminate => isRunning && stagePercent is null;
    public bool Indeterminate => IsIndeterminate;
    public string ProgressText => stagePercent is { } value ? $"{value:0}%" : isRunning ? "Working…" : "";
    public string StageProgressText => ProgressText;

    public void Start(string initialStage)
    {
        IsRunning = true;
        Update(initialStage, null);
    }

    public void Update(string stage, double? percent)
    {
        Stage = stage;
        stagePercent = percent is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 100) : null;
        RaisePropertyChanged(nameof(Percent));
        RaisePropertyChanged(nameof(StagePercent));
        RaisePropertyChanged(nameof(IsIndeterminate));
        RaisePropertyChanged(nameof(Indeterminate));
        RaisePropertyChanged(nameof(ProgressText));
        RaisePropertyChanged(nameof(StageProgressText));
    }

    public void Update(OperationProgress progress)
    {
        Update(progress.Stage, progress.Percent);
    }

    public void End()
    {
        IsRunning = false;
        RaisePropertyChanged(nameof(IsIndeterminate));
        RaisePropertyChanged(nameof(Indeterminate));
        RaisePropertyChanged(nameof(ProgressText));
        RaisePropertyChanged(nameof(StageProgressText));
    }

    public void Reset()
    {
        IsRunning = false;
        Stage = "";
        stagePercent = null;
        RaisePropertyChanged(nameof(Percent));
        RaisePropertyChanged(nameof(StagePercent));
        RaisePropertyChanged(nameof(IsIndeterminate));
        RaisePropertyChanged(nameof(Indeterminate));
        RaisePropertyChanged(nameof(ProgressText));
        RaisePropertyChanged(nameof(StageProgressText));
    }
}

