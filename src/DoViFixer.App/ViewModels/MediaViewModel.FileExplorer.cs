using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.FileExplorer;

namespace DoViFixer.App.ViewModels;

/// <summary>
/// Files and folders File Explorer's right-click menu sends: Open adds them, as Add files does, and Convert also converts
/// them, as the Convert button does. Requests wait while another operation runs or Settings is open.
/// </summary>
public sealed partial class MediaViewModel
{
    // Requests that wait for the current operation to finish; used on the UI thread only.
    private readonly List<FileExplorerRequest> fileExplorerRequests = [];

    /// <summary>Completes when the File Explorer request started last finishes.</summary>
    public Task FileExplorerCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>Adds, and for Convert converts, the files and folders of <paramref name="request"/> once no other operation runs.</summary>
    public void Open(FileExplorerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Paths.Count == 0)
        {
            return;
        }

        fileExplorerRequests.Add(request);
        StartQueuedWork();
    }

    /// <summary>Starts the waiting File Explorer requests, which the user waits for, before files from the watch folder.</summary>
    private void StartQueuedWork()
    {
        if (!StartFileExplorerRequests())
        {
            StartAutoConversion();
        }
    }

    private bool StartFileExplorerRequests()
    {
        if (automaticWorkHeld || IsBusy || fileExplorerRequests.Count == 0)
        {
            return false;
        }

        // File Explorer starts the app once for each selected file, so requests of the same kind form one batch.
        bool convert = fileExplorerRequests[0].Convert;
        string[] paths = [.. fileExplorerRequests.Where(request => request.Convert == convert).SelectMany(request => request.Paths)
            .Where(discovery.IsSupportedInput).Distinct(StringComparer.OrdinalIgnoreCase)];
        fileExplorerRequests.RemoveAll(request => request.Convert == convert);
        if (paths.Length == 0)
        {
            return StartFileExplorerRequests();
        }

        // Show the whole list, so a filter never hides the files the user just sent.
        FilterText = "";
        FileFilter = MediaFileFilter.All;
        FileExplorerCompletion = convert ? AddAndConvertAsync(paths) : AddAsync(paths);
        return true;
    }

    private Task AddAndConvertAsync(string[] paths) => RunAsync("Conversion", async (token, progress) =>
    {
        var found = (await AddInputsAsync(paths, token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // As with the Convert button, files conversion would only skip are left out; their details say why.
        var rows = InDisplayOrder(Files.Where(row => found.Contains(row.Path) && row.MayConvert));
        if (rows.Length == 0)
        {
            if (found.Count > 0)
            {
                SetStatus(ViewStatus.Result, found.Count == 1
                    ? $"{Path.GetFileName(found.First())} was not converted. Its details say why."
                    : $"None of the {found.Count} files were converted. Their details say why.");
            }

            return;
        }

        await ConvertBatchCoreAsync(rows, progress, token);
    });
}
