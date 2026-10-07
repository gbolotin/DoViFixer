namespace DoViFixer.App.ViewModels;

public enum JobStepState
{
    Pending,
    Active,
    Completed
}

/// <summary>One step of a job's stepper. A step is active while the job reports any of its stages.</summary>
public sealed class JobStep(string name, params string[] stages) : ObservableObject
{
    private JobStepState state;

    public string Name { get; } = name;

    /// <summary>The stepper draws no connector after the last step.</summary>
    public bool IsLast { get; set; }

    public JobStepState State
    {
        get => state;
        set
        {
            if (SetProperty(ref state, value))
            {
                OnPropertyChanged(nameof(StateText));
            }
        }
    }

    public string StateText => State switch
    {
        JobStepState.Active => "In progress",
        JobStepState.Completed => "Completed",
        _ => "Pending"
    };

    public bool Reports(string stage) => stages.Any(s => stage.StartsWith(s, StringComparison.Ordinal));
}
