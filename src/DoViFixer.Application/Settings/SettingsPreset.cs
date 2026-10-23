using DoViFixer.Domain.Analysis;

namespace DoViFixer.Application.Settings;

/// <summary>A named set of the settings that describe a way of working.</summary>
/// <param name="IsBuiltIn">Built-in presets ship with the app and cannot be replaced or deleted.</param>
public sealed record SettingsPreset(string Name, string Description, PresetValues Values, bool IsBuiltIn = false);

/// <summary>
/// Every setting a preset holds: how files are examined, converted, checked and kept. A preset always sets all of them,
/// so applying one gives the same result whatever was applied before.
/// </summary>
/// <remarks>
/// Folders, worker counts, process priority, tool paths and appearance belong to the computer or the window rather than
/// to a way of working, so presets never change them.
/// </remarks>
public sealed record PresetValues(
    int ScanSampleCount,
    double ScanSampleSeconds,
    int MinimumScanCoveragePercent,
    double InspectionPeakPercentile,
    int BrightnessMarginNits,
    int AssumedPeakBrightnessNits,
    bool IncludeSimple,
    bool ForceComplex,
    bool CreateElArchive,
    bool RemoveDolbyVision,
    bool SafeMode,
    bool ReplaceOriginal,
    VerificationLevel VerificationLevel,
    bool RecycleDeletedOriginalsAndBackups,
    double FreeSpaceMarginPercent)
{
    public static PresetValues From(UserSettings settings) => new(
        settings.ScanSampleCount,
        settings.ScanSampleSeconds,
        settings.MinimumScanCoveragePercent,
        settings.InspectionPeakPercentile,
        settings.BrightnessMarginNits,
        settings.AssumedPeakBrightnessNits,
        settings.IncludeSimple,
        settings.ForceComplex,
        settings.CreateElArchive,
        settings.RemoveDolbyVision,
        settings.SafeMode,
        settings.ReplaceOriginal,
        settings.VerificationLevel,
        settings.RecycleDeletedOriginalsAndBackups,
        settings.FreeSpaceMarginPercent);

    public UserSettings ApplyTo(UserSettings settings) => settings with
    {
        ScanSampleCount = ScanSampleCount,
        ScanSampleSeconds = ScanSampleSeconds,
        MinimumScanCoveragePercent = MinimumScanCoveragePercent,
        InspectionPeakPercentile = InspectionPeakPercentile,
        BrightnessMarginNits = BrightnessMarginNits,
        AssumedPeakBrightnessNits = AssumedPeakBrightnessNits,
        IncludeSimple = IncludeSimple,
        ForceComplex = ForceComplex,
        CreateElArchive = CreateElArchive,
        RemoveDolbyVision = RemoveDolbyVision,
        SafeMode = SafeMode,
        ReplaceOriginal = ReplaceOriginal,
        VerificationLevel = VerificationLevel,
        RecycleDeletedOriginalsAndBackups = RecycleDeletedOriginalsAndBackups,
        FreeSpaceMarginPercent = FreeSpaceMarginPercent
    };

    /// <summary>Rejects values the Settings pages would not accept, such as a hand-edited presets file.</summary>
    public void Validate()
    {
        new AnalysisOptions(BrightnessMarginNits, AssumedPeakBrightnessNits, ScanSampleCount, ScanSampleSeconds, MinimumScanCoveragePercent, InspectionPeakPercentile).ValidateSampling();
        if (BrightnessMarginNits is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(BrightnessMarginNits));
        }
        if (AssumedPeakBrightnessNits is < 100 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(AssumedPeakBrightnessNits));
        }
        if (!Enum.IsDefined(VerificationLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(VerificationLevel));
        }
        if (!double.IsFinite(FreeSpaceMarginPercent) || FreeSpaceMarginPercent is < 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(FreeSpaceMarginPercent));
        }
    }
}

/// <summary>One setting a preset would change.</summary>
/// <param name="IncreasesRisk">Whether the new value can lose data or picture quality the old value kept, such as replacing originals.</param>
public sealed record PresetChange(string Setting, string From, string To, bool IncreasesRisk);
