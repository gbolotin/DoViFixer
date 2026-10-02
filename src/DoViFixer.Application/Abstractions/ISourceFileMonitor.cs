namespace DoViFixer.Application.Abstractions;

/// <summary>
/// Reports when source files may have been deleted, moved, renamed or restored outside the application.
/// </summary>
public interface ISourceFileMonitor
{
    /// <summary>Raised on a background thread when a watched folder changes or a watcher fails; callers re-check <see cref="Exists"/>.</summary>
    event EventHandler? Changed;

    /// <summary>Replaces the watched set with the folders containing <paramref name="paths"/> and retries folders that were unavailable.</summary>
    void Watch(IEnumerable<string> paths);

    bool Exists(string path);
}
