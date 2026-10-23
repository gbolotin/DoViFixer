using DoViFixer.Application.Operations;

namespace DoViFixer.App.ViewModels;

public sealed partial class BatchProgressViewModel(TimeProvider time) : ObservableObject
{
    /// <summary>A step's remaining time is estimated only after it has run this long, so early rates do not jump around.</summary>
    private static readonly TimeSpan EstimateWarmUp = TimeSpan.FromSeconds(5);
    private readonly object gate = new();
    private ITimer? clock;
    private readonly Dictionary<MediaRow, JobTiming> active = new();
    private int revision;
    private int total;
    private int processed;
    private int cancelled;
    private string operation = "";

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    public int Total => total;
    public int Processed => processed;
    public string Operation => operation;
    public double Percent
    {
        get
        {
            lock (gate)
            {
                return total == 0 ? 0 : (100.0 * processed + active.Keys.Sum(row => row.Progress.Percent)) / total;
            }
        }
    }
    public int ActiveCount
    {
        get
        {
            lock (gate)
            {
                return active.Count;
            }
        }
    }
    public string Summary => $"{processed} of {total} files processed" + (cancelled > 0 ? $" · {cancelled} cancelled" : "");
    [ObservableProperty]
    public partial MediaRow? CurrentJob { get; private set; }

    public BatchProgressViewModel() : this(TimeProvider.System)
    {
    }

    public string CurrentJobElapsed
    {
        get
        {
            lock (gate)
            {
                return CurrentJob is not null && active.TryGetValue(CurrentJob, out var timing) ? Format(time.GetUtcNow() - timing.Started) : "";
            }
        }
    }

    /// <summary>
    /// The current step's remaining time at its average rate so far. Steps rewrite or read the whole video at
    /// different speeds, so no estimate covers the steps still to come.
    /// </summary>
    public string CurrentStepRemaining
    {
        get
        {
            lock (gate)
            {
                if (CurrentJob is not { } job)
                {
                    return "";
                }

                // A cancelling job, or a step that reports no percentage, has nothing to estimate from.
                if (job.IsCancellationRequested || job.Progress.IsIndeterminate)
                {
                    return "—";
                }

                if (!active.TryGetValue(job, out var timing))
                {
                    return "";
                }

                var running = time.GetUtcNow() - timing.StageStarted;
                double done = job.Progress.Percent;
                if (done <= timing.StageStartPercent || running < EstimateWarmUp)
                {
                    return "Estimating…";
                }

                return Format(running * ((100 - done) / (done - timing.StageStartPercent)));
            }
        }
    }

    private static string Format(TimeSpan span) => span < TimeSpan.Zero ? "00:00:00" : $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";

    public void Begin(int fileCount, string operationName)
    {
        lock (gate)
        {
            revision++;
            foreach (var row in active.Keys)
            {
                row.Progress.End();
            }
            active.Clear();
            total = fileCount;
            processed = 0;
            cancelled = 0;
            operation = operationName;
            CurrentJob = null;
            IsRunning = true;
        }

        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Operation));
        NotifyBatchProgress();
    }

    public IProgress<OperationProgress> Start(MediaRow row, string initialStage)
    {
        int jobRevision;
        JobTiming startedTiming;
        bool startClock;
        lock (gate)
        {
            jobRevision = revision;
            startedTiming = new(time.GetUtcNow(), initialStage);
            active[row] = startedTiming;
            CurrentJob ??= row;
            row.Progress.Start(initialStage);
            row.Progress.Describe([operation]);
            startClock = active.Count == 1;
        }

        if (startClock)
        {
            StartClock();
        }
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(Percent));
        NotifyTimes();
        // Each job owns its callback, so late reports cannot update another file or batch.
        return new Progress<OperationProgress>(progress =>
        {
            lock (gate)
            {
                if (!IsRunning || revision != jobRevision || !active.TryGetValue(row, out var timing) || timing != startedTiming || row.IsCancellationRequested)
                {
                    return;
                }

                if (progress.Stage != timing.Stage)
                {
                    timing.Stage = progress.Stage;
                    timing.StageStarted = time.GetUtcNow();
                    timing.StageStartPercent = progress.Percent is { } percent && double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : 0;
                }

                row.Progress.Update(progress);
                OnPropertyChanged(nameof(Percent));
                if (row.LastAnalysisMethod is null || row.CurrentOperation == "Conversion planning")
                {
                    row.Status = progress.Stage;
                }
            }
        });
    }

    public void Describe(MediaRow row, IReadOnlyList<string> details, IEnumerable<JobStep> steps)
    {
        lock (gate)
        {
            if (active.ContainsKey(row))
            {
                row.Progress.Describe(details, steps);
            }
        }
    }

    public void Complete(OperationStatus status) => Complete(CurrentJob, status);

    public void Complete(MediaRow? row, OperationStatus status)
    {
        bool stopClock;
        lock (gate)
        {
            processed++;
            if (status == OperationStatus.Cancelled)
            {
                cancelled++;
            }
            if (row is not null && active.Remove(row))
            {
                row.Progress.End();
            }

            if (CurrentJob == row)
            {
                CurrentJob = active.Keys.FirstOrDefault();
            }
            stopClock = active.Count == 0;
        }

        if (stopClock)
        {
            StopClock();
        }
        OnPropertyChanged(nameof(ActiveCount));
        NotifyBatchProgress();
        NotifyTimes();
    }

    public void End()
    {
        lock (gate)
        {
            revision++;
            IsRunning = false;
            foreach (var row in active.Keys)
            {
                row.Progress.End();
            }
            active.Clear();
            CurrentJob = null;
        }

        StopClock();
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(Percent));
        NotifyTimes();
    }

    // Elapsed and remaining times change without progress reports, so a clock refreshes them every second on the UI thread.
    private void StartClock()
    {
        StopClock();
        var context = SynchronizationContext.Current;
        clock = time.CreateTimer(_ =>
        {
            if (context is null)
            {
                NotifyTimes();
            }
            else
            {
                context.Post(_ => NotifyTimes(), null);
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void StopClock()
    {
        clock?.Dispose();
        clock = null;
    }

    private void NotifyTimes()
    {
        OnPropertyChanged(nameof(CurrentJobElapsed));
        OnPropertyChanged(nameof(CurrentStepRemaining));
    }

    private void NotifyBatchProgress()
    {
        OnPropertyChanged(nameof(Processed));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(Summary));
    }

    private sealed class JobTiming(DateTimeOffset started, string stage)
    {
        public DateTimeOffset Started { get; } = started;
        public DateTimeOffset StageStarted { get; set; } = started;
        public string Stage { get; set; } = stage;
        public double StageStartPercent { get; set; }
    }
}
