using System.Globalization;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Settings;

/// <summary>Lists, compares, applies, saves and deletes presets. Applying a preset changes settings only; it starts no operation.</summary>
public sealed class PresetService(ISettingsStore settings, IPresetStore presets, ILogger<PresetService> logger)
{
    public const int MaximumNameLength = 60;

    private sealed record ShownSetting(string Name, Func<PresetValues, string> Format, Func<PresetValues, PresetValues, bool> IncreasesRisk);

    // Names match the Settings pages, so a listed change can be found there.
    private static readonly ShownSetting[] shownSettings =
    [
        new("Clips per video", v => Number(v.ScanSampleCount), (from, to) => to.ScanSampleCount < from.ScanSampleCount),
        new("Clip length", v => Number(v.ScanSampleSeconds) + " s", (from, to) => to.ScanSampleSeconds < from.ScanSampleSeconds),
        new("Required scan coverage", v => Number(v.MinimumScanCoveragePercent) + " %", (from, to) => to.MinimumScanCoveragePercent < from.MinimumScanCoveragePercent),
        new("Peak-brightness percentile", v => Number(v.InspectionPeakPercentile), (from, to) => to.InspectionPeakPercentile < from.InspectionPeakPercentile),
        new("Brightness margin", v => Number(v.BrightnessMarginNits) + " nits", (from, to) => to.BrightnessMarginNits > from.BrightnessMarginNits),
        new("Assumed peak brightness", v => Number(v.AssumedPeakBrightnessNits) + " nits", (_, _) => false),
        new("Include Simple FEL conversion", v => OnOff(v.IncludeSimple), (from, to) => !from.IncludeSimple && to.IncludeSimple),
        new("Allow Complex FEL conversion", v => OnOff(v.ForceComplex), (from, to) => !from.ForceComplex && to.ForceComplex),
        new("Back up enhancement layer", v => OnOff(v.CreateElArchive), (from, to) => from.CreateElArchive && !to.CreateElArchive),
        new("Convert to HDR10", v => OnOff(v.RemoveDolbyVision), (from, to) => !from.RemoveDolbyVision && to.RemoveDolbyVision),
        new("Use disk-based conversion (Safe mode)", v => OnOff(v.SafeMode), (_, _) => false),
        new("Replace original after verification", v => OnOff(v.ReplaceOriginal), (from, to) => !from.ReplaceOriginal && to.ReplaceOriginal),
        new("Verification level", v => Verification(v.VerificationLevel), (from, to) => to.VerificationLevel < from.VerificationLevel),
        new("Move deleted originals and backups to Recycle Bin", v => OnOff(v.RecycleDeletedOriginalsAndBackups), (from, to) => from.RecycleDeletedOriginalsAndBackups && !to.RecycleDeletedOriginalsAndBackups),
        new("Extra free-space margin", v => Number(v.FreeSpaceMarginPercent) + " %", (from, to) => to.FreeSpaceMarginPercent < from.FreeSpaceMarginPercent)
    ];

    /// <summary>Built-in presets first, then the user's in the order they were saved.</summary>
    public async Task<IReadOnlyList<SettingsPreset>> ListAsync(CancellationToken cancellationToken) =>
        [.. BuiltInPresets.All, .. await presets.ReadAsync(cancellationToken)];

    public async Task<SettingsPreset> FindAsync(string name, CancellationToken cancellationToken) =>
        Find(await ListAsync(cancellationToken), name) ?? throw new ArgumentException($"There is no preset named '{name.Trim()}'.", nameof(name));

    /// <summary>The preset whose values all match <paramref name="current"/>, or null for custom settings.</summary>
    public static SettingsPreset? FindActive(IEnumerable<SettingsPreset> list, UserSettings current)
    {
        var values = PresetValues.From(current);
        return list.FirstOrDefault(preset => preset.Values == values);
    }

