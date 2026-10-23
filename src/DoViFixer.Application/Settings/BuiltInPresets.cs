namespace DoViFixer.Application.Settings;

/// <summary>The presets that ship with the app. Names describe what each one sets, not what it is for.</summary>
/// <remarks>
/// No preset examines files less closely than the default. Sampling fewer points can miss brightness expansion that is
/// confined to a few scenes, and convert a Complex FEL file that should have been blocked.
/// </remarks>
public static class BuiltInPresets
{
    private static readonly PresetValues defaults = PresetValues.From(new UserSettings());

    public static SettingsPreset Default { get; } = new(
        "Default",
        "The settings a new installation starts with: 10 scan clips per video, frame-count verification with a recount, and originals kept.",
        defaults,
        IsBuiltIn: true);

    public static SettingsPreset ThoroughCheck { get; } = new(
        "Thorough check (40 clips)",
        "Scans 40 clips per video instead of 10, and runs full integrity verification. Originals are kept.",
        defaults with { ScanSampleCount = 40, VerificationLevel = VerificationLevel.FullIntegrity },
        IsBuiltIn: true);

    public static SettingsPreset ExhaustiveCheck { get; } = new(
        "Exhaustive check (100 clips)",
        "Scans 100 clips per video, the most a scan allows, and runs full integrity verification. Still sampling: Inspect reads every frame.",
        defaults with { ScanSampleCount = 100, VerificationLevel = VerificationLevel.FullIntegrity },
        IsBuiltIn: true);

    public static SettingsPreset ReplaceOriginalsKeepArchive { get; } = new(
        "Replace originals, keep EL archive",
        "Saves the enhancement layer in a .dovi archive, then replaces each original after full integrity verification. Replaced originals go to the Recycle Bin. Restore can rebuild Profile 7 from the archive.",
        defaults with
        {
            ScanSampleCount = 40,
            VerificationLevel = VerificationLevel.FullIntegrity,
            CreateElArchive = true,
            ReplaceOriginal = true,
            RecycleDeletedOriginalsAndBackups = true
        },
        IsBuiltIn: true);

    public static IReadOnlyList<SettingsPreset> All { get; } = [Default, ThoroughCheck, ExhaustiveCheck, ReplaceOriginalsKeepArchive];
}
