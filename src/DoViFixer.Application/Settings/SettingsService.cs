using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Settings;
public sealed class SettingsService(ISettingsStore store, IDependencyDetector detector, IToolCatalog catalog, IFileOperations files, ILogger<SettingsService> logger)
{
    public Task<UserSettings> ReadAsync(CancellationToken cancellationToken) => store.ReadAsync(cancellationToken);
    public async Task SetPreferencesAsync(string? temporaryDirectory, string? outputDirectory, bool replaceOriginal, bool createElArchive, CancellationToken cancellationToken, bool? automaticallyScanAddedFiles = null, bool? useCachedResults = null, bool? autoSelectAfterScan = null, AppTheme? theme = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? temporary = string.IsNullOrWhiteSpace(temporaryDirectory) ? null : Path.GetFullPath(temporaryDirectory);
        string? full = string.IsNullOrWhiteSpace(outputDirectory) ? null : Path.GetFullPath(outputDirectory);
        if (temporary is not null)
        {
            files.EnsureWritableDirectory(temporary);
            files.EnsureAvailableSpace(temporary, 0);
        }

        if (full is not null)
        {
            files.EnsureWritableDirectory(full);
        }

        await store.UpdateAsync(s => s with
        {
            TemporaryDirectory = temporary,
            OutputDirectory = full,
            ReplaceOriginal = replaceOriginal,
            CreateElArchive = createElArchive,
            AutomaticallyScanAddedFiles = automaticallyScanAddedFiles ?? s.AutomaticallyScanAddedFiles,
            UseCachedResults = useCachedResults ?? s.UseCachedResults,
            AutoSelectAfterScan = autoSelectAfterScan ?? s.AutoSelectAfterScan,
            Theme = theme ?? s.Theme
        }, cancellationToken);
        OperationLog.Audit(logger, "SetConversionDefaults", full ?? "Same folder as source", "Completed");
    }

    public async Task SetThemeAsync(AppTheme theme, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await store.UpdateAsync(s => s with
        {
            Theme = theme
        }, cancellationToken);
        OperationLog.Audit(logger, "SetTheme", theme.ToString(), "Completed");
    }

    public async Task SetToolAsync(NativeTool tool, string path, CancellationToken cancellationToken)
    {
        var status = await detector.ValidatePathAsync(tool, path, cancellationToken);
        if (status.State != DependencyState.Ready || status.Path is null)
        {
            throw new InvalidOperationException(status.Diagnostic);
        }

        await store.UpdateAsync(s => s with
        {
            ToolPaths = s.ToolPaths.SetItem(tool, status.Path)
        }, cancellationToken);
        catalog.Refresh(new[]
        {
            status
        });
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["Tool"] = tool.ToString(), ["ToolVersion"] = status.Version ?? "unknown"
        });
        OperationLog.Audit(logger, "SetToolPath", status.Path, "Completed");
    }

    public async Task ResetToolAsync(NativeTool tool, CancellationToken cancellationToken)
    {
        await store.UpdateAsync(s => s with
        {
            ToolPaths = s.ToolPaths.Remove(tool)
        }, cancellationToken);
        catalog.Refresh(new[]
        {
            new DependencyStatus(tool, DependencyState.Missing, null, null, "Path reset; re-detection required.")
        });
        OperationLog.Audit(logger, "ResetToolPath", tool.ToString(), "Completed");
    }

    public async Task SetTemporaryDirectoryAsync(string? path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
        {
            await store.UpdateAsync(s => s with
            {
                TemporaryDirectory = null
            }, cancellationToken);
            OperationLog.Audit(logger, "ResetTemporaryDirectory", "System temporary directory", "Completed");
            return;
        }

        string full = Path.GetFullPath(path);
        files.EnsureWritableDirectory(full);
        files.EnsureAvailableSpace(full, 0);
        await store.UpdateAsync(s => s with
        {
            TemporaryDirectory = full
        }, cancellationToken);
        OperationLog.Audit(logger, "SetTemporaryDirectory", full, "Completed");
    }
}
