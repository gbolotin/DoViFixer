using DoViFixer.Application.Operations;
using DoViFixer.Application.Updates;
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
    private MediaAnalysis? resultAnalysis;
    private OperationItemResult? result;
    private string? analysisError;
    private string? warning;
    private string notice = "";
    private string? restoreArchive;
    private bool missing;

    #endregion

    #region Public fields

    public const string FileLocationLabel = "File location";
    public const string MissingStatus = "File not found";
    public static readonly string MissingNote = $"File not found. It was deleted, moved or renamed outside {ApplicationTitle.Name}. Restore it to this location, or remove it from the list.";
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public ProgressViewModel Progress { get; } = new();
    public bool IsProfile7 => Analysis?.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7;
    public bool CanRestore => Analysis?.Media.Profile == Domain.Media.DolbyVisionProfile.Profile81 && RestoreArchive is not null && State != MediaRowState.Restored && !IsMissing;
    public string? RestoreArchive
    {
        get => restoreArchive;
        set
        {
            SetProperty(ref restoreArchive, value);
            OnPropertyChanged(nameof(CanRestore));
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
    public bool CanInspectIncomplete => HasIncompleteScan && !IsActive && !IsPending && !IsMissing;

    /// <summary>
    /// The source file no longer exists at <see cref="Path"/>. The row keeps its last results so the file can be restored or removed.
    /// </summary>
    public bool IsMissing
    {
        get => missing;
        set
        {
            if (SetProperty(ref missing, value))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusToolTip));
                OnPropertyChanged(nameof(SelectionEnabled));
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(CanRetryAnalysis));
                OnPropertyChanged(nameof(CanInspectIncomplete));
                OnPropertyChanged(nameof(DetailNotes));
            }
        }
    }

    /// <summary>A converted row keeps its result when Replace original moved the source away on purpose.</summary>
    private bool ShowsMissing => IsMissing && State != MediaRowState.Converted;
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
                OnPropertyChanged(nameof(CanOpenResult));
                OnPropertyChanged(nameof(ShowsResultComparison));
                OnPropertyChanged(nameof(DetailRows));
                OnPropertyChanged(nameof(ResultFiles));
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(Warning));
                OnPropertyChanged(nameof(HasWarning));
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusToolTip));
                OnPropertyChanged(nameof(DetailNotes));
            }
        }
    }
    public bool CanRetryAnalysis
    {
        get => canRetryAnalysis && !IsMissing;
        set => SetProperty(ref canRetryAnalysis, value);
    }
    public bool CanOpenResult => Result?.Output is not null && State is MediaRowState.Converted or MediaRowState.Restored;
   
    public string? Warning
    {
        get => State == MediaRowState.Skipped ? Notice : warning;
        set
        {
            SetProperty(ref warning, value);
            OnPropertyChanged(nameof(HasWarning));
            OnPropertyChanged(nameof(StatusToolTip));
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
        get => selectionEnabled && !IsMissing;
        set => SetProperty(ref selectionEnabled, value);
    }
    public bool IsPending
    {
        get => pending;
        set
        {
            SetProperty(ref pending, value);
            OnPropertyChanged(nameof(CanInspectIncomplete));
        }
    }
    public bool IsActive
    {
        get => active;
        set
        {
            SetProperty(ref active, value);
            OnPropertyChanged(nameof(CanInspectIncomplete));
        }
    }
    public string Status
    {
        get => ShowsMissing ? MissingStatus : status;
        set
        {
            SetProperty(ref status, value);
            OnPropertyChanged(nameof(CanOpenResult));
            OnPropertyChanged(nameof(ShowsResultComparison));
            OnPropertyChanged(nameof(StatusToolTip));
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
            OnPropertyChanged(nameof(Warning));
            OnPropertyChanged(nameof(HasWarning));
            OnPropertyChanged(nameof(DetailNotes));
            OnPropertyChanged(nameof(StatusToolTip));
        }
    }

    public string? StatusToolTip
    {
        get
        {
            if (ShowsMissing)
            {
                return MissingNote;
            }

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
            OnPropertyChanged(nameof(Classification));
            OnPropertyChanged(nameof(HasAnalysisError));
            OnPropertyChanged(nameof(DetailRows));
            OnPropertyChanged(nameof(DetailNotes));
            OnPropertyChanged(nameof(StatusToolTip));
        }
    }

    public OperationItemResult? Result
    {
        get => result;
        set
        {
            SetProperty(ref result, value);
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ResultFiles));
            OnPropertyChanged(nameof(CanOpenResult));
            OnPropertyChanged(nameof(ShowsResultComparison));
            OnPropertyChanged(nameof(DetailRows));
            OnPropertyChanged(nameof(StatusToolTip));
        }
    }

    /// <summary>Container metadata of the file the last conversion or restoration produced, when it could be read.</summary>
    public MediaAnalysis? ResultAnalysis
    {
        get => resultAnalysis;
        set
        {
            SetProperty(ref resultAnalysis, value);
            OnPropertyChanged(nameof(DetailRows));
        }
    }

    /// <summary>Details compare the original with the produced file side by side.</summary>
    public bool ShowsResultComparison => CanOpenResult;

    public MediaAnalysis? Analysis
    {
        get => analysis;
        set
        {
            SetProperty(ref analysis, value);
            OnPropertyChanged(nameof(IsProfile7));
            OnPropertyChanged(nameof(CanRestore));
            OnPropertyChanged(nameof(HasIncompleteScan));
            OnPropertyChanged(nameof(CanInspectIncomplete));
            OnPropertyChanged(nameof(Classification));
            OnPropertyChanged(nameof(DetailRows));
            OnPropertyChanged(nameof(DetailNotes));
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
        _ => ProfileName(Analysis.Media.Profile)
    };
    public bool HasAnalysisError => AnalysisError is not null;
    /// <summary>
    /// Labeled file details. With <see cref="ShowsResultComparison"/>, <see cref="MediaDetail.Result"/> holds the produced file's value
    /// and the file row shows names instead of full paths.
    /// </summary>
    public IReadOnlyList<MediaDetail> DetailRows
    {
        get
        {
            bool compare = ShowsResultComparison;
            var produced = compare ? ResultAnalysis : null;
            var file = compare
                ? new MediaDetail(FileLocationLabel, Name, System.IO.Path.GetFileName(Result!.Output!))
                : new MediaDetail(FileLocationLabel, Path);
            if (Analysis is not { } a)
            {
                return [file];
            }

            MediaDetail Compared(string label, string value, Func<MediaAnalysis, string> resultValue) =>
                new(label, value, !compare ? "" : produced is null ? "Unknown" : resultValue(produced));

            return
            [
                file,
                Compared("Size", FormatSize(a), FormatSize),
                Compared("Date modified", FormatDate(a), FormatDate),
                Compared("Length", FormatLength(a), FormatLength),
                Compared("Profile / Type", Classification, produced => ProfileName(produced.Media.Profile)),
                Compared("Resolution", FormatResolution(a), FormatResolution),
                Compared("Frame rate", FormatFrameRate(a), FormatFrameRate),
                new("Evidence", EvidenceName(a.Evidence.Method)),
                new("Frames", a.Evidence.Frames.ToString("N0")),
                new("Samples", $"{a.Evidence.SuccessfulSamples}/{a.Evidence.RequestedSamples}")
            ];
        }
    }

    public string DetailNotes => string.Join("\n\n", new[]
    {
        ShowsMissing ? MissingNote : null,
        AnalysisError is not null ? $"Analysis failed\n{AnalysisError}" : Analysis?.Reason,
        Analysis?.Evidence.SampleDiagnostics,
        HasIncompleteScan ? "Suggested action: Inspect. Standard inspection examines the full RPU metadata stream instead of short samples. It may take longer; missing metadata may still prevent classification." : null,
        Notice
    }.Where(text => !string.IsNullOrWhiteSpace(text)));
    public bool HasResult => Result is not null;
    /// <summary>
    /// The files the last operation produced or kept, each shown as its own link.
    /// Files the comparison table already links are left out.
    /// </summary>
    public IReadOnlyList<ResultFile> ResultFiles => Result is null ? [] : new[]
    {
        Result.Output is { } output && !ShowsResultComparison ? new ResultFile("Output", output) : null,
        Result.Original is { } original && !(ShowsResultComparison && string.Equals(original, Path, StringComparison.OrdinalIgnoreCase)) ? new ResultFile("Original", original) : null,
        Result.Archive is { } archive ? new ResultFile("Archive", archive) : null
    }.OfType<ResultFile>().ToArray();

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

    /// <summary>A conversion or restoration failed. Its message stays in <see cref="Result"/>; the file's analysis is unaffected.</summary>
    public void SetFailed(OperationItemResult itemResult)
    {
        ClearPlan();
        Result = itemResult;
        Status = $"{OperationName} failed";
        State = MediaRowState.Failed;
    }

    private static string FormatSize(MediaAnalysis analysis) => analysis.Media.Source.Length >= 1073741824
        ? $"{analysis.Media.Source.Length / 1073741824d:0.##} GiB" : $"{analysis.Media.Source.Length / 1048576d:0.##} MiB";

    private static string FormatDate(MediaAnalysis analysis) => analysis.Media.Source.LastWriteUtc.ToLocalTime().ToString("g");

    private static string FormatResolution(MediaAnalysis analysis) => $"{analysis.Media.Width} × {analysis.Media.Height}";

    private static string FormatFrameRate(MediaAnalysis analysis) => analysis.Media.FramesPerSecond is { } fps ? $"{fps:0.###} fps" : "Unknown";

    private static string FormatLength(MediaAnalysis analysis)
    {
        if (analysis.Media.DurationSeconds is { } seconds && double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds)
        {
            var duration = TimeSpan.FromSeconds(seconds);
            return $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return "Unknown";
    }

    private static string ProfileName(Domain.Media.DolbyVisionProfile profile) => profile switch
    {
        Domain.Media.DolbyVisionProfile.Profile7 => "Profile 7",
        Domain.Media.DolbyVisionProfile.Profile81 => "Profile 8.1",
        Domain.Media.DolbyVisionProfile.Profile5 => "Profile 5",
        Domain.Media.DolbyVisionProfile.None => "No Dolby Vision",
        _ => "Other / unknown profile"
    };

    private static string EvidenceName(AnalysisMethod method) => method switch
    {
        AnalysisMethod.SampledRpu => "Scan (sampled RPU)",
        AnalysisMethod.FullRpu => "Standard inspection (full RPU)",
        AnalysisMethod.DeepInspection => "Deep inspection (frame analysis)",
        _ => "Metadata only"
    };
}

public sealed record ResultFile(string Label, string Path);

/// <summary>One labeled detail; <see cref="Result"/> is empty unless the details compare an original with its produced file.</summary>
public sealed record MediaDetail(string Label, string Value, string Result = "");
