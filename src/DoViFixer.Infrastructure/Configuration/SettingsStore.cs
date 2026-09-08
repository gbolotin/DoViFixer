using System.Text.Json;
using System.Text.Json.Serialization;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;

namespace DoViFixer.Infrastructure.Configuration;

public sealed record StorageOptions(string RootDirectory)
{
    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    public string ToolsDirectory => Path.Combine(RootDirectory, "tools");
}

internal sealed class SettingsStore(StorageOptions options) : ISettingsStore
{
    private static readonly JsonSerializerOptions json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<UserSettings> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(options.SettingsPath))
        {
            return new();
        }
        await using var stream = new FileStream(options.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
        return await JsonSerializer.DeserializeAsync<UserSettings>(stream, json, cancellationToken)
            ?? throw new InvalidDataException("Settings file is empty or invalid; repair settings.json before continuing.");
    }

    public async Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.RootDirectory);
        // File-share lock serializes writers across both future UIs and processes.
        await using var lease = await LockAsync(cancellationToken);
        var updated = update(await ReadAsync(cancellationToken));
        string temporary = options.SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, updated, json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, options.SettingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task<FileStream> LockAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(options.SettingsPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 100)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}
