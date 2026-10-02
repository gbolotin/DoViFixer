using DoViFixer.Application.Operations;

namespace DoViFixer.App.ViewModels;

public sealed class ProgressViewModel : ObservableObject
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
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(StagePercent));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StageProgressText));
    }

    public void Update(OperationProgress progress)
    {
        Update(progress.Stage, progress.Percent);
    }

    public void End()
    {
        IsRunning = false;
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StageProgressText));
    }

    public void Reset()
    {
        IsRunning = false;
        Stage = "";
        stagePercent = null;
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(StagePercent));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StageProgressText));
    }
}

