using DoViFixer.Application.Activity;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;
using DoViFixer.Application.WatchFolder;

namespace DoViFixer.App.ViewModels;

/// <summary>
/// The watch folder: files that finish arriving there are scanned, and those the conversion settings would convert
/// join the list and are converted without asking. Files wait while another operation runs or Settings is open.
/// </summary>
public sealed partial class MediaViewModel
{
    // Files that finished arriving and wait for the next automatic conversion; used on the UI thread only.
    private readonly List<string> arrivedFiles = [];
    private UserSettings? watchSettings;
    private bool automaticWorkHeld;

    /// <summary>The folder being watched, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusItems))]
    public partial string? WatchedFolder { get; private set; }

    /// <summary>Whether the watched folder is unavailable, such as a disconnected drive.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusItems))]
    public partial bool IsWatchFolderUnavailable { get; private set; }

    /// <summary>Whether the running operation is an automatic conversion of files from the watch folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusItems))]
    public partial bool IsAutoConverting { get; private set; }

    /// <summary>Completes when the automatic conversion started last finishes.</summary>
    public Task AutoConversion { get; private set; } = Task.CompletedTask;

    private StatusItem? WatchFolderStatusItem => WatchedFolder is not { } folder
        ? null
        : IsWatchFolderUnavailable
            ? new($"⚠ Watch folder unavailable: {folder}", $"{folder} cannot be watched. Files that arrive there are not converted until it is available again.\n\nClick to change the watch folder.", IsEmphasized: true, Command: OpenWatchFolderSettingsCommand)
            : IsAutoConverting
                ? new($"Auto-converting from {folder}", AutoConvertingToolTip(folder), IsEmphasized: true, Command: OpenWatchFolderSettingsCommand)
                : new(arrivedFiles.Count == 0 ? $"Watching: {folder}" : $"Watching: {folder} · {arrivedFiles.Count} waiting", WatchingToolTip(folder), Command: OpenWatchFolderSettingsCommand);

    private static string AutoConvertingToolTip(string folder) =>
        $"Converting new files from {folder} without asking. Activity marks each automatic conversion.\n\nClick to view or change the watch folder.";

    private string WatchingToolTip(string folder) =>
        $"New files that finish arriving in {folder}{(watchSettings?.WatchSubfolders == true ? " or its subfolders" : "")} are scanned. Files your conversion settings would convert are added to the list and converted without asking."
        + (arrivedFiles.Count == 0 ? "" : $"\n\n{arrivedFiles.Count} {(arrivedFiles.Count == 1 ? "file waits" : "files wait")} for the current operation to finish or Settings to close.")
        + "\n\nClick to view or change the watch folder.";

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenWatchFolderSettings() => RequestOpenWatchFolderSettings?.Invoke();

    public event Action? RequestOpenWatchFolderSettings;

    /// <summary>Watches the folder the settings name, or stops watching.</summary>
    private void ApplyWatchFolder(UserSettings s)
    {
        watchSettings = s;
        bool watching = folderWatcher.Watch(s.WatchFolder, s.WatchSubfolders, TimeSpan.FromSeconds(s.WatchSettleSeconds));
        WatchedFolder = string.IsNullOrWhiteSpace(s.WatchFolder) ? null : Path.GetFullPath(s.WatchFolder);
        IsWatchFolderUnavailable = WatchedFolder is not null && !watching;
        if (WatchedFolder is null)
        {
            arrivedFiles.Clear();
        }
    }

    /// <summary>Retries a watch folder that was unavailable, for example when the window is activated.</summary>
    private void RetryWatchFolder()
    {
        if (watchSettings is { } s)
        {
            ApplyWatchFolder(s);
        }
    }

    /// <summary>
    /// Holds automatic conversion and File Explorer requests while <paramref name="hold"/> is <see langword="true"/>, as the
    /// shell does while Settings is open, so settings never change under such a batch. Files that arrive meanwhile wait.
    /// </summary>
    public void HoldAutomaticWork(bool hold)
    {
        automaticWorkHeld = hold;
        StartQueuedWork();
    }

    private void OnFileArrived(object? sender, string path)
    {
        if (uiContext is null)
        {
            QueueArrivedFile(path);
        }
        else
        {
            uiContext.Post(_ => QueueArrivedFile(path), null);
        }
    }

