using WpfFoundation.Operations;

namespace DoViFixer.App.Tests;

/// <summary>Records the operations a ViewModel reports outside the window.</summary>
internal sealed class RecordingFeedback : IOperationFeedback
{
    public List<Activity> Activities { get; } = [];

    public IOperationActivity Start(string title)
    {
        var activity = new Activity(title);
        Activities.Add(activity);
        return activity;
    }

    internal sealed class Activity(string title) : IOperationActivity
    {
        public string Title { get; } = title;

        public List<double?> Reports { get; } = [];

        public List<bool> Pauses { get; } = [];

        public (OperationOutcome Outcome, string? Summary)? Completion { get; private set; }

        public void Report(double? fraction) => Reports.Add(fraction);

        public void SetPaused(bool paused) => Pauses.Add(paused);

        public void Complete(OperationOutcome outcome, string? summary = null) => Completion ??= (outcome, summary);
    }
}
