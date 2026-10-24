namespace DoViFixer.Application.Settings;

/// <summary>
/// Preferences to save through <see cref="SettingsService.SetPreferencesAsync"/>. A <see langword="null"/> property
/// leaves the saved value unchanged.
/// </summary>
public sealed record PreferenceChanges
{
    /// <summary>The folder for temporary files; a blank value restores the default.</summary>
    public string? TemporaryDirectory { get; init; }
    /// <summary>The folder converted files are written to; a blank value writes them next to the source.</summary>
    public string? OutputDirectory { get; init; }
    public bool? ReplaceOriginal { get; init; }
    public bool? CreateElArchive { get; init; }
    public bool? AutomaticallyScanAddedFiles { get; init; }
    public bool? UseCachedResults { get; init; }
    public AppTheme? Theme { get; init; }
    public bool? IncludeSimple { get; init; }
    public bool? ForceComplex { get; init; }
    public bool? ShowCompletionNotifications { get; init; }
    public int? ConversionWorkers { get; init; }
    public VerificationLevel? VerificationLevel { get; init; }
    public bool? RecycleDeletedOriginalsAndBackups { get; init; }
    public double? FreeSpaceMarginPercent { get; init; }
    public int? AnalysisWorkers { get; init; }
    public ToolProcessPriority? ToolProcessPriority { get; init; }
    public int? PipeBufferKiB { get; init; }
    public int? BrightnessMarginNits { get; init; }
    public int? AssumedPeakBrightnessNits { get; init; }
    public int? ScanSampleCount { get; init; }
    public double? ScanSampleSeconds { get; init; }
    public int? MinimumScanCoveragePercent { get; init; }
    public double? InspectionPeakPercentile { get; init; }
    public bool? SafeMode { get; init; }
    public bool? RemoveDolbyVision { get; init; }
    public int? FolderSearchDepth { get; init; }
    public bool? ShowSelectionCheckBoxes { get; init; }
    public bool? ShowInFileExplorerMenu { get; init; }
    public bool? CheckForUpdates { get; init; }

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> when a set value is outside its allowed range.</summary>
    public void Validate()
    {
        if (FolderSearchDepth is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(FolderSearchDepth), "Folder search depth must be between 0 and 100.");
        }
        if (ConversionWorkers is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(ConversionWorkers), "Conversion workers must be Automatic (0) or between 1 and 16.");
        }
        if (AnalysisWorkers is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(AnalysisWorkers));
        }
        if (PipeBufferKiB is < 64 or > 65536)
        {
            throw new ArgumentOutOfRangeException(nameof(PipeBufferKiB));
        }
        if (ToolProcessPriority is not null && !Enum.IsDefined(ToolProcessPriority.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(ToolProcessPriority));
        }
        if (VerificationLevel is not null && !Enum.IsDefined(VerificationLevel.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(VerificationLevel));
        }
        if (FreeSpaceMarginPercent is not null && (!double.IsFinite(FreeSpaceMarginPercent.Value) || FreeSpaceMarginPercent is < 0 or > 200))
        {
            throw new ArgumentOutOfRangeException(nameof(FreeSpaceMarginPercent));
        }
        if (BrightnessMarginNits is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(BrightnessMarginNits));
        }
        if (AssumedPeakBrightnessNits is < 100 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(AssumedPeakBrightnessNits));
        }
        new DoViFixer.Domain.Analysis.AnalysisOptions(ScanSampleCount: ScanSampleCount ?? 10, ScanSampleSeconds: ScanSampleSeconds ?? 1,
            MinimumScanCoveragePercent: MinimumScanCoveragePercent ?? 100, InspectionPeakPercentile: InspectionPeakPercentile ?? 100).ValidateSampling();
    }
}
