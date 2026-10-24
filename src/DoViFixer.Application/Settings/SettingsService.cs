using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Settings;
public sealed class SettingsService(ISettingsStore store, IDependencyDetector detector, IToolCatalog catalog, IFileOperations files, ILogger<SettingsService> logger, IHardwareInfoProvider? hardware = null)
{
    public HardwareProfile Hardware => hardware?.Profile ?? new(1, 1, false);

    public Task<UserSettings> ReadAsync(CancellationToken cancellationToken) => store.ReadAsync(cancellationToken);
    public Task SetMediaListLayoutAsync(MediaListLayout? layout, CancellationToken cancellationToken) =>
        store.UpdateAsync(settings => settings with { MediaListLayout = layout }, cancellationToken);
    public Task SetShowSelectionCheckBoxesAsync(bool show, CancellationToken cancellationToken) =>
        store.UpdateAsync(settings => settings with { ShowSelectionCheckBoxes = show }, cancellationToken);
    public async Task SetPreferencesAsync(PreferenceChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        cancellationToken.ThrowIfCancellationRequested();
        changes.Validate();
        string? temporary = string.IsNullOrWhiteSpace(changes.TemporaryDirectory) ? null : Path.GetFullPath(changes.TemporaryDirectory);
        string? full = string.IsNullOrWhiteSpace(changes.OutputDirectory) ? null : Path.GetFullPath(changes.OutputDirectory);
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
            TemporaryDirectory = changes.TemporaryDirectory is null ? s.TemporaryDirectory : temporary,
            OutputDirectory = changes.OutputDirectory is null ? s.OutputDirectory : full,
            ReplaceOriginal = changes.ReplaceOriginal ?? s.ReplaceOriginal,
            CreateElArchive = changes.CreateElArchive ?? s.CreateElArchive,
            SafeMode = changes.SafeMode ?? s.SafeMode,
            FolderSearchDepth = changes.FolderSearchDepth ?? s.FolderSearchDepth,
            RemoveDolbyVision = changes.RemoveDolbyVision ?? s.RemoveDolbyVision,
            ScanSampleCount = changes.ScanSampleCount ?? s.ScanSampleCount,
            ScanSampleSeconds = changes.ScanSampleSeconds ?? s.ScanSampleSeconds,
            MinimumScanCoveragePercent = changes.MinimumScanCoveragePercent ?? s.MinimumScanCoveragePercent,
            InspectionPeakPercentile = changes.InspectionPeakPercentile ?? s.InspectionPeakPercentile,
            BrightnessMarginNits = changes.BrightnessMarginNits ?? s.BrightnessMarginNits,
            AssumedPeakBrightnessNits = changes.AssumedPeakBrightnessNits ?? s.AssumedPeakBrightnessNits,
            ConversionWorkers = changes.ConversionWorkers ?? s.ConversionWorkers,
            AnalysisWorkers = changes.AnalysisWorkers ?? s.AnalysisWorkers,
            ToolProcessPriority = changes.ToolProcessPriority ?? s.ToolProcessPriority,
            PipeBufferKiB = changes.PipeBufferKiB ?? s.PipeBufferKiB,
            VerificationLevel = changes.VerificationLevel ?? s.VerificationLevel,
            RecycleDeletedOriginalsAndBackups = changes.RecycleDeletedOriginalsAndBackups ?? s.RecycleDeletedOriginalsAndBackups,
            FreeSpaceMarginPercent = changes.FreeSpaceMarginPercent ?? s.FreeSpaceMarginPercent,
            IncludeSimple = changes.IncludeSimple ?? s.IncludeSimple,
            ForceComplex = changes.ForceComplex ?? s.ForceComplex,
            AutomaticallyScanAddedFiles = changes.AutomaticallyScanAddedFiles ?? s.AutomaticallyScanAddedFiles,
            UseCachedResults = changes.UseCachedResults ?? s.UseCachedResults,
            Theme = changes.Theme ?? s.Theme,
            ShowCompletionNotifications = changes.ShowCompletionNotifications ?? s.ShowCompletionNotifications,
            ShowSelectionCheckBoxes = changes.ShowSelectionCheckBoxes ?? s.ShowSelectionCheckBoxes,
            ShowInFileExplorerMenu = changes.ShowInFileExplorerMenu ?? s.ShowInFileExplorerMenu,
            CheckForUpdates = changes.CheckForUpdates ?? s.CheckForUpdates
        }, cancellationToken);
        OperationLog.Audit(logger, "SetConversionDefaults", changes.OutputDirectory is null ? "Output folder unchanged" : full ?? "Same folder as source", "Completed");
    }

    /// <summary>
    /// Sets the watch folder, or stops watching when <paramref name="folder"/> is empty. A folder that is unavailable,
    /// such as a disconnected drive, is kept: watching starts when it returns.
    /// </summary>
    public async Task SetWatchFolderAsync(string? folder, bool includeSubfolders, int settleSeconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (settleSeconds is < UserSettings.MinimumWatchSettleSeconds or > UserSettings.MaximumWatchSettleSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(settleSeconds), $"The wait before acting must be between {UserSettings.MinimumWatchSettleSeconds} and {UserSettings.MaximumWatchSettleSeconds} seconds.");
        }

        string? full = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder);
        var previous = await store.ReadAsync(cancellationToken);
        await store.UpdateAsync(s => s with
        {
            WatchFolder = full,
            WatchSubfolders = includeSubfolders,
            WatchSettleSeconds = settleSeconds
        }, cancellationToken);
        if (!string.Equals(previous.WatchFolder, full, StringComparison.OrdinalIgnoreCase))
        {
            OperationLog.Audit(logger, "SetWatchFolder", full ?? "Not watching", "Completed");
        }
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