    private void QueueArrivedFile(string path)
    {
        if (WatchedFolder is null || arrivedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        arrivedFiles.Add(path);
        OnPropertyChanged(nameof(StatusItems));
        StartQueuedWork();
    }

    private void OnBusyChangedForWatchFolder()
    {
        if (IsBusy)
        {
            return;
        }

        // Start after the finished operation has fully ended, not from inside it.
        if (uiContext is null)
        {
            StartQueuedWork();
        }
        else
        {
            uiContext.Post(_ => StartQueuedWork(), null);
        }
    }

    private void StartAutoConversion()
    {
        if (automaticWorkHeld || IsBusy || arrivedFiles.Count == 0)
        {
            return;
        }

        string[] paths = [.. arrivedFiles];
        arrivedFiles.Clear();
        AutoConversion = AutoConvertAsync(paths);
    }

    private Task AutoConvertAsync(string[] paths) => RunAsync("Automatic conversion", async (token, progress) =>
    {
        IsAutoConverting = true;
        try
        {
            if (ConversionStarting is not null)
            {
                foreach (Func<CancellationToken, Task> handler in ConversionStarting.GetInvocationList())
                {
                    await handler(token);
                }
            }

            var userSettings = await settings.ReadAsync(token);
            UpdateSettingsSummary(userSettings);
            // Files the list already shows, such as this app's own outputs and restored files, are not new arrivals.
            paths = paths.Where(path => IsInWatchFolder(path, userSettings) && sourceFiles.Exists(path)
                && !Files.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (paths.Length == 0)
            {
                return;
            }

            if (ConversionFolderProblem(userSettings) is { } problem)
            {
                RejectArrivedFiles(paths, $"{problem.Title}. {problem.Message.ReplaceLineEndings(" ")}");
                SetStatus(ViewStatus.Error, problem.Title);
                return;
            }

            // Automatic conversion never asks, so it never installs tools either.
            bool toolsReady;
            try
            {
                toolsReady = (await dependencies.CheckAsync(token)).Ready;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The dependency warning in the status bar reports the failed check.
                toolsReady = false;
            }

            if (!toolsReady)
            {
                RejectArrivedFiles(paths, "Required tools are unavailable. Install them from Settings → Native tools; automatic conversion never installs tools.");
                SetStatus(ViewStatus.ToolsUnavailable);
                return;
            }

            InvalidatePlan();
            List<MediaRow> accepted = [];
            foreach (string path in paths)
            {
                SetStatus(ViewStatus.Progress, $"Scanning {Path.GetFileName(path)} from the watch folder…");
                var decision = await Task.Run(() => watchFolder.EvaluateAsync(path, userSettings, progress, token), token);
                if (!decision.Accepted)
                {
                    continue;
                }

                var remembered = await Task.Run(() => library.RememberAsync(path, token), token);
                // The new row leaves the user's selection as it is.
                if (AddRow(path, remembered, decision.Analysis, isSelected: false) is { } row)
                {
                    accepted.Add(row);
                }
            }

            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(AllFilesSelected));
            if (accepted.Count == 0)
            {
                SetStatus(ViewStatus.Result, paths.Length == 1
                    ? $"{Path.GetFileName(paths[0])} from the watch folder was not converted. Activity says why."
                    : $"None of the {paths.Length} new files from the watch folder were converted. Activity says why.");
                return;
            }

            Focused ??= accepted[0];
            StartBackgroundPreviews();
            await ConvertRowsAsync([.. accepted], userSettings, ExistingOutputHandling.Skip, ActivityOperation.AutoConversion, token);
        }
        finally
        {
            IsAutoConverting = false;
        }
    });

    private static bool IsInWatchFolder(string path, UserSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.WatchFolder))
        {
            return false;
        }

        string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(s.WatchFolder));
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
        return parent is not null && (string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase)
            || (s.WatchSubfolders && parent.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
    }

    private void RejectArrivedFiles(IEnumerable<string> paths, string reason)
    {
        foreach (string path in paths)
        {
            watchFolder.ReportNotConverted(path, reason, ActivitySeverity.Error);
        }

        ReportItemFailed();
    }
}
