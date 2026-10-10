using System.Collections.ObjectModel;
using DoViFixer.Application.Operations;

namespace DoViFixer.App.ViewModels;

public sealed partial class ProgressViewModel : ObservableObject
{
    private double? stagePercent;

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial string Stage { get; private set; } = "";

    public double Percent => stagePercent ?? 0;
    public double StagePercent => Percent;
    public bool IsIndeterminate => IsRunning && stagePercent is null;
    public string ProgressText => stagePercent is { } value ? $"{value:0}%" : IsRunning ? "Working…" : "";
    public string StageProgressText => ProgressText;

    /// <summary>Lines under the job's file name, such as the conversion target and what happens to the original.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Details { get; private set; } = [];

    /// <summary>The job's steps, in order; empty when the operation has no fixed steps.</summary>
    public ObservableCollection<JobStep> Steps { get; } = [];
    public bool HasSteps => Steps.Count > 0;

    public void Start(string initialStage)
    {
        IsRunning = true;
        Details = [];
        Steps.Clear();
        OnPropertyChanged(nameof(HasSteps));
        Update(initialStage, null);
    }

    public void Describe(IReadOnlyList<string> jobDetails, IEnumerable<JobStep>? steps = null)
    {
        Details = jobDetails;
        Steps.Clear();
        var added = (steps ?? []).ToList();
        foreach (var step in added)
        {
            step.IsLast = ReferenceEquals(step, added[^1]);
            Steps.Add(step);
        }

        OnPropertyChanged(nameof(HasSteps));
        UpdateSteps();
    }

    public void Update(string stage, double? percent)
    {
        Stage = stage;
        UpdateSteps();
        stagePercent = percent is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 100) : null;
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(StagePercent));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StageProgressText));
    }

    // Stages no step reports, such as planning or cancelling, leave the steps as they are.
    private void UpdateSteps()
    {
        int active = Steps.ToList().FindIndex(step => step.Reports(Stage));
        if (active < 0)
        {
            return;
        }

        for (int i = 0; i < Steps.Count; i++)
        {
            Steps[i].State = i < active ? JobStepState.Completed : i == active ? JobStepState.Active : JobStepState.Pending;
        }
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
        Details = [];
        Steps.Clear();
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(StagePercent));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(StageProgressText));
    }
}

