using DoViFixer.Application.Operations;

namespace DoViFixer.App.ViewModels;

public sealed partial class BatchProgressViewModel(TimeProvider time) : ObservableObject
{
    /// <summary>A step's remaining time is estimated only after it has run this long, so early rates do not jump around.</summary>
    private static readonly TimeSpan EstimateWarmUp = TimeSpan.FromSeconds(5);
    private readonly object gate = new();
    private ITimer? clock;
    private DateTimeOffset jobStarted;
    private DateTimeOffset stageStarted;
    private string stage = "";
    private double stageStartPercent;
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
    public double Percent => total == 0 ? 0 : (100.0 * processed + (CurrentJob?.Progress.Percent ?? 0)) / total;
    public string Summary => $"{processed} of {total} files processed" + (cancelled > 0 ? $" · {cancelled} cancelled" : "");
    [ObservableProperty]
    public partial MediaRow? CurrentJob { get; private set; }

    public BatchProgressViewModel() : this(TimeProvider.System)
    {
    }

    public string CurrentJobElapsed => CurrentJob is null ? "" : Format(time.GetUtcNow() - jobStarted);

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

                var running = time.GetUtcNow() - stageStarted;
                double done = job.Progress.Percent;
                if (done <= stageStartPercent || running < EstimateWarmUp)
                {
                    return "Estimating…";
                }

                return Format(running * ((100 - done) / (done - stageStartPercent)));
            }
        }
    }

    private static string Format(TimeSpan span) => span < TimeSpan.Zero ? "00:00:00" : $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";

    public void Begin(int fileCount, string operationName)
    {
        lock (gate)
        {
            revision++;
            CurrentJob?.Progress.End();
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
        lock (gate)
        {
            jobRevision = ++revision;
            CurrentJob?.Progress.End();
            CurrentJob = row;
            jobStarted = stageStarted = time.GetUtcNow();
            stage = initialStage;
            stageStartPercent = 0;
        }

        row.Progress.Start(initialStage);
        row.Progress.Describe([operation]);
        StartClock();
        OnPropertyChanged(nameof(Percent));
        NotifyTimes();
        // Each job owns its callback, so late reports cannot update another file or batch.
        return new Progress<OperationProgress>(progress =>
        {
            lock (gate)
            {
                if (!IsRunning || revision != jobRevision || CurrentJob != row || row.IsCancellationRequested)
                {
                    return;
                }

                if (progress.Stage != stage)
                {
                    stage = progress.Stage;
                    stageStarted = time.GetUtcNow();
                    stageStartPercent = progress.Percent is { } percent && double.IsFinite(percent) ? Math.Clamp(percent, 0, 100) : 0;
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

    public void Complete(OperationStatus status)
    {
        lock (gate)
        {
            revision++;
            processed++;
            if (status == OperationStatus.Cancelled)
            {
                cancelled++;
            }
            CurrentJob?.Progress.End();
            CurrentJob = null;
        }

        StopClock();
        NotifyBatchProgress();
        NotifyTimes();
    }

    public void End()
    {
        lock (gate)
        {
            revision++;
            IsRunning = false;
            CurrentJob?.Progress.End();
            CurrentJob = null;
        }

        StopClock();
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
}
