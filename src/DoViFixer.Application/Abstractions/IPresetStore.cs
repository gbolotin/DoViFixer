using DoViFixer.Application.Settings;

namespace DoViFixer.Application.Abstractions;

/// <summary>Keeps the presets the user saved. Built-in presets are not stored.</summary>
public interface IPresetStore
{
    Task<IReadOnlyList<SettingsPreset>> ReadAsync(CancellationToken cancellationToken);
    Task WriteAsync(IReadOnlyList<SettingsPreset> presets, CancellationToken cancellationToken);
}
