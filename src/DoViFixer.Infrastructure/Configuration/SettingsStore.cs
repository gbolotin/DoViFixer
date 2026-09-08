using System.Text.Json;
using System.Text.Json.Serialization;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Configuration;

public sealed record StorageOptions(string RootDirectory)
{
    public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    public string ToolsDirectory => Path.Combine(RootDirectory, "tools");
}

internal sealed class SettingsStore(StorageOptions options, ILogger<SettingsStore> logger) : ISettingsStore
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
        var previous = await ReadAsync(cancellationToken);
        var updated = update(previous);
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
            using var audit = logger.BeginScope(new Dictionary<string, object> { ["LogKind"] = "Audit" });
            if (previous.TemporaryDirectory != updated.TemporaryDirectory)
            {
                logger.LogInformation("Setting {Setting} changed from {PreviousValue} to {NewValue} in {SettingsPath}",
                    "TemporaryDirectory", previous.TemporaryDirectory, updated.TemporaryDirectory, options.SettingsPath);
            }
            foreach (var tool in previous.ToolPaths.Keys.Union(updated.ToolPaths.Keys))
            {
                string? before = previous.ToolPaths.GetValueOrDefault(tool);
                string? after = updated.ToolPaths.GetValueOrDefault(tool);
                if (before != after)
                {
                    logger.LogInformation("Setting {Setting} changed from {PreviousValue} to {NewValue} in {SettingsPath}",
                        "ToolPaths." + tool, before, after, options.SettingsPath);
                }
            }
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
