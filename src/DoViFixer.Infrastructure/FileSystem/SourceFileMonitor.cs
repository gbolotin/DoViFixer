using DoViFixer.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.FileSystem;
internal sealed class SourceFileMonitor(ILogger<SourceFileMonitor> logger) : ISourceFileMonitor, IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new();
    private bool disposed;

    public event EventHandler? Changed;

    public bool Exists(string path) => File.Exists(path);

    public void Watch(IEnumerable<string> paths)
    {
        var folders = paths.Select(Path.GetDirectoryName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            foreach (string folder in watchers.Keys.Where(folder => !folders.Contains(folder)).ToArray())
            {
                Stop(folder);
            }

            foreach (string folder in folders.Where(folder => !watchers.ContainsKey(folder)))
            {
                if (Start(folder) is { } watcher)
                {
                    watchers.Add(folder, watcher);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (string folder in watchers.Keys.ToArray())
            {
                Stop(folder);
            }
        }
    }

    private FileSystemWatcher? Start(string folder)
    {
        if (!Directory.Exists(folder))
        {
            // The folder itself was removed; the next Watch call retries it.
            return null;
        }

        var watcher = new FileSystemWatcher(folder, "*.mkv")
        {
            NotifyFilter = NotifyFilters.FileName,
            IncludeSubdirectories = false
        };
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Error += (_, e) => OnError(folder, e.GetException());
        try
        {
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(ex, "Cannot watch source folder {Folder}", folder);
            watcher.Dispose();
            return null;
        }
    }

    private void Stop(string folder)
    {
        if (watchers.Remove(folder, out var watcher))
        {
            watcher.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void OnError(string folder, Exception error)
    {
        // Buffer overflows and removed folders lose events; drop the watcher so the next Watch call recreates it.
        logger.LogDebug(error, "Source folder watcher failed for {Folder}", folder);
        lock (gate)
        {
            Stop(folder);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
