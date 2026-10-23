namespace DoViFixer.Application.Abstractions;

/// <summary>
/// Reports media files that finish arriving in a folder, such as a download or transfer folder. Files already in the
/// folder when watching starts are not reported.
/// </summary>
public interface IFolderWatcher
{
    /// <summary>
    /// Raised on a background thread with the full path of a new file once it has finished arriving: see
    /// <see cref="WatchFolder.ArrivalTracker"/>.
    /// </summary>
    event EventHandler<string>? FileArrived;

    /// <summary>
    /// Watches <paramref name="folder"/>, replacing the previous watch, or stops watching when it is <see langword="null"/>.
    /// Calling it again with the same values keeps the files still arriving and retries a folder that was unavailable.
    /// </summary>
    /// <returns>Whether the folder is being watched; <see langword="false"/> when it is unavailable or not given.</returns>
    bool Watch(string? folder, bool includeSubfolders, TimeSpan settleTime);
}
