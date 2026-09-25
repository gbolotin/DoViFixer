using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.App.ViewModels;
public sealed class MediaRow(string path) : BindableBase
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

    #endregion

    #region Public fields
    
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public ProgressViewModel Progress { get; } = new();
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
            }
        }
    }
    public bool CanRetryAnalysis
    {
        get => canRetryAnalysis;
        set => SetProperty(ref canRetryAnalysis, value);
    }
    public bool CanOpenResult => Result?.Output is not null && State == MediaRowState.Converted;
   
    public string? Warning
    {
        get => warning;
        set
        {
            SetProperty(ref warning, value);
            RaisePropertyChanged(nameof(HasWarning));
            RaisePropertyChanged(nameof(StatusToolTip));
        }
    }
    public bool HasWarning => !string.IsNullOrWhiteSpace(warning);
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
            RaisePropertyChanged(nameof(Details));
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
            RaisePropertyChanged(nameof(ClassificationColor));
            RaisePropertyChanged(nameof(Details));
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
            RaisePropertyChanged(nameof(HasIncompleteScan));
            RaisePropertyChanged(nameof(CanInspectIncomplete));
            RaisePropertyChanged(nameof(Classification));
            RaisePropertyChanged(nameof(ClassificationColor));
            RaisePropertyChanged(nameof(Details));
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
    public string ClassificationColor => AnalysisError is not null ? "#FF625A" : Analysis?.Verdict switch
    {
        AnalysisVerdict.Mel => "#66D94B",
        AnalysisVerdict.SimpleFel => "#29AEFA",
        AnalysisVerdict.ComplexFel or AnalysisVerdict.AnalysisFailed => "#FF625A",
        AnalysisVerdict.Unknown or AnalysisVerdict.FelUnclassified => "#FFD166",
        _ => "#A6B6C3"
    };
    public string Details => AnalysisError is not null ? $"{Path}\n\nAnalysis failed\n{AnalysisError}" : Analysis is not{ }
    a ? Path + "\n" + Notice : $"{Path}\n\n{Classification}\n{a.Media.Width} × {a.Media.Height} · {a.Media.FramesPerSecond:0.###} fps\n" + $"{TimeSpan.FromSeconds(a.Media.DurationSeconds ?? 0):g} · {a.Media.Source.Length / 1073741824d:0.00} GiB\n\n" + $"Evidence: {EvidenceName(a.Evidence.Method)}\nFrames: {a.Evidence.Frames:N0}\nSamples: {a.Evidence.SuccessfulSamples}/{a.Evidence.RequestedSamples}\n\n{a.Reason}\n{a.Evidence.SampleDiagnostics}\n" + (HasIncompleteScan ? "\nSuggested action: Inspect. Standard inspection examines the full RPU metadata stream instead of short samples. It may take longer; missing metadata may still prevent classification.\n" : "") + $"\n{Notice}";
    public string ResultDetails => Result is null ? "" : $"Last conversion result\n{Result.Status}\n{Result.Output}\n{Result.Message}";

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

    public void SetSkipped()
    {
        IsPending = false;
        IsSelected = false;
        SelectionEnabled = false;
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
        if (message is not null)
        {
            Notice = message;
        }

        Status = $"{OperationName} cancelled";
        State = MediaRowState.Cancelled;
    }

    public void SetFailed(string? error, string? statusText = null)
    {
        AnalysisError = error;
        Status = statusText ?? $"{OperationName} failed";
        State = MediaRowState.Failed;
    }

    #endregion

    private static string EvidenceName(AnalysisMethod method) => method switch
    {
        AnalysisMethod.SampledRpu => "Sampled RPU metadata",
        AnalysisMethod.FullRpu => "Full RPU inspection",
        AnalysisMethod.DeepInspection => "Deep frame inspection",
        _ => "Metadata only"
    };
}
