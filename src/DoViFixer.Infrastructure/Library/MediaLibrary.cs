using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Library;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Library;

/// <summary>SQLite collection and analysis storage. Connections and transactions belong to each call.</summary>
internal sealed class MediaLibrary(StorageOptions options, ISettingsStore settings, TimeProvider time, ILogger<MediaLibrary> logger,
    ILogger<AnalysisCache> legacyLogger) : IMediaLibrary, IAnalysisCache, IDisposable
{
    internal const int SchemaVersion = 1;
    private readonly AnalysisCache legacy = new(options, legacyLogger, settings);
    private readonly SemaphoreSlim importGate = new(1);
    public void Dispose() => importGate.Dispose();
    public string FilePath => Path.Combine(options.RootDirectory, "library.db");
    public string RootDirectory => options.CacheDirectory;

    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        Directory.CreateDirectory(options.RootDirectory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, DefaultTimeout = 5 }.ToString());
        try
        {
            await connection.OpenAsync(token);
            using var version = Command(connection, "PRAGMA user_version");
            int current = Convert.ToInt32(await version.ExecuteScalarAsync(token));
            if (current > SchemaVersion)
            {
                throw new InvalidDataException("The media library was created by a newer application version.");
            }
            if (current == 0)
            {
                using var create = Command(connection, """
                    PRAGMA journal_mode=WAL;
                    BEGIN IMMEDIATE;
                    CREATE TABLE IF NOT EXISTS media (
                        fingerprint TEXT PRIMARY KEY, path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                        source_json TEXT NOT NULL, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL,
                        base_hash TEXT, conversion_json TEXT);
                    CREATE TABLE IF NOT EXISTS analyses (
                        fingerprint TEXT NOT NULL REFERENCES media(fingerprint) ON DELETE CASCADE,
                        method INTEGER NOT NULL, version INTEGER NOT NULL, json TEXT NOT NULL,
                        PRIMARY KEY(fingerprint, method));
                    CREATE TABLE IF NOT EXISTS history (
                        id INTEGER PRIMARY KEY, fingerprint TEXT NOT NULL REFERENCES media(fingerprint) ON DELETE CASCADE,
                        when_utc TEXT NOT NULL, action TEXT NOT NULL, detail TEXT NOT NULL);
                    CREATE INDEX IF NOT EXISTS history_media ON history(fingerprint, id);
                    CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                    PRAGMA user_version=1;
                    COMMIT;
                    """);
                await create.ExecuteNonQueryAsync(token);
            }
            using var foreignKeys = Command(connection, "PRAGMA foreign_keys=ON");
            await foreignKeys.ExecuteNonQueryAsync(token);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, SqliteTransaction? transaction = null,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command;
    }

    private async Task<string> FingerprintAsync(SqliteConnection connection, FileIdentity source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new FileInfo(source.Path);
        if (!info.Exists || info.Length != source.Length || info.LastWriteTimeUtc != source.LastWriteUtc)
        {
            throw new IOException("The source changed or is unavailable while looking it up in the media library.");
        }
        using var command = Command(connection, "SELECT fingerprint, source_json FROM media WHERE path=$path", null, ("$path", source.Path));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (await reader.ReadAsync(token) && JsonSerializer.Deserialize<FileIdentity>(reader.GetString(1)) is { } saved &&
            saved.Length == source.Length && saved.LastWriteUtc == source.LastWriteUtc)
        {
            return reader.GetString(0);
        }
        return await ContentFingerprint.ComputeAsync(source, token);
    }

    public async Task<LibraryFile?> FindAsync(FileIdentity source, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        string fingerprint = await FingerprintAsync(connection, source, cancellationToken);
        using var command = Command(connection, "SELECT 1 FROM media WHERE fingerprint=$key", null, ("$key", fingerprint));
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            return null;
        }
        return new(fingerprint, source, await BestAnalysisAsync(connection, fingerprint, source, cancellationToken));
    }

    public async Task<LibraryFile> RememberAsync(FileIdentity source, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        string fingerprint = await FingerprintAsync(connection, source, cancellationToken);
        using (var transaction = connection.BeginTransaction())
        {
            using var previous = Command(connection, "SELECT path FROM media WHERE fingerprint=$key", transaction, ("$key", fingerprint));
            string? oldPath = await previous.ExecuteScalarAsync(cancellationToken) as string;
            // A path occupied by different content represents a different file, not reusable analysis.
            using var replaced = Command(connection, "DELETE FROM media WHERE path=$path AND fingerprint<>$key", transaction,
                ("$path", source.Path), ("$key", fingerprint));
            await replaced.ExecuteNonQueryAsync(cancellationToken);
            string now = time.GetUtcNow().ToString("O");
            using var save = Command(connection, """
                INSERT INTO media(fingerprint,path,source_json,first_seen,last_seen) VALUES($key,$path,$source,$now,$now)
                ON CONFLICT(fingerprint) DO UPDATE SET path=excluded.path,source_json=excluded.source_json,last_seen=excluded.last_seen
                """, transaction, ("$key", fingerprint), ("$path", source.Path), ("$source", JsonSerializer.Serialize(source)), ("$now", now));
            await save.ExecuteNonQueryAsync(cancellationToken);
            if (oldPath is null)
            {
                await RecordAsync(connection, transaction, fingerprint, "Added", source.Path, cancellationToken);
            }
            else if (!string.Equals(oldPath, source.Path, StringComparison.OrdinalIgnoreCase))
            {
                await RecordAsync(connection, transaction, fingerprint, "Moved or renamed", oldPath + " -> " + source.Path, cancellationToken);
            }
            transaction.Commit();
        }
        return new(fingerprint, source, await BestAnalysisAsync(connection, fingerprint, source, cancellationToken));
    }

    private async Task RecordAsync(SqliteConnection connection, SqliteTransaction transaction, string fingerprint,
        string action, string detail, CancellationToken token)
    {
        using var command = Command(connection, "INSERT INTO history(fingerprint,when_utc,action,detail) VALUES($key,$when,$action,$detail)",
            transaction, ("$key", fingerprint), ("$when", time.GetUtcNow().ToString("O")), ("$action", action), ("$detail", detail));
        await command.ExecuteNonQueryAsync(token);
    }

    private async Task<MediaAnalysis?> BestAnalysisAsync(SqliteConnection connection, string key, FileIdentity source, CancellationToken token, AnalysisOptions? options = null)
    {
        options ??= (await settings.ReadAsync(token)).AnalysisOptions;
        using var command = Command(connection, "SELECT json FROM analyses WHERE fingerprint=$key AND version=1 ORDER BY method DESC", null, ("$key", key));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (ValidAnalysis(reader.GetString(0), source, options) is { } analysis)
            {
                return analysis;
            }
        }
        return null;
    }

    private static MediaAnalysis? ValidAnalysis(string json, FileIdentity source, AnalysisOptions options)
    {
        MediaAnalysis? analysis;
        try
        {
            analysis = JsonSerializer.Deserialize<MediaAnalysis>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (analysis is null || !Cacheable(analysis) ||
            (analysis.Evidence.Method != AnalysisMethod.MetadataOnly && analysis.Evidence.Options != options))
        {
            return null;
        }
        return MediaClassifier.Classify(analysis.Media with { Source = source }, analysis.Evidence);
    }

    private static bool Cacheable(MediaAnalysis analysis) => analysis.Evidence.Error is null &&
        MediaClassifier.Classify(analysis.Media, analysis.Evidence).Verdict is AnalysisVerdict.NotApplicable or AnalysisVerdict.Mel or
            AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel or AnalysisVerdict.FelUnclassified &&
        (analysis.Evidence.Method != AnalysisMethod.MetadataOnly || analysis.Media.Profile != DolbyVisionProfile.Profile7);

    public async Task<MediaAnalysis?> ReadAsync(FileIdentity source, AnalysisMethod method, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        string key = await FingerprintAsync(connection, source, cancellationToken);
        var snapshot = await settings.ReadAsync(cancellationToken);
        using var command = Command(connection, """
            SELECT json FROM analyses WHERE fingerprint=$key AND version=1
            AND (method=$method OR ($method=1 AND method IN (2,3))) ORDER BY method DESC
            """, null, ("$key", key), ("$method", (int)method));
        MediaAnalysis? stored = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                stored = ValidAnalysis(reader.GetString(0), source, snapshot.AnalysisOptions);
                // Scans saved before each sample was kept cannot list their samples, so they do not spare a new scan.
                if (stored?.Evidence is { Method: AnalysisMethod.SampledRpu, Samples: null })
                {
                    stored = null;
                }
                if (stored is not null)
                {
                    break;
                }
            }
        }
        if (stored is not null)
        {
            await RememberAsync(source, cancellationToken);
            return stored;
        }
        var cached = await legacy.ReadAsync(source, method, cancellationToken);
        if (cached is not null)
        {
            await WriteAsync(cached, cancellationToken);
        }
        return cached;
    }

    public Task WriteAsync(MediaAnalysis analysis, CancellationToken cancellationToken) =>
        WriteBestEffortAsync(() => WriteAnalysisCoreAsync(analysis, cancellationToken), analysis.Media.Source);

    private async Task WriteAnalysisCoreAsync(MediaAnalysis analysis, CancellationToken cancellationToken)
    {
        if (!Cacheable(analysis))
        {
            return;
        }
        var entry = await RememberAsync(analysis.Media.Source, cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        string json = JsonSerializer.Serialize(analysis);
        using var previous = Command(connection, "SELECT json FROM analyses WHERE fingerprint=$key AND method=$method", transaction,
            ("$key", entry.Fingerprint), ("$method", (int)analysis.Evidence.Method));
        if (await previous.ExecuteScalarAsync(cancellationToken) as string != json)
        {
            using var save = Command(connection, """
                INSERT INTO analyses(fingerprint,method,version,json) VALUES($key,$method,1,$json)
                ON CONFLICT(fingerprint,method) DO UPDATE SET version=1,json=excluded.json
                """, transaction, ("$key", entry.Fingerprint), ("$method", (int)analysis.Evidence.Method), ("$json", json));
            await save.ExecuteNonQueryAsync(cancellationToken);
            await RecordAsync(connection, transaction, entry.Fingerprint, analysis.Evidence.Method.ToString(), analysis.Reason, cancellationToken);
        }
        transaction.Commit();
    }

    public async Task<IReadOnlyList<LibraryFile>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await ImportLegacyAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        var sources = new List<(string Key, FileIdentity Source)>();
        using (var command = Command(connection, "SELECT fingerprint,source_json FROM media ORDER BY path COLLATE NOCASE"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                sources.Add((reader.GetString(0), JsonSerializer.Deserialize<FileIdentity>(reader.GetString(1))!));
            }
        }
        var snapshot = await settings.ReadAsync(cancellationToken);
        var result = new List<LibraryFile>();
        foreach (var (key, source) in sources)
        {
            result.Add(new(key, source, await BestAnalysisAsync(connection, key, source, cancellationToken, snapshot.AnalysisOptions)));
        }
        return result;
    }

    private async Task ImportLegacyAsync(CancellationToken token)
    {
        await importGate.WaitAsync(token);
        try
        {
            await using var connection = await OpenAsync(token);
            using var check = Command(connection, "SELECT value FROM metadata WHERE key='legacy_imported'");
            if (await check.ExecuteScalarAsync(token) is not null)
            {
                return;
            }
            string directory = Path.Combine(options.CacheDirectory, "analysis");
            foreach (string path in Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.json") : [])
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
                    if (json.RootElement.TryGetProperty("Version", out var version) && version.GetInt32() == 1 &&
                        json.RootElement.TryGetProperty("Analysis", out var value) && value.Deserialize<MediaAnalysis>() is { } analysis &&
                        File.Exists(analysis.Media.Source.Path) && new FileInfo(analysis.Media.Source.Path) is { } info &&
                        info.Length == analysis.Media.Source.Length && info.LastWriteTimeUtc == analysis.Media.Source.LastWriteUtc)
                    {
                        await WriteAnalysisCoreAsync(analysis, token);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    logger.LogDebug(ex, "Could not import legacy analysis {Path}", path);
                }
            }
            using var done = Command(connection, "INSERT OR IGNORE INTO metadata(key,value) VALUES('legacy_imported','1')");
            await done.ExecuteNonQueryAsync(token);
        }
        finally
        {
            importGate.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryEvent>> ReadHistoryAsync(string fingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = Command(connection, "SELECT when_utc,action,detail FROM history WHERE fingerprint=$key ORDER BY id", null, ("$key", fingerprint));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LibraryEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new(DateTimeOffset.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        }
        return result;
    }

    public async Task<int> CleanMissingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var candidates = new List<(string Key, string Path)>();
        using (var read = Command(connection, "SELECT fingerprint,path FROM media"))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add((reader.GetString(0), reader.GetString(1)));
            }
        }
        int count = 0;
        using var transaction = connection.BeginTransaction();
        foreach (var (key, path) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ConfirmedMissing(path))
            {
                continue;
            }
            using var delete = Command(connection, "DELETE FROM media WHERE fingerprint=$key AND path=$path", transaction, ("$key", key), ("$path", path));
            count += await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
        return count;
    }

    private static bool ConfirmedMissing(string path)
    {
        try
        {
            File.GetAttributes(path);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An inaccessible file is not proof that it was deleted.
            return false;
        }
    }

    public async Task<long> GetSizeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = Command(connection, "SELECT page_count*page_size FROM pragma_page_count(),pragma_page_size()");
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> ForgetAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        // Complete the legacy import before removing records so a later startup cannot restore them.
        await ImportLegacyAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        int removed = 0;
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var find = Command(connection, "SELECT source_json FROM media WHERE path=$path", transaction, ("$path", path));
            if (await find.ExecuteScalarAsync(cancellationToken) is string json)
            {
                var source = JsonSerializer.Deserialize<FileIdentity>(json)!;
                await legacy.ForgetAsync(source, cancellationToken);
            }
            // Foreign keys remove this record's analysis and history in the same transaction.
            using var delete = Command(connection, "DELETE FROM media WHERE path=$path", transaction, ("$path", path));
            removed += await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
        return removed;
    }

    public async Task<int> DeleteAsync(CancellationToken cancellationToken)
    {
        await importGate.WaitAsync(cancellationToken);
        try
        {
            // Remove legacy fallbacks too, so deleted results cannot return after restart.
            await legacy.ClearAsync(cancellationToken);
            await using var connection = await OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();
            using var count = Command(connection, "SELECT COUNT(*) FROM media", transaction);
            int removed = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
            using var delete = Command(connection, """
                DELETE FROM history;
                DELETE FROM analyses;
                DELETE FROM media;
                INSERT OR REPLACE INTO metadata(key,value) VALUES('legacy_imported','1');
                """, transaction);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();
            return removed;
        }
        finally
        {
            importGate.Release();
        }
    }

    public async Task<int> ClearAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var command = Command(connection, "DELETE FROM analyses; UPDATE media SET base_hash=NULL,conversion_json=NULL;");
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string?> ReadValueAsync(FileIdentity source, string column, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        string key = await FingerprintAsync(connection, source, token);
        // Column names come only from the two fixed call sites below.
        using var command = Command(connection, $"SELECT {column} FROM media WHERE fingerprint=$key", null, ("$key", key));
        return await command.ExecuteScalarAsync(token) as string;
    }

    private async Task WriteValueAsync(FileIdentity source, string column, string value, CancellationToken token)
    {
        var entry = await RememberAsync(source, token);
        await using var connection = await OpenAsync(token);
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection, $"UPDATE media SET {column}=$value WHERE fingerprint=$key", transaction,
            ("$value", value), ("$key", entry.Fingerprint));
        await command.ExecuteNonQueryAsync(token);
        if (column == "conversion_json")
        {
            await RecordAsync(connection, transaction, entry.Fingerprint, "Converted", JsonSerializer.Deserialize<OperationItemResult>(value)!.Output ?? "", token);
        }
        transaction.Commit();
    }

    public async Task<string?> ReadBaseLayerHashAsync(FileIdentity source, CancellationToken cancellationToken)
    {
        string? hash = await ReadValueAsync(source, "base_hash", cancellationToken) ?? await legacy.ReadBaseLayerHashAsync(source, cancellationToken);
        return hash is { Length: 64 } && hash.All(Uri.IsHexDigit) ? hash : null;
    }

    public Task WriteBaseLayerHashAsync(FileIdentity source, string sha256, CancellationToken cancellationToken) =>
        WriteBestEffortAsync(() => WriteValueAsync(source, "base_hash", sha256, cancellationToken), source);

    public async Task<OperationItemResult?> ReadConversionResultAsync(FileIdentity source, CancellationToken cancellationToken)
    {
        string? json = await ReadValueAsync(source, "conversion_json", cancellationToken);
        if (json is null)
        {
            return await legacy.ReadConversionResultAsync(source, cancellationToken);
        }
        try
        {
            return JsonSerializer.Deserialize<OperationItemResult>(json) is { Output: not null } result ? result with { Item = source.Path } : null;
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Invalid saved conversion result for {Path}", source.Path);
            return null;
        }
    }

    public Task WriteConversionResultAsync(FileIdentity source, OperationItemResult result, CancellationToken cancellationToken) =>
        WriteBestEffortAsync(() => WriteValueAsync(source, "conversion_json", JsonSerializer.Serialize(result), cancellationToken), source);
    private async Task WriteBestEffortAsync(Func<Task> write, FileIdentity source)
    {
        try
        {
            await write();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            logger.LogWarning(ex, "Could not save library data for {Path}", source.Path);
        }
    }

}
