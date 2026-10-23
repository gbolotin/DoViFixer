using System.Text.Json;
using System.Text.Json.Serialization;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;

namespace DoViFixer.Infrastructure.Configuration;

/// <summary>Keeps the user's presets in presets.json beside settings.json.</summary>
internal sealed class PresetStore(StorageOptions options) : IPresetStore
{
    private sealed record PresetFile(int SchemaVersion, IReadOnlyList<StoredPreset> Presets);
    private sealed record StoredPreset(string Name, string Description, PresetValues Values);

    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions json = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<IReadOnlyList<SettingsPreset>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.PresetsPath))
        {
            return [];
        }

        await using var stream = new FileStream(options.PresetsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
        PresetFile? file;
        try
        {
            file = await JsonSerializer.DeserializeAsync<PresetFile>(stream, json, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The presets file is damaged; repair or delete {options.PresetsPath}. {ex.Message}", ex);
        }

        if (file?.Presets is null || file.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"The presets file is empty or from an unsupported version; repair or delete {options.PresetsPath}.");
        }

        return file.Presets.Select(preset => new SettingsPreset(preset.Name, preset.Description ?? "", preset.Values)).ToList();
    }

    public async Task WriteAsync(IReadOnlyList<SettingsPreset> presets, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.RootDirectory);
        var file = new PresetFile(CurrentSchemaVersion, presets.Where(preset => !preset.IsBuiltIn).Select(preset => new StoredPreset(preset.Name, preset.Description, preset.Values)).ToList());
        string temporary = options.PresetsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, file, json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, options.PresetsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
