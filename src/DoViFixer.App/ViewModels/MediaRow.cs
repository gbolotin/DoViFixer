using DoViFixer.Application.Operations;
using DoViFixer.Application.Updates;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.App.ViewModels;
public sealed partial class MediaRow(string path) : ObservableObject
{
    #region Private fields

    private bool selectionEnabled = true;
    private string status = "Not scanned";
    private bool canRetryAnalysis;
    private string? warning;

    #endregion

    #region Public fields

    public const string FileLocationLabel = "File location";
    public const string MissingStatus = "File not found";
    public static readonly string MissingNote = $"File not found. It was deleted, moved or renamed outside {ApplicationTitle.Name}. Restore it to this location, or remove it from the list.";
    public string Path { get; } = path;
    public string? LibraryFingerprint { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryHistory))]
    public partial string LibraryHistory { get; set; } = "";
    public bool HasLibraryHistory => !string.IsNullOrWhiteSpace(LibraryHistory);
    public string Name => System.IO.Path.GetFileName(Path);
    public ProgressViewModel Progress { get; } = new();
    public bool IsProfile7 => Analysis?.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7;
    public bool CanRestore => CurrentFileAnalysis?.Media.Profile == Domain.Media.DolbyVisionProfile.Profile81 && RestoreArchive is not null && State != MediaRowState.Restored && !IsMissing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(StatusTone), nameof(ActionText), nameof(ShowsActionText), nameof(DetailRows))]
    public partial string? RestoreArchive { get; set; }
    [ObservableProperty]
    public partial bool IsCancellationRequested { get; set; }
    public bool HasIncompleteScan => Analysis is not null
        && Analysis.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7
        && Analysis.Verdict == AnalysisVerdict.Unknown
        && Analysis.Evidence.Method == AnalysisMethod.SampledRpu
        && (Analysis.Evidence.Frames <= 0 || Analysis.Evidence.SuccessfulSamples < Analysis.Evidence.RequestedSamples);
    public bool NeedsInspection => AnalysisError is null
        && Analysis is not null
        && Analysis.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7
        && Analysis.Evidence.Method == AnalysisMethod.SampledRpu
        && (HasIncompleteScan || Analysis.Verdict is AnalysisVerdict.SimpleFel or AnalysisVerdict.FelUnclassified or AnalysisVerdict.Unknown);
    public bool CanInspectIncomplete => NeedsInspection && !CanOpenResult && !IsActive && !IsPending && !IsMissing;

    /// <summary>
    /// The source file no longer exists at <see cref="Path"/>. The row keeps its last results so the file can be restored or removed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(StatusTone), nameof(StatusToolTip), nameof(ActionText), nameof(ShowsActionText), nameof(SelectionEnabled), nameof(CanRestore), nameof(CanRetryAnalysis), nameof(CanInspectIncomplete), nameof(DetailNotes))]
    public partial bool IsMissing { get; set; }

    /// <summary>A converted row keeps its result when Replace original moved the source away on purpose.</summary>
    private bool ShowsMissing => IsMissing && State != MediaRowState.Converted;
    public AnalysisMethod? LastAnalysisMethod { get; set;} = AnalysisMethod.SampledRpu;
    public string? CurrentOperation { get; set; }
    public string OperationName => CurrentOperation ?? (LastAnalysisMethod is null ? "Conversion" : LastAnalysisMethod == AnalysisMethod.SampledRpu ? "Scan" : "Inspection");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(CanOpenResult), nameof(CanInspectIncomplete), nameof(ShowsResultComparison), nameof(DetailRows), nameof(ResultFiles), nameof(CanRestore), nameof(Warning), nameof(HasWarning), nameof(Status), nameof(StatusTone), nameof(StatusToolTip), nameof(ActionText), nameof(ShowsActionText), nameof(DetailNotes))]
    public partial MediaRowState State { get; private set; } = MediaRowState.NotScanned;
    public bool CanRetryAnalysis
    {
        get => canRetryAnalysis && !IsMissing;
        set
        {
            SetProperty(ref canRetryAnalysis, value);
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(ShowsActionText));
        }
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
    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;
    public bool SelectionEnabled
    {
        get => selectionEnabled && !IsMissing;
        set => SetProperty(ref selectionEnabled, value);
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInspectIncomplete), nameof(ActionText), nameof(ShowsActionText))]
    public partial bool IsPending { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInspectIncomplete), nameof(StatusTone), nameof(ActionText), nameof(ShowsActionText))]
    public partial bool IsActive { get; set; }
    public string Status
    {
        get => ShowsMissing ? MissingStatus : status;
        set
        {
            SetProperty(ref status, value);
            OnPropertyChanged(nameof(CanOpenResult));
            OnPropertyChanged(nameof(ShowsResultComparison));
            OnPropertyChanged(nameof(StatusTone));
            OnPropertyChanged(nameof(StatusToolTip));
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(ShowsActionText));
        }
    }
    public MediaStatusTone StatusTone
    {
        get
        {
            if (ShowsMissing || IsActive || string.IsNullOrWhiteSpace(Status))
            {
                return MediaStatusTone.None;
            }

            if (HasAnalysisError)
            {
                return MediaStatusTone.HighRisk;
            }

            return State switch
            {
                MediaRowState.Converted => Result?.Status == OperationStatus.Partial
                    ? MediaStatusTone.VerifyRecommended
                    : MediaStatusTone.Safe,
                MediaRowState.Restored => MediaStatusTone.Safe,
                MediaRowState.PlanReady => MediaStatusTone.LowRisk,
                MediaRowState.Cancelled => MediaStatusTone.VerifyRecommended,
                MediaRowState.Failed => MediaStatusTone.HighRisk,
                MediaRowState.Skipped => MediaStatusTone.Neutral,
                MediaRowState.Scanned => Analysis is null ? MediaStatusTone.Neutral : Analysis.Media.Profile switch
                {
                    Domain.Media.DolbyVisionProfile.Profile7 => Analysis.Verdict switch
                    {
                        AnalysisVerdict.Mel => MediaStatusTone.Safe,
                        AnalysisVerdict.SimpleFel => Analysis.Evidence.Method switch
                        {
                            AnalysisMethod.FullRpu => MediaStatusTone.LowRisk,
                            AnalysisMethod.DeepInspection => MediaStatusTone.Safe,
                            _ => MediaStatusTone.VerifyRecommended
                        },
                        AnalysisVerdict.ComplexFel or AnalysisVerdict.AnalysisFailed => MediaStatusTone.HighRisk,
                        AnalysisVerdict.NotApplicable => MediaStatusTone.Neutral,
                        _ => MediaStatusTone.VerifyRecommended
                    },
                    Domain.Media.DolbyVisionProfile.Profile81 => CanRestore
                        ? MediaStatusTone.Safe
                        : MediaStatusTone.Neutral,
                    _ => MediaStatusTone.Neutral
                },
                _ => MediaStatusTone.Neutral
            };
        }
    }
    public string ActionText
    {
        get
        {
            if (ShowsMissing)
            {
                return "";
            }

            if (IsActive)
            {
                return "Cancel";
            }

            if (IsPending)
            {
                return "Skip";
            }

            if (CanRetryAnalysis)
            {
                return "Retry";
            }

            if (CanInspectIncomplete)
            {
                return "Inspect";
            }

            if (HasAnalysisError)
            {
                return "Retry";
            }

            return State switch
            {
                MediaRowState.Converted => CanRestore ? "Restore to DV7" : "No action needed",
                MediaRowState.Restored => "No action needed",
                MediaRowState.PlanReady => "Convert",
                MediaRowState.Skipped => "Review settings",
                MediaRowState.Cancelled => "Retry",
                MediaRowState.Failed => "Check details",
                MediaRowState.Scanned => Analysis is null ? "Scan" : Analysis.Media.Profile switch
                {
                    Domain.Media.DolbyVisionProfile.Profile7 => Analysis.Verdict switch
                    {
                        AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel => "Convert",
                        AnalysisVerdict.ComplexFel => "Do not convert",
                        AnalysisVerdict.AnalysisFailed => "Retry",
                        _ => Analysis.Evidence.Method == AnalysisMethod.MetadataOnly ? "Scan" : "Do not convert"
                    },
                    Domain.Media.DolbyVisionProfile.Profile81 => CanRestore
                        ? "Restore to DV7"
                        : "No action needed",
                    _ => "No action needed"
                },
                _ => "Scan"
            };
        }
    }
    public bool ShowsActionText => !ShowsMissing
        && !IsActive
        && !IsPending
        && !CanRetryAnalysis
        && !CanInspectIncomplete
        && !string.IsNullOrWhiteSpace(ActionText);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(DetailNotes), nameof(StatusToolTip))]
    public partial string Notice { get; set; } = "";

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Classification), nameof(FilterGroup), nameof(HasAnalysisError), nameof(NeedsInspection), nameof(CanInspectIncomplete), nameof(StatusTone), nameof(ActionText), nameof(ShowsActionText), nameof(DetailRows), nameof(DetailNotes), nameof(StatusToolTip))]
    public partial string? AnalysisError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(HasResult), nameof(ResultFiles), nameof(CanOpenResult), nameof(CanInspectIncomplete), nameof(ShowsResultComparison), nameof(StatusTone), nameof(ActionText), nameof(ShowsActionText), nameof(DetailRows), nameof(StatusToolTip))]
    public partial OperationItemResult? Result { get; set; }

    /// <summary>Container metadata of the file the last conversion or restoration produced, when it could be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(CanRestore), nameof(ActionText), nameof(ShowsActionText), nameof(DetailRows))]
    public partial MediaAnalysis? ResultAnalysis { get; set; }

    /// <summary>Details compare the original with the produced file side by side.</summary>
    public bool ShowsResultComparison => CanOpenResult;

    /// <summary>Replace original removed the source file from <see cref="Path"/>, so the row now describes the produced file.</summary>
    private bool ReplacesOriginal => State == MediaRowState.Converted
        && Result is not null
        && !string.Equals(Result.Original, Path, StringComparison.OrdinalIgnoreCase);

    private MediaAnalysis? CurrentFileAnalysis => ReplacesOriginal && ResultAnalysis is { } result ? result : Analysis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(IsProfile7), nameof(CanRestore), nameof(HasIncompleteScan), nameof(NeedsInspection), nameof(CanInspectIncomplete), nameof(Classification), nameof(FilterGroup), nameof(StatusTone), nameof(ActionText), nameof(ShowsActionText), nameof(DetailRows), nameof(DetailNotes))]
    public partial MediaAnalysis? Analysis { get; set; }

    /// <summary>The list filter that shows this file. Files not scanned yet and files whose analysis failed share one group.</summary>
    public MediaFileFilter FilterGroup => AnalysisError is not null || Analysis is null ? MediaFileFilter.NotScanned : Analysis.Media.Profile switch
    {
        Domain.Media.DolbyVisionProfile.Profile7 => MediaFileFilter.Profile7,
        Domain.Media.DolbyVisionProfile.Profile81 => MediaFileFilter.Profile81,
        Domain.Media.DolbyVisionProfile.None => MediaFileFilter.NoDolbyVision,
        _ => MediaFileFilter.OtherProfiles
    };

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
    public string Format => CurrentFileAnalysis is { } current ? FormatAnalysis(current) : "";

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
                Compared("Format", FormatAnalysis(a), FormatAnalysis),
                Compared("Resolution", FormatResolution(a), FormatResolution),
                Compared("Frame rate", FormatFrameRate(a), FormatFrameRate),
                new("Risk", RiskDetailText(a)),
                new("Evidence", EvidenceName(a.Evidence.Method)),
                new("Next step", NextStepDetailText(a)),
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
        GuidanceNote(),
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

    partial void OnAnalysisChanged(MediaAnalysis? value)
    {
        if (!IsActive && !IsPending && State is MediaRowState.NotScanned or MediaRowState.Scanned)
        {
            if (value is null)
            {
                State = MediaRowState.NotScanned;
                Status = AnalysisError is not null ? $"{OperationName} failed" : "Not scanned";
            }
            else
            {
                State = MediaRowState.Scanned;
                Status = ScannedStatus();
            }
        }
    }

    partial void OnRestoreArchiveChanged(string? value)
    {
        if (!IsActive && !IsPending && State == MediaRowState.Scanned)
        {
            Status = ScannedStatus();
        }
    }

    public void SetPlan(string? warning)
    {
        Warning = warning;
        Status = "Ready to convert";
        State = MediaRowState.PlanReady;
    }

    public void ClearPlan()
    {
        Warning = null;
        if (State == MediaRowState.PlanReady)
        {
            State = MediaRowState.Scanned;
            Status = ScannedStatus();
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
        State = MediaRowState.Scanned;
        Status = ScannedStatus();
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

    /// <summary>A scan or inspection failed; the row keeps no analysis result.</summary>
    public void SetAnalysisFailed(string? message)
    {
        AnalysisError = message;
        Status = $"{OperationName} failed";
        State = MediaRowState.Failed;
    }

    /// <summary>A conversion or restoration failed. Its message stays in <see cref="Result"/>; the file's analysis is unaffected.</summary>
    public void SetFailed(OperationItemResult itemResult)
    {
        ClearPlan();
        Result = itemResult;
        Status = $"{OperationName} failed";
        State = MediaRowState.Failed;
    }

    private string ScannedStatus()
    {
        if (AnalysisError is not null)
        {
            return $"{OperationName} failed";
        }

        if (Analysis is not { } analysis)
        {
            return "Not scanned";
        }

        string method = MethodStatusText(analysis.Evidence.Method);
        return analysis.Media.Profile switch
        {
            Domain.Media.DolbyVisionProfile.Profile7 => analysis.Verdict switch
            {
                AnalysisVerdict.Mel => $"Safe · {method}",
                AnalysisVerdict.SimpleFel => analysis.Evidence.Method switch
                {
                    AnalysisMethod.FullRpu => $"Low risk · {method}",
                    AnalysisMethod.DeepInspection => $"Safe · {method}",
                    _ => $"Verify recommended · {method}"
                },
                AnalysisVerdict.ComplexFel => $"High risk · {method}",
                AnalysisVerdict.AnalysisFailed => $"{OperationName} failed",
                _ => $"Verify recommended · {method}"
            },
            Domain.Media.DolbyVisionProfile.Profile81 => CanRestore
                ? $"Backup paired · {method}"
                : $"Compatible · {method}",
            Domain.Media.DolbyVisionProfile.None => $"No Dolby Vision · {method}",
            _ => $"Compatible · {method}"
        };
    }

    private static string MethodStatusText(AnalysisMethod method) => method switch
    {
        AnalysisMethod.FullRpu => "Inspected",
        AnalysisMethod.DeepInspection => "Deep inspected",
        _ => "Scanned"
    };

    private string RiskDetailText(MediaAnalysis analysis)
    {
        if (AnalysisError is not null || analysis.Verdict == AnalysisVerdict.AnalysisFailed)
        {
            return "Analysis failed";
        }

        return analysis.Media.Profile switch
        {
            Domain.Media.DolbyVisionProfile.Profile7 => analysis.Verdict switch
            {
                AnalysisVerdict.Mel => "Safe",
                AnalysisVerdict.SimpleFel => analysis.Evidence.Method switch
                {
                    AnalysisMethod.FullRpu => "Low risk",
                    AnalysisMethod.DeepInspection => "Safe",
                    _ => "Verify recommended"
                },
                AnalysisVerdict.ComplexFel => "High risk",
                _ => "Verify recommended"
            },
            Domain.Media.DolbyVisionProfile.Profile81 => CanRestore ? "Safe (backup paired)" : "Compatible",
            Domain.Media.DolbyVisionProfile.None => "No Dolby Vision",
            _ => "Compatible"
        };
    }

    private string NextStepDetailText(MediaAnalysis analysis)
    {
        if (State == MediaRowState.Converted)
        {
            return "Converted";
        }

        if (State == MediaRowState.Restored)
        {
            return "Restored";
        }

        if (AnalysisError is not null || analysis.Verdict == AnalysisVerdict.AnalysisFailed)
        {
            return "Retry analysis";
        }

        return analysis.Media.Profile switch
        {
            Domain.Media.DolbyVisionProfile.Profile7 => analysis.Verdict switch
            {
                AnalysisVerdict.Mel => "Convert",
                AnalysisVerdict.SimpleFel => analysis.Evidence.Method == AnalysisMethod.SampledRpu
                    ? "Inspect (verify full RPU)"
                    : "Convert",
                AnalysisVerdict.ComplexFel => "Do not convert",
                _ => analysis.Evidence.Method == AnalysisMethod.SampledRpu
                    ? "Inspect (verify full RPU)"
                    : "Do not convert"
            },
            Domain.Media.DolbyVisionProfile.Profile81 => CanRestore ? "Restore to DV7 (optional)" : "No action needed",
            _ => "No action needed"
        };
    }

    private string? GuidanceNote()
    {
        if (AnalysisError is not null || Analysis is not { } analysis)
        {
            return null;
        }

        if (HasIncompleteScan)
        {
            return "Suggested action: Inspect. Standard inspection examines the full RPU metadata stream instead of short samples. It may take longer; missing metadata may still prevent classification.";
        }

        if (analysis.Media.Profile != Domain.Media.DolbyVisionProfile.Profile7)
        {
            return null;
        }

        return analysis.Verdict switch
        {
            AnalysisVerdict.Mel => "Safe to convert. Minimal Enhancement Layer (MEL) carries no extra picture data, so converting to Profile 8.1 is lossless.",
            AnalysisVerdict.SimpleFel => analysis.Evidence.Method switch
            {
                AnalysisMethod.SampledRpu => "Suggested action: Inspect. Quick scan sampled 10 snippets and found Simple FEL. Run standard Inspect to verify the full RPU stream before converting.",
                AnalysisMethod.FullRpu => "Low risk. Full RPU inspection confirmed Simple FEL within base-layer brightness limits. Enable 'Include Simple FEL' in Settings to convert. (Deep Inspect is a slow frame-by-frame check only needed by experts.)",
                AnalysisMethod.DeepInspection => "Safe to convert. Expert deep inspection verified frames across the movie. Enable 'Include Simple FEL' in Settings to convert.",
                _ => null
            },
            AnalysisVerdict.ComplexFel => "High risk — do not convert. Full Enhancement Layer (FEL) expands brightness beyond the base layer; converting discards that picture data.",
            AnalysisVerdict.FelUnclassified or AnalysisVerdict.Unknown => "Verify recommended. RPU metadata did not provide brightness limits to classify FEL safety. Standard Inspect checks the full RPU stream; Deep Inspect decodes video frames (slow, for experts only).",
            _ => null
        };
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

    private static string FormatAnalysis(MediaAnalysis analysis) => analysis.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7
        ? analysis.Evidence.Layer switch
        {
            Domain.Media.EnhancementLayer.Mel => "Dolby Vision Profile 7 · MEL",
            Domain.Media.EnhancementLayer.Fel => "Dolby Vision Profile 7 · FEL",
            _ => "Dolby Vision Profile 7"
        }
        : FormatName(analysis.Media);

    private static string FormatName(Domain.Media.MediaInfo media) => media.Profile switch
    {
        Domain.Media.DolbyVisionProfile.Profile7 => "Dolby Vision Profile 7",
        Domain.Media.DolbyVisionProfile.Profile81 => "Dolby Vision Profile 8.1",
        Domain.Media.DolbyVisionProfile.Profile5 => "Dolby Vision Profile 5",
        Domain.Media.DolbyVisionProfile.None => media.IsHdr switch { true => "HDR", false => "SDR", _ => "Unknown" },
        _ => media.DolbyVisionProfileNumber is { } number ? $"Dolby Vision Profile {number}" : "Dolby Vision (unknown profile)"
    };

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
        AnalysisMethod.DeepInspection => "Deep inspection (expert frame check)",
        _ => "Metadata only"
    };
}

public sealed record ResultFile(string Label, string Path);

/// <summary>One labeled detail; <see cref="Result"/> is empty unless the details compare an original with its produced file.</summary>
public sealed record MediaDetail(string Label, string Value, string Result = "");
