using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;

namespace DoViFixer.App.ViewModels;
public sealed class MediaRow(string path) : BindableBase
{
    private bool selected = true;
    private bool selectionEnabled = true;
    private bool pending;
    private bool active;
    private string status = "Ready";
    private string output = "";
    private MediaAnalysis? analysis;
    private FileResult? result;
    private string? analysisError;
    private string notice = "";
    public string Path
    {
        get;
    }
    = path;
    public string Name => System.IO.Path.GetFileName(Path);
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
        set => SetProperty(ref pending, value);
    }
    public bool IsActive
    {
        get => active;
        set => SetProperty(ref active, value);
    }
    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
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
        }
    }

    public MediaAnalysis? Analysis
    {
        get => analysis;
        set
        {
            SetProperty(ref analysis, value);
            RaisePropertyChanged(nameof(Classification));
            RaisePropertyChanged(nameof(ClassificationColor));
            RaisePropertyChanged(nameof(Details));
        }
    }

    public FileResult? Result
    {
        get => result;
        set
        {
            SetProperty(ref result, value);
            RaisePropertyChanged(nameof(ResultDetails));
        }
    }

    public string Classification => AnalysisError is not null ? "Analysis failed" : Analysis is null ? "Analysis pending" : Analysis.Verdict switch
    {
        AnalysisVerdict.Mel => "Profile 7 · MEL",
        AnalysisVerdict.SimpleFel => "Profile 7 · Simple FEL",
        AnalysisVerdict.ComplexFel => "Profile 7 · Complex FEL",
        AnalysisVerdict.AnalysisFailed => "Analysis failed",
        AnalysisVerdict.Unknown => "Unknown",
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
        AnalysisVerdict.Unknown => "#FFD166",
        _ => "#A6B6C3"
    };
    public string Details => AnalysisError is not null ? $"{Path}\n\nAnalysis failed\n{AnalysisError}" : Analysis is not
    {
    }
    a ? Path + "\n" + Notice : $"{Path}\n\n{Classification}\n{a.Media.Width} × {a.Media.Height} · {a.Media.FramesPerSecond:0.###} fps\n" + $"{TimeSpan.FromSeconds(a.Media.DurationSeconds ?? 0):g} · {a.Media.Source.Length / 1073741824d:0.00} GiB\n\n" + $"Evidence: {EvidenceName(a.Evidence.Method)}\nFrames: {a.Evidence.Frames:N0}\nSamples: {a.Evidence.SuccessfulSamples}/{a.Evidence.RequestedSamples}\n\n{a.Reason}\n\n{Notice}";
    public string ResultDetails => Result is null ? "" : $"Last conversion result\n{Result.Status}\n{Result.Output}\n{Result.Message}";

    private static string EvidenceName(AnalysisMethod method) => method switch
    {
        AnalysisMethod.SampledRpu => "Sampled RPU metadata",
        AnalysisMethod.FullRpu => "Full RPU inspection",
        AnalysisMethod.DeepInspection => "Deep frame inspection",
        _ => "Metadata only"
    };
}
