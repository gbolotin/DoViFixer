using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DoViFixer.Application.Dependencies;

namespace DoViFixer.Application.Settings;
public sealed record UserSettings
{
    public MediaListLayout? MediaListLayout { get; init; }
    public int BrightnessMarginNits { get; init; } = 50;
    public int AssumedPeakBrightnessNits { get; init; } = 1000;
    public int ScanSampleCount { get; init; } = 10;
    public double ScanSampleSeconds { get; init; } = 1;
    public int MinimumScanCoveragePercent { get; init; } = 100;
    public double InspectionPeakPercentile { get; init; } = 100;
    [JsonIgnore]
    public DoViFixer.Domain.Analysis.AnalysisOptions AnalysisOptions => new(BrightnessMarginNits, AssumedPeakBrightnessNits, ScanSampleCount, ScanSampleSeconds, MinimumScanCoveragePercent, InspectionPeakPercentile);
    public bool AutomaticallyScanAddedFiles
    {
        get;
        init;
    }
    = true;
    public bool UseCachedResults
    {
        get;
        init;
    }
    = true;
    public ImmutableDictionary<NativeTool, string> ToolPaths
    {
        get;
        init;
    }
    = ImmutableDictionary<NativeTool, string>.Empty;
    public string? TemporaryDirectory
    {
        get;
        init;
    }
    public string? OutputDirectory
    {
        get;
        init;
    }
    public bool ReplaceOriginal
    {
        get;
        init;
    }
    public int FolderSearchDepth { get; init; } = 100;
    public bool SafeMode { get; init; }
    public bool RemoveDolbyVision { get; init; }
    public bool CreateElArchive
    {
        get;
        init;
    }
    /// <summary>Maximum files converted at once; zero selects the hardware recommendation. Additional files wait in the batch queue.</summary>
    public int ConversionWorkers { get; init; }
    /// <summary>Samples analyzed at once within one file. Zero selects the hardware recommendation.</summary>
    public int AnalysisWorkers { get; init; }
    public ToolProcessPriority ToolProcessPriority { get; init; } = ToolProcessPriority.BelowNormal;
    public int PipeBufferKiB { get; init; } = 4096;
    public VerificationLevel VerificationLevel { get; init; } = VerificationLevel.FrameCountWithRecount;
    public bool RecycleDeletedOriginalsAndBackups { get; init; } = true;
    public double FreeSpaceMarginPercent { get; init; } = 20;
    public bool IncludeSimple
    {
        get;
        init;
    }
    public bool ForceComplex
    {
        get;
        init;
    }
    [JsonIgnore]
    public bool AllowFel
    {
        get => IncludeSimple || ForceComplex;
        init
        {
            IncludeSimple = value;
            ForceComplex = value;
        }
    }
    /// <summary>Whether a Windows notification reports an operation that finishes while the window is not active.</summary>
    public bool ShowCompletionNotifications
    {
        get;
        init;
    }
    = true;
    public AppTheme Theme
    {
        get;
        init;
    }
    = AppTheme.System;
    /// <summary>Whether each media list row has a check box; otherwise the list selects rows as File Explorer does.</summary>
    public bool ShowSelectionCheckBoxes { get; init; }
    /// <summary>The preset applied last, so changed settings can say which preset they started from.</summary>
    public string? LastAppliedPreset { get; init; }
}
