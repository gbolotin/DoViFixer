using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;

namespace DoViFixer.Application.Diagnostics;

// Explicit fields prevent future settings (including credentials) from entering exports automatically.
public sealed record DiagnosticSettings(string? TemporaryDirectory, string? OutputDirectory, bool ReplaceOriginal,
    bool CreateElArchive, bool IncludeSimple, bool ForceComplex, bool AutomaticallyScanAddedFiles,
    bool UseCachedResults, bool AutoSelectAfterScan, bool ShowCompletionNotifications, AppTheme Theme,
    IReadOnlyDictionary<NativeTool, string> ToolPaths);

public sealed record DiagnosticSnapshot(DateTimeOffset CreatedAt, string ApplicationName, string ApplicationVersion,
    DiagnosticSettings? Settings, IReadOnlyList<DependencyStatus> KnownTools, string? SettingsError);

public sealed record DiagnosticExportResult(string Path, int IncludedLogs, int UnavailableEntries, int TruncatedLogs) : IOperationResult
{
    public OperationStatus Status => UnavailableEntries > 0 || TruncatedLogs > 0 ? OperationStatus.Partial : OperationStatus.Completed;
    public string Message => $"Diagnostic ZIP saved to {Path}. Logs: {IncludedLogs}; unavailable entries: {UnavailableEntries}; trimmed logs: {TruncatedLogs}. Review manifest.json for details.";
}

public sealed class DiagnosticsService(ISettingsStore settings, IToolCatalog tools, IDiagnosticBundleWriter writer, TimeProvider timeProvider)
{
    public const string ContentsNotice = "Includes app/runtime details, known tool paths and versions, saved settings, and recent logs. May contain media paths, user names, and machine names. Saved locally; nothing is uploaded. Review before sharing.";

    public async Task<DiagnosticExportResult> ExportAsync(string destination, CancellationToken cancellationToken)
    {
        if (!string.Equals(System.IO.Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Choose a diagnostic ZIP filename ending in .zip.", nameof(destination));
        }

        cancellationToken.ThrowIfCancellationRequested();
        DiagnosticSettings? snapshot = null;
        string? error = null;
        try
        {
            var value = await settings.ReadAsync(cancellationToken);
            snapshot = new(value.TemporaryDirectory, value.OutputDirectory, value.ReplaceOriginal, value.CreateElArchive,
                value.IncludeSimple, value.ForceComplex, value.AutomaticallyScanAddedFiles, value.UseCachedResults,
                value.AutoSelectAfterScan, value.ShowCompletionNotifications, value.Theme, value.ToolPaths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error = ex.Message;
        }

        var report = new DiagnosticSnapshot(timeProvider.GetUtcNow(), ApplicationTitle.Name,
            ApplicationVersion.Of(typeof(DiagnosticsService).Assembly), snapshot, tools.Snapshot(), error);
        return await writer.WriteAsync(destination, report, cancellationToken);
    }
}
