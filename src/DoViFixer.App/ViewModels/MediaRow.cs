using DoViFixer.Application.Operations;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Updates;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.App.ViewModels;
public sealed partial class MediaRow(string path) : ObservableObject
{
    #region Private fields

    private bool selectionEnabled = true;
    private string status = "Not checked";
    private bool canRetryAnalysis;
    private AnalysisMethod? lastAnalysisMethod = AnalysisMethod.SampledRpu;
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
    public bool CanRestore => CurrentFileAnalysis?.Media is { } media && RestoreService.IsRestoreBase(media) && RestoreArchive is not null && State != MediaRowState.Restored && !IsMissing;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(StatusTone), nameof(AnalysisText), nameof(ResultRecommendation), nameof(NextStepText), nameof(DetailRows))]
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
        && (HasIncompleteScan || Analysis.Verdict is AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel or AnalysisVerdict.FelUnclassified or AnalysisVerdict.Unknown);
    public bool CanInspectIncomplete => NeedsInspection && !CanOpenResult && !IsActive && !IsPending && !IsMissing;

    /// <summary>
    /// The source file no longer exists at <see cref="Path"/>. The row keeps its last results so the file can be restored or removed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(StatusTone), nameof(StatusToolTip), nameof(NextStepText), nameof(SelectionEnabled), nameof(CanRestore), nameof(CanRetryAnalysis), nameof(CanInspectIncomplete), nameof(DetailNotes))]
    public partial bool IsMissing { get; set; }

    /// <summary>A converted row keeps its result when Replace original moved the source away on purpose.</summary>
    private bool ShowsMissing => IsMissing && State != MediaRowState.Converted;
    public AnalysisMethod? LastAnalysisMethod
    {
        get => lastAnalysisMethod;
        set
        {
            if (SetProperty(ref lastAnalysisMethod, value))
            {
                OnPropertyChanged(nameof(AnalysisText));
            }
        }
    }
    public string AnalysisText
    {
        get
        {
            if (Analysis is { } analysis)
            {
                return MethodName(analysis.Evidence.Method);
            }

            return HasAnalysisError && LastAnalysisMethod is { } attempted
                ? $"{MethodName(attempted)} (failed)"
                : "—";
        }
    }
    public string ResultRecommendation
    {
        get
        {
            if (State != MediaRowState.Scanned || Analysis is not { } analysis)
            {
                return "";
            }

            if (analysis.Evidence.Method == AnalysisMethod.SampledRpu && analysis.Verdict is AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel)
            {
                return "Inspection recommended";
            }

            return analysis.Verdict is AnalysisVerdict.Unknown or AnalysisVerdict.FelUnclassified ? "Verification required" : "";
        }
    }
    public string? CurrentOperation { get; set; }
    public string OperationName => CurrentOperation ?? (LastAnalysisMethod is null ? "Conversion" : LastAnalysisMethod == AnalysisMethod.SampledRpu ? "Scan" : "Inspection");
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(CanOpenResult), nameof(CanInspectIncomplete), nameof(ShowsResultComparison), nameof(AnalysisText), nameof(ResultRecommendation), nameof(NextStepText), nameof(DetailRows), nameof(ResultFiles), nameof(CanRestore), nameof(Warning), nameof(HasWarning), nameof(Status), nameof(StatusTone), nameof(StatusToolTip), nameof(DetailNotes))]
    public partial MediaRowState State { get; private set; } = MediaRowState.NotScanned;
    public bool CanRetryAnalysis
    {
        get => canRetryAnalysis && !IsMissing;
        set
        {
            if (SetProperty(ref canRetryAnalysis, value))
            {
                OnPropertyChanged(nameof(NextStepText));
            }
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
    [NotifyPropertyChangedFor(nameof(CanInspectIncomplete), nameof(NextStepText))]
    public partial bool IsPending { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInspectIncomplete), nameof(StatusTone), nameof(NextStepText))]
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
                MediaRowState.PlanReady => MediaStatusTone.Neutral,
                MediaRowState.Cancelled => MediaStatusTone.VerifyRecommended,
                MediaRowState.Failed => MediaStatusTone.HighRisk,
                MediaRowState.Skipped => MediaStatusTone.Neutral,
                MediaRowState.Scanned => Analysis is null ? MediaStatusTone.Neutral : Analysis.Media.Profile switch
                {
                    Domain.Media.DolbyVisionProfile.Profile7 => RiskTone(Analysis),
                    Domain.Media.DolbyVisionProfile.Profile81 => CanRestore
                        ? MediaStatusTone.Safe
                        : MediaStatusTone.Neutral,
                    _ => MediaStatusTone.Neutral
                },
                _ => MediaStatusTone.Neutral
            };
        }
    }
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
    [NotifyPropertyChangedFor(nameof(Classification), nameof(FilterGroup), nameof(HasAnalysisError), nameof(NeedsInspection), nameof(CanInspectIncomplete), nameof(StatusTone), nameof(AnalysisText), nameof(ResultRecommendation), nameof(NextStepText), nameof(DetailRows), nameof(DetailNotes), nameof(StatusToolTip))]
    public partial string? AnalysisError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(HasResult), nameof(ResultFiles), nameof(CanOpenResult), nameof(CanInspectIncomplete), nameof(ShowsResultComparison), nameof(StatusTone), nameof(AnalysisText), nameof(ResultRecommendation), nameof(NextStepText), nameof(DetailRows), nameof(StatusToolTip))]
    public partial OperationItemResult? Result { get; set; }

    /// <summary>Container metadata of the file the last conversion or restoration produced, when it could be read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(CanRestore), nameof(AnalysisText), nameof(ResultRecommendation), nameof(NextStepText), nameof(DetailRows))]
    public partial MediaAnalysis? ResultAnalysis { get; set; }

    /// <summary>Details compare the original with the produced file side by side.</summary>
    public bool ShowsResultComparison => CanOpenResult;

    /// <summary>Replace original removed the source file from <see cref="Path"/>, so the row now describes the produced file.</summary>
    private bool ReplacesOriginal => State == MediaRowState.Converted
        && Result is not null
        && !string.Equals(Result.Original, Path, StringComparison.OrdinalIgnoreCase);

    private MediaAnalysis? CurrentFileAnalysis => ReplacesOriginal && ResultAnalysis is { } result ? result : Analysis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(IsProfile7), nameof(CanRestore), nameof(HasIncompleteScan), nameof(NeedsInspection), nameof(CanInspectIncomplete), nameof(Classification), nameof(FilterGroup), nameof(StatusTone), nameof(AnalysisText), nameof(ResultRecommendation), nameof(NextStepText), nameof(DetailRows), nameof(DetailNotes))]
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
                Compared("Format", DetailFormat(a), DetailFormat),
                .. HasDolbyVision(a) || produced is not null && HasDolbyVision(produced)
                    ? new[] { Compared("Profile", DetailProfile(a), DetailProfile) }
                    : Array.Empty<MediaDetail>(),
                Compared("Resolution", FormatResolution(a), FormatResolution),
                Compared("Frame rate", FormatFrameRate(a), FormatFrameRate),
                new(compare ? "Original risk" : "Risk", RiskDetailText(a), StatusTone: RiskTone(a)),
                new("Analysis", EvidenceName(a.Evidence.Method)),
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
        Notice,
        Analysis?.Media.Profile == Domain.Media.DolbyVisionProfile.Profile7
            ? "Conversion uses standard inspection or eligible saved scan results. Expert deep checks do not change conversion permission. Simple FEL requires Include Simple FEL; Complex FEL requires explicit approval."
            : null
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
                Status = AnalysisError is not null ? $"{OperationName} failed" : "Not checked";
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
            return "Not checked";
        }

        return RiskDetailText(analysis);
    }

    private static string MethodName(AnalysisMethod method) => method switch
    {
        AnalysisMethod.SampledRpu => "Scan",
        AnalysisMethod.FullRpu => "Inspection",
        AnalysisMethod.DeepInspection => "Deep inspection",
        _ => "Metadata only"
    };

    private string RiskDetailText(MediaAnalysis analysis)
    {
        if (AnalysisError is not null || analysis.Verdict == AnalysisVerdict.AnalysisFailed)
        {
            return "Check failed";
        }

        if (analysis.Media.Profile != Domain.Media.DolbyVisionProfile.Profile7)
        {
            return analysis.Media.Profile switch
            {
                Domain.Media.DolbyVisionProfile.Profile81 => "Already compatible",
                Domain.Media.DolbyVisionProfile.None when analysis.Media.IsHdr is not null => "No conversion needed",
                Domain.Media.DolbyVisionProfile.Profile5 or Domain.Media.DolbyVisionProfile.Other => "Unsupported profile",
                _ => "Unknown format"
            };
        }

        if (analysis.Evidence.Method == AnalysisMethod.MetadataOnly)
        {
            return "Not checked";
        }

        return analysis.Media.Profile switch
        {
            Domain.Media.DolbyVisionProfile.Profile7 => analysis.Verdict switch
            {
                AnalysisVerdict.Mel => analysis.Evidence.Method == AnalysisMethod.SampledRpu ? "Likely safe" : "Safe",
                AnalysisVerdict.SimpleFel => "Low risk",
                AnalysisVerdict.ComplexFel => "High risk",
                _ => "Risk unknown"
            },
            _ => "Not applicable"
        };
    }

    private MediaStatusTone RiskTone(MediaAnalysis analysis)
    {
        if (AnalysisError is not null || analysis.Verdict == AnalysisVerdict.AnalysisFailed)
        {
            return MediaStatusTone.HighRisk;
        }

        if (analysis.Media.Profile != Domain.Media.DolbyVisionProfile.Profile7 || analysis.Evidence.Method == AnalysisMethod.MetadataOnly)
        {
            return MediaStatusTone.Neutral;
        }

        return analysis.Verdict switch
        {
            AnalysisVerdict.Mel => analysis.Evidence.Method == AnalysisMethod.SampledRpu ? MediaStatusTone.VerifyRecommended : MediaStatusTone.Safe,
            AnalysisVerdict.SimpleFel => MediaStatusTone.LowRisk,
            AnalysisVerdict.ComplexFel => MediaStatusTone.HighRisk,
            AnalysisVerdict.NotApplicable => MediaStatusTone.Neutral,
            _ => MediaStatusTone.VerifyRecommended
        };
    }

    public string NextStepText
    {
        get
        {
            if (IsMissing || IsActive || IsPending)
            {
                return "";
            }

            if (CanOpenResult)
            {
                return "Open output";
            }

            if (CanRestore)
            {
                return "Restore to DV7";
            }

            if (CanRetryAnalysis)
            {
                return "Retry analysis";
            }

            if (Analysis is null || Analysis.Evidence.Method == AnalysisMethod.MetadataOnly && IsProfile7)
            {
                return "Scan";
            }

            if (CanInspectIncomplete)
            {
                return "Inspect";
            }

            return IsProfile7 && Analysis.Verdict is AnalysisVerdict.Mel or AnalysisVerdict.SimpleFel ? "Convert" : "";
        }
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
            AnalysisVerdict.Mel => analysis.Evidence.Method == AnalysisMethod.SampledRpu
                ? "Likely safe. Sampled metadata indicates MEL. Inspection is recommended to check the complete metadata stream. Output verification still applies."
                : "Safe: MEL carries no enhancement-layer picture data. Output verification still applies; playback compatibility is not guaranteed.",
            AnalysisVerdict.SimpleFel => analysis.Evidence.Method switch
            {
                AnalysisMethod.SampledRpu => "Suggested action: Inspect. Sampled metadata indicates Simple FEL. Run standard Inspect to verify the full RPU stream before converting.",
                AnalysisMethod.FullRpu => "Low risk. Complete metadata suggests limited brightness expansion. This is an estimate, not proof that FEL removal preserves the picture. Enable 'Include Simple FEL' in Settings to convert. Keep the original if picture preservation matters.",
                AnalysisMethod.DeepInspection => "Low risk. Expert frame analysis found no brightness expansion above its threshold. This does not prove safe FEL removal. Picture differences remain possible. Enable 'Include Simple FEL' in Settings to convert.",
                _ => null
            },
            AnalysisVerdict.ComplexFel => "High risk. Analysis indicates possible brightness expansion. FEL removal discards enhancement-layer picture data. Keep the original; conversion requires explicit approval.",
            AnalysisVerdict.FelUnclassified or AnalysisVerdict.Unknown => "Risk unknown. Available evidence cannot establish the risk of FEL removal. Keep the original. Inspection can check the complete metadata stream, but missing proof may remain.",
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

    private static bool HasDolbyVision(MediaAnalysis analysis) => analysis.Media.Profile is
        Domain.Media.DolbyVisionProfile.Profile7 or Domain.Media.DolbyVisionProfile.Profile81 or
        Domain.Media.DolbyVisionProfile.Profile5 or Domain.Media.DolbyVisionProfile.Other
        || analysis.Media.DolbyVisionProfileNumber is not null;

    private static string DetailFormat(MediaAnalysis analysis) => HasDolbyVision(analysis) ? "Dolby Vision" : FormatName(analysis.Media);

    private static string DetailProfile(MediaAnalysis analysis) => analysis.Media.Profile switch
    {
        Domain.Media.DolbyVisionProfile.Profile7 => analysis.Verdict switch
        {
            AnalysisVerdict.SimpleFel => "7 · Simple FEL",
            AnalysisVerdict.ComplexFel => "7 · Complex FEL",
            _ => analysis.Evidence.Layer switch
            {
                Domain.Media.EnhancementLayer.Mel => "7 · MEL",
                Domain.Media.EnhancementLayer.Fel => "7 · FEL (unclassified)",
                _ => "7"
            }
        },
        Domain.Media.DolbyVisionProfile.Profile81 => "8.1",
        Domain.Media.DolbyVisionProfile.Profile5 => "5",
        _ => HasDolbyVision(analysis) ? analysis.Media.DolbyVisionProfileNumber?.ToString() ?? "Unknown" : ""
    };

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
public sealed record MediaDetail(string Label, string Value, string Result = "", MediaStatusTone StatusTone = MediaStatusTone.None);
