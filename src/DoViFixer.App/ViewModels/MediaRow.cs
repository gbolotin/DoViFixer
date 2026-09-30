using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.App.ViewModels;
public sealed class MediaRow(string path) : ObservableObject
{
    #region Private fields

    private bool selected = true;
    private bool selectionEnabled = true;
    private bool pending;
    private bool active;
    private bool cancellationRequested;
    private MediaRowState state = MediaRowState.NotScanned;
    private string status = "Not scanned";
    private bool canRetryAnalysis;
    private string output = "";
    private MediaAnalysis? analysis;
    private OperationItemResult? result;
    private string? analysisError;
    private string? warning;
    private string notice = "";
    private string? restoreArchive;

    #endregion

    #region Public fields
    
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public ProgressViewModel Progress { get; } = new();
    public bool IsProfile7 => Analysis?.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7;
    public bool CanRestore => Analysis?.Media.Profile == Domain.Media.DolbyVisionProfile.Profile81 && RestoreArchive is not null && State != MediaRowState.Restored;
    public string? RestoreArchive
    {
        get => restoreArchive;
        set
        {
            SetProperty(ref restoreArchive, value);
            RaisePropertyChanged(nameof(CanRestore));
        }
    }
    public bool IsCancellationRequested
    {
        get => cancellationRequested;
        set => SetProperty(ref cancellationRequested, value);
    }
    public bool HasIncompleteScan => Analysis is not null
        && Analysis.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7
        && Analysis.Verdict == AnalysisVerdict.Unknown
        && Analysis.Evidence.Method == AnalysisMethod.SampledRpu
        && (Analysis.Evidence.Frames <= 0 || Analysis.Evidence.SuccessfulSamples < Analysis.Evidence.RequestedSamples);
    public bool CanInspectIncomplete => HasIncompleteScan && !IsActive && !IsPending;
    public AnalysisMethod? LastAnalysisMethod { get; set;} = AnalysisMethod.SampledRpu;
    public string? CurrentOperation { get; set; }
    public string OperationName => CurrentOperation ?? (LastAnalysisMethod is null ? "Conversion" : LastAnalysisMethod == AnalysisMethod.SampledRpu ? "Scan" : "Inspection");
    public MediaRowState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                RaisePropertyChanged(nameof(CanOpenResult));
                RaisePropertyChanged(nameof(CanRestore));
                RaisePropertyChanged(nameof(Warning));
                RaisePropertyChanged(nameof(HasWarning));
                RaisePropertyChanged(nameof(StatusToolTip));
            }
        }
    }
    public bool CanRetryAnalysis
    {
        get => canRetryAnalysis;
        set => SetProperty(ref canRetryAnalysis, value);
    }
    public bool CanOpenResult => Result?.Output is not null && State is MediaRowState.Converted or MediaRowState.Restored;
   
    public string? Warning
    {
        get => State == MediaRowState.Skipped ? Notice : warning;
        set
        {
            SetProperty(ref warning, value);
            RaisePropertyChanged(nameof(HasWarning));
            RaisePropertyChanged(nameof(StatusToolTip));
        }
    }
    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);
    public bool IsSelected
    {
        get => selected;
        set => SetProperty(ref selected, value);
    }
    public bool SelectionEnabled
    {
        get => selectionEnabled;
        set => SetProperty(ref selectionEnabled, value);
    }
    public bool IsPending
    {
        get => pending;
        set
        {
            SetProperty(ref pending, value);
            RaisePropertyChanged(nameof(CanInspectIncomplete));
        }
    }
    public bool IsActive
    {
        get => active;
        set
        {
            SetProperty(ref active, value);
            RaisePropertyChanged(nameof(CanInspectIncomplete));
        }
    }
    public string Status
    {
        get => status;
        set
        {
            SetProperty(ref status, value);
            RaisePropertyChanged(nameof(CanOpenResult));
            RaisePropertyChanged(nameof(StatusToolTip));
        }
    }
    public string PlannedOutput
    {
        get => output;
        set => SetProperty(ref output, value);
    }

    public string Notice
    {
        get => notice;
        set
        {
            SetProperty(ref notice, value);
            RaisePropertyChanged(nameof(Warning));
            RaisePropertyChanged(nameof(HasWarning));
            RaisePropertyChanged(nameof(DetailNotes));
            RaisePropertyChanged(nameof(StatusToolTip));
        }
    }

    public string? StatusToolTip
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Notice))
            {
                return Notice;
            }

            if (!string.IsNullOrWhiteSpace(Warning))
            {
                return Warning;
            }

            if (!string.IsNullOrWhiteSpace(Result?.Message))
            {
                return Result.Message;
            }

            if (!string.IsNullOrWhiteSpace(AnalysisError))
            {
                return AnalysisError;
            }

            return string.IsNullOrWhiteSpace(Status) ? null : Status;
        }
    }

    public string? AnalysisError
    {
        get => analysisError;
        set
        {
            SetProperty(ref analysisError, value);
            RaisePropertyChanged(nameof(Classification));
            RaisePropertyChanged(nameof(HasAnalysisError));
            RaisePropertyChanged(nameof(DetailRows));
            RaisePropertyChanged(nameof(DetailNotes));
            RaisePropertyChanged(nameof(StatusToolTip));
        }
    }

    public OperationItemResult? Result
    {
        get => result;
        set
        {
            SetProperty(ref result, value);
            RaisePropertyChanged(nameof(ResultDetails));
            RaisePropertyChanged(nameof(CanOpenResult));
            RaisePropertyChanged(nameof(StatusToolTip));
        }
    }

    public MediaAnalysis? Analysis
    {
        get => analysis;
        set
        {
            SetProperty(ref analysis, value);
            RaisePropertyChanged(nameof(IsProfile7));
            RaisePropertyChanged(nameof(CanRestore));
            RaisePropertyChanged(nameof(HasIncompleteScan));
            RaisePropertyChanged(nameof(CanInspectIncomplete));
            RaisePropertyChanged(nameof(Classification));
            RaisePropertyChanged(nameof(DetailRows));
            RaisePropertyChanged(nameof(DetailNotes));
        }
    }

    public string Classification => AnalysisError is not null ? "Analysis failed" : Analysis is null ? "" : Analysis.Verdict switch
    {
        AnalysisVerdict.Mel => "Profile 7 · MEL",
        AnalysisVerdict.SimpleFel => "Profile 7 · Simple FEL",
        AnalysisVerdict.ComplexFel => "Profile 7 · Complex FEL",
        AnalysisVerdict.FelUnclassified => "Profile 7 · FEL · Unclassified",
        AnalysisVerdict.AnalysisFailed => "Analysis failed",
        AnalysisVerdict.Unknown => HasIncompleteScan ? "Profile 7 · Incomplete scan" : "Unknown",
        _ => Analysis.Media.Profile switch
        {
            Domain.Media.DolbyVisionProfile.Profile81 => "Profile 8.1",
            Domain.Media.DolbyVisionProfile.Profile5 => "Profile 5",
            Domain.Media.DolbyVisionProfile.None => "No Dolby Vision",
            _ => "Other / unknown profile"
        }
    };
    public bool HasAnalysisError => AnalysisError is not null;
    public IReadOnlyList<KeyValuePair<string, string>> DetailRows
    {
        get
        {
            if (Analysis is not { } a)
            {
                return [new("File location", Path)];
            }

            string length = "Unknown";
            if (a.Media.DurationSeconds is { } seconds && double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
            {
                var duration = TimeSpan.FromSeconds(seconds);
                length = $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
            }

            return
            [
                new("Type", "Matroska"),
                new("Size", a.Media.Source.Length >= 1073741824
                    ? $"{a.Media.Source.Length / 1073741824d:0.##} GiB" : $"{a.Media.Source.Length / 1048576d:0.##} MiB"),
                new("File location", Path),
                new("Date modified", a.Media.Source.LastWriteUtc.ToLocalTime().ToString("g")),
                new("Length", length),
                new("Profile / Type", Classification),
                new("Resolution", $"{a.Media.Width} × {a.Media.Height}"),
                new("Frame rate", a.Media.FramesPerSecond is { } fps ? $"{fps:0.###} fps" : "Unknown"),
                new("Evidence", EvidenceName(a.Evidence.Method)),
                new("Frames", a.Evidence.Frames.ToString("N0")),
                new("Samples", $"{a.Evidence.SuccessfulSamples}/{a.Evidence.RequestedSamples}")
            ];
        }
    }

    public string DetailNotes => string.Join("\n\n", new[]
    {
        AnalysisError is not null ? $"Analysis failed\n{AnalysisError}" : Analysis?.Reason,
        Analysis?.Evidence.SampleDiagnostics,
        HasIncompleteScan ? "Suggested action: Inspect. Standard inspection examines the full RPU metadata stream instead of short samples. It may take longer; missing metadata may still prevent classification." : null,
        Notice
    }.Where(text => !string.IsNullOrWhiteSpace(text)));
    public string ResultDetails => Result is null ? "" : $"Last operation result\n{Result.Status}\n{Result.Output}\n{Result.Message}";

    #endregion

    public void SetPlan(string plannedOutput, string? warning)
    {
        PlannedOutput = plannedOutput;
        Warning = warning;
        Status = "Ready to convert";
        State = MediaRowState.PlanReady;
    }

    public void ClearPlan()
    {
        PlannedOutput = "";
        Warning = null;
        if (State == MediaRowState.PlanReady)
        {
            Status = "";
            State = MediaRowState.Scanned;
        }
    }

    public void SetConverted(OperationItemResult itemResult)
    {
        Result = itemResult;
        Warning = itemResult.Status == OperationStatus.Partial
            ? (string.IsNullOrWhiteSpace(itemResult.Message) ? "Conversion completed with warnings." : itemResult.Message)
            : null;
        Status = itemResult.Status == OperationStatus.Partial ? "Converted with warnings" : "Converted";
        State = MediaRowState.Converted;
    }

    public void SetScanned()
    {
        Status = "";
        State = MediaRowState.Scanned;
    }

    public void SetRestored(OperationItemResult itemResult)
    {
        ClearPlan();
        Result = itemResult;
        Status = "Restored";
        State = MediaRowState.Restored;
    }

    public void SetSkipped(string reason)
    {
        ClearPlan();
        IsPending = false;
        IsSelected = false;
        SelectionEnabled = false;
        Notice = reason;
        Status = $"{OperationName} skipped";
        State = MediaRowState.Skipped;
    }

    public void SetActive(string stage)
    {
        IsPending = false;
        IsActive = true;
        SelectionEnabled = false;
        Status = stage;
        State = MediaRowState.Active;
    }

    public void SetCancelled(string? message = null)
    {
        ClearPlan();
        if (message is not null)
        {
            Notice = message;
        }

        Status = $"{OperationName} cancelled";
        State = MediaRowState.Cancelled;
    }

    public void SetFailed(string? error, string? statusText = null)
    {
        ClearPlan();
        AnalysisError = error;
        Status = statusText ?? $"{OperationName} failed";
        State = MediaRowState.Failed;
    }

    private static string EvidenceName(AnalysisMethod method) => method switch
    {
        AnalysisMethod.SampledRpu => "Sampled RPU metadata",
        AnalysisMethod.FullRpu => "Full RPU inspection",
        AnalysisMethod.DeepInspection => "Deep frame inspection",
        _ => "Metadata only"
    };
}
