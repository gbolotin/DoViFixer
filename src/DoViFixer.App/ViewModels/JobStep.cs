namespace DoViFixer.App.ViewModels;

public enum JobStepState
{
    Pending,
    Active,
    Completed
}

/// <summary>One step of a job's stepper. A step is active while the job reports any of its stages.</summary>
public sealed partial class JobStep(string name, params string[] stages) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>The stepper draws no connector after the last step.</summary>
    public bool IsLast { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial JobStepState State { get; set; }

    public string StateText => State switch
    {
        JobStepState.Active => "In progress",
        JobStepState.Completed => "Completed",
        _ => "Pending"
    };

    public bool Reports(string stage) => stages.Any(s => stage.StartsWith(s, StringComparison.Ordinal));
}
