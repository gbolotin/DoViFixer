namespace DoViFixer.App.ViewModels;

public sealed class ProgressViewModel : BindableBase
{
    private bool isRunning;
    private string stage = "";
    private double? stagePercent;

    public string Stage { get => stage; private set => SetProperty(ref stage, value); }
    public double StagePercent => stagePercent ?? 0;
    public bool IsIndeterminate => isRunning && stagePercent is null;
    public string StageProgressText => stagePercent is { } value ? $"{value:0}%" : isRunning ? "Working…" : "";

    public void Start(string initialStage)
    {
        isRunning = true;
        Update(initialStage, null);
    }

    public void Update(string stage, double? percent)
    {
        Stage = stage;
        stagePercent = percent is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 100) : null;
        RaisePropertyChanged(nameof(StagePercent));
        RaisePropertyChanged(nameof(IsIndeterminate));
        RaisePropertyChanged(nameof(StageProgressText));
    }

    public void End()
    {
        isRunning = false;
        RaisePropertyChanged(nameof(IsIndeterminate));
        RaisePropertyChanged(nameof(StageProgressText));
    }
}
