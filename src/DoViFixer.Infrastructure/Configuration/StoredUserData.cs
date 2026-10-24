using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Infrastructure.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Configuration;

/// <summary>Checks and removes the settings and media library in the data folder. Tools and logs stay.</summary>
internal sealed class StoredUserData(StorageOptions options, ISettingsStore settings, IPresetStore presets, ILogger<StoredUserData> logger) : IStoredUserData
{
    private const int sqliteCorrupt = 11;
    private const int sqliteNotADatabase = 26;
    private string LibraryPath => Path.Combine(options.RootDirectory, "library.db");

    public async Task<StoredUserDataState> InspectAsync(CancellationToken cancellationToken)
    {
        var compatible = UserDataParts.None;
        var incompatible = UserDataParts.None;
        if (File.Exists(options.SettingsPath) || File.Exists(options.PresetsPath))
        {
            if (await SettingsReadableAsync(cancellationToken))
            {
                compatible |= UserDataParts.Settings;
            }
            else
            {
                incompatible |= UserDataParts.Settings;
            }
        }

        if (File.Exists(LibraryPath) || Directory.Exists(options.CacheDirectory))
        {
            // The cache alone is always readable: its entries carry their own version and stale ones are skipped.
            if (!File.Exists(LibraryPath) || await LibraryReadableAsync(cancellationToken))
            {
                compatible |= UserDataParts.Library;
            }
            else
            {
                incompatible |= UserDataParts.Library;
            }
        }

        logger.LogInformation("Stored user data in {Directory}: compatible {Compatible}, incompatible {Incompatible}", options.RootDirectory, compatible, incompatible);
        return new(compatible, incompatible);
    }

    private async Task<bool> SettingsReadableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await settings.ReadAsync(cancellationToken);
            await presets.ReadAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            logger.LogInformation(ex, "The stored settings cannot be read by this version");
            return false;
        }
    }

    private async Task<bool> LibraryReadableAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read-write without create, so SQLite can recover a write-ahead log the last session left.
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = LibraryPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) <= MediaLibrary.SchemaVersion;
        }
        // Only a file that is not a usable database counts; a busy or locked one stops the check instead.
        catch (SqliteException ex) when (ex.SqliteErrorCode is sqliteNotADatabase or sqliteCorrupt)
        {
            logger.LogInformation(ex, "The stored media library cannot be read by this version");
            return false;
        }
    }

    public Task RemoveAsync(UserDataParts parts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parts.HasFlag(UserDataParts.Settings))
        {
            File.Delete(options.SettingsPath);
            File.Delete(options.SettingsPath + ".lock");
            File.Delete(options.PresetsPath);
        }

        if (parts.HasFlag(UserDataParts.Library))
        {
            // Pooled connections keep the database open, and Windows does not delete an open file.
            SqliteConnection.ClearAllPools();
            File.Delete(LibraryPath);
            File.Delete(LibraryPath + "-wal");
            File.Delete(LibraryPath + "-shm");
            if (Directory.Exists(options.CacheDirectory))
            {
                Directory.Delete(options.CacheDirectory, recursive: true);
            }
        }

        using var audit = logger.BeginScope(new Dictionary<string, object>
        {
            ["LogKind"] = "Audit"
        });
        logger.LogInformation("Removed stored user data {Parts} from {Directory}", parts, options.RootDirectory);
        return Task.CompletedTask;
    }
}
