using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Diagnostics;
using DoViFixer.Infrastructure.FileSystem;

namespace DoViFixer.Infrastructure.Diagnostics;

internal sealed record DiagnosticOptions(string LogDirectory);

internal sealed class DiagnosticBundleWriter(DiagnosticOptions options, IOutputPublisher publisher) : IDiagnosticBundleWriter
{
    internal const int LogsPerKind = 3;
    internal const int MaxLogBytes = 2 * 1024 * 1024;
    private const int MaxSnapshotBytes = 256 * 1024;
    private static readonly JsonSerializerOptions json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<DiagnosticExportResult> WriteAsync(string destination, DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var staged = publisher.Stage(destination);
        var entries = new List<LogEntry>();
        var issues = new List<string>();
        if (snapshot.SettingsError is not null)
        {
            issues.Add("Saved settings unavailable: " + snapshot.SettingsError);
        }

        await using (var output = new FileStream(staged.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            byte[] details = JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1,
                Snapshot = snapshot,
                Runtime = new { Framework = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString() }
            }, json);
            if (details.Length <= MaxSnapshotBytes)
            {
                await WriteEntryAsync(archive, "snapshot.json", details, cancellationToken);
            }
            else
            {
                issues.Add("Settings/tool snapshot exceeded 256 KiB and was omitted.");
            }

            foreach (string kind in new[] { "diagnostic", "history", "audit" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] paths;
                try
                {
                    FileOperations.RejectReparsePoints(options.LogDirectory);
                    paths = Directory.EnumerateFiles(options.LogDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
                        .Where(p => Regex.IsMatch(Path.GetFileName(p), $"^(?:(?:app|mcp)-)?{kind}-[0-9]{{8}}(?:_[0-9]+)?\\.jsonl$", RegexOptions.CultureInvariant))
                        .OrderByDescending(p => Path.GetFileName(p).Split(kind + "-")[1], StringComparer.Ordinal)
                        .Take(LogsPerKind).ToArray();
                    if (paths.Length == 0)
                    {
                        issues.Add($"No {kind} logs are available.");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    issues.Add($"Cannot enumerate {kind} logs: {ex.Message}");
                    continue;
                }

                foreach (string path in paths)
                {
                    entries.Add(await CopyLogAsync(archive, path, cancellationToken));
                }
            }

            await WriteEntryAsync(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                SchemaVersion = 1, snapshot.CreatedAt, Notice = DiagnosticsService.ContentsNotice,
                Limits = new { LogsPerKind, MaxLogBytes, MaxSnapshotBytes },
                Scope = "Up to three newest files per log kind. Older files are omitted. Only complete newline-terminated records within each tail are included. Active logs are best-effort snapshots.",
                Issues = issues, Logs = entries
            }, json), cancellationToken);
        }

        await staged.PublishAsync(cancellationToken);
        return new(Path.GetFullPath(destination), entries.Count(e => e.Error is null),
            issues.Count + entries.Count(e => e.Error is not null), entries.Count(e => e.Truncated));
    }

    private static async Task<LogEntry> CopyLogAsync(ZipArchive archive, string path, CancellationToken token)
    {
        string name = "logs/" + Path.GetFileName(path);
        byte[] buffer;
        long initialLength;
        int read;
        int start;
        int end;
        bool changed;
        try
        {
            FileOperations.RejectReparsePoints(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
            initialLength = input.Length;
            long offset = Math.Max(0, initialLength - MaxLogBytes);
            input.Position = offset;
            buffer = new byte[(int)(initialLength - offset)];
            read = await input.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, token);
            changed = input.Length != initialLength || read != buffer.Length;
            start = offset == 0 ? 0 : Array.IndexOf(buffer, (byte)'\n', 0, read) + 1;
            end = read == 0 ? 0 : Array.LastIndexOf(buffer, (byte)'\n', read - 1, read) + 1;
            if (offset > 0 && start == 0)
            {
                start = end;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(name, null, 0, false, false, ex.Message);
        }

        // Destination failures must abort publication, rather than masquerading as unavailable source logs.
        await WriteEntryAsync(archive, name, buffer.AsMemory(start, end - start), token);
        return new(name, initialLength, end - start, initialLength != end - start, changed, null);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        await using var entry = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
        await entry.WriteAsync(bytes, token);
    }

    private sealed record LogEntry(string Entry, long? SourceBytes, int IncludedBytes, bool Truncated, bool ChangedDuringRead, string? Error);
}