    /// <summary>The settings that would change, in Settings page order.</summary>
    public static IReadOnlyList<PresetChange> Compare(UserSettings current, PresetValues target)
    {
        var from = PresetValues.From(current);
        return shownSettings
            .Where(setting => setting.Format(from) != setting.Format(target))
            .Select(setting => new PresetChange(setting.Name, setting.Format(from), setting.Format(target), setting.IncreasesRisk(from, target)))
            .ToList();
    }

    public async Task<IReadOnlyList<PresetChange>> PreviewAsync(SettingsPreset preset, CancellationToken cancellationToken) =>
        Compare(await settings.ReadAsync(cancellationToken), preset.Values);

    /// <summary>Sets every value the preset holds and leaves folders, workers, tool paths and appearance unchanged.</summary>
    /// <returns>The settings that changed.</returns>
    public async Task<IReadOnlyList<PresetChange>> ApplyAsync(SettingsPreset preset, CancellationToken cancellationToken)
    {
        preset.Values.Validate();
        IReadOnlyList<PresetChange> changes = [];
        await settings.UpdateAsync(current =>
        {
            changes = Compare(current, preset.Values);
            return preset.Values.ApplyTo(current) with { LastAppliedPreset = preset.Name };
        }, cancellationToken);
        OperationLog.Audit(logger, "ApplyPreset", preset.Name, "Completed");
        foreach (var change in changes)
        {
            logger.LogInformation("Preset {Preset} changed {Setting} from {PreviousValue} to {NewValue}", preset.Name, change.Setting, change.From, change.To);
        }

        return changes;
    }

    /// <summary>Saves the current settings under a name, replacing a saved preset with the same name.</summary>
    public async Task<SettingsPreset> SaveCurrentAsync(string name, string? description, CancellationToken cancellationToken)
    {
        name = ValidateName(name);
        if (BuiltInPresets.All.Any(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"'{name}' is a built-in preset. Choose another name.", nameof(name));
        }

        var current = await settings.ReadAsync(cancellationToken);
        var preset = new SettingsPreset(name, description?.Trim() ?? "", PresetValues.From(current));
        var saved = (await presets.ReadAsync(cancellationToken)).ToList();
        int existing = saved.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            saved[existing] = preset;
        }
        else
        {
            saved.Add(preset);
        }

        await presets.WriteAsync(saved, cancellationToken);
        // The current settings now match this preset, so it names them from here on.
        await settings.UpdateAsync(s => s with { LastAppliedPreset = name }, cancellationToken);
        OperationLog.Audit(logger, "SavePreset", name, existing >= 0 ? "Replaced" : "Completed");
        return preset;
    }

    /// <summary>Deletes a saved preset. Settings are not changed.</summary>
    /// <returns>Whether a preset with that name existed.</returns>
    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        name = name.Trim();
        if (BuiltInPresets.All.Any(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"'{name}' is a built-in preset and cannot be deleted.");
        }

        var saved = (await presets.ReadAsync(cancellationToken)).ToList();
        if (saved.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        await presets.WriteAsync(saved, cancellationToken);
        OperationLog.Audit(logger, "DeletePreset", name, "Completed");
        return true;
    }

    private static SettingsPreset? Find(IEnumerable<SettingsPreset> list, string name) =>
        list.FirstOrDefault(preset => preset.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Enter a preset name.", nameof(name));
        }
        if (name.Length > MaximumNameLength)
        {
            throw new ArgumentException($"Preset names can be at most {MaximumNameLength} characters.", nameof(name));
        }

        return name;
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string OnOff(bool value) => value ? "On" : "Off";
    private static string Verification(VerificationLevel level) => level switch
    {
        VerificationLevel.None => "1 · No integrity check",
        VerificationLevel.FrameCount => "2 · Frame count",
        VerificationLevel.FrameCountWithRecount => "3 · Frame count + mismatch recount",
        VerificationLevel.FullIntegrity => "4 · Full integrity",
        _ => level.ToString()
    };
}
