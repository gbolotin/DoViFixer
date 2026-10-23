using DoViFixer.Application.Abstractions;
using DoViFixer.Application.WatchFolder;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.FileSystem;

/// <summary>
/// Watches one folder for new Matroska files and reports each once <see cref="ArrivalTracker"/> finds it has finished
/// arriving. Files still arriving are checked once a second.
/// </summary>
internal sealed class FolderWatcher(TimeProvider time, ILogger<FolderWatcher> logger) : IFolderWatcher, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly Lock gate = new();
    private FileSystemWatcher? watcher;
    private ITimer? timer;
    private ArrivalTracker? tracker;
    private WatchOptions? options;
    private bool disposed;

    public event EventHandler<string>? FileArrived;

    private readonly record struct WatchOptions(string Folder, bool IncludeSubfolders);

    public bool Watch(string? folder, bool includeSubfolders, TimeSpan settleTime)
    {
        lock (gate)
        {
            if (disposed)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                options = null;
                tracker = null;
                StopWatcher();
                timer?.Dispose();
                timer = null;
                return false;
            }

            var requested = new WatchOptions(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), includeSubfolders);
            bool sameFolder = options is { } current && string.Equals(current.Folder, requested.Folder, StringComparison.OrdinalIgnoreCase);
            // Another folder starts afresh; the same folder keeps the files still arriving, under the new wait.
            if (!sameFolder || tracker is null)
            {
                tracker = new ArrivalTracker(settleTime);
            }
            else
            {
                tracker.SettleTime = settleTime;
            }

            // Retrying the same folder recreates a watcher that failed or could not start.
            if (watcher is null || !sameFolder || options?.IncludeSubfolders != includeSubfolders)
            {
                StopWatcher();
                watcher = Start(requested.Folder, includeSubfolders);
            }

            options = requested;
            timer ??= time.CreateTimer(_ => Poll(), null, PollInterval, PollInterval);
            return watcher is not null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            StopWatcher();
            timer?.Dispose();
            timer = null;
            tracker = null;
        }
    }

    private FileSystemWatcher? Start(string folder, bool includeSubfolders)
    {
        if (!Directory.Exists(folder))
        {
            // A disconnected drive or a removed folder; the next Watch call retries it.
            logger.LogWarning("Watch folder {Folder} is unavailable", folder);
            return null;
        }

        var created = new FileSystemWatcher(folder, "*.mkv")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            IncludeSubdirectories = includeSubfolders,
            // Large downloads write often; a bigger buffer loses fewer events.
            InternalBufferSize = 64 * 1024
        };
        created.Created += OnChanged;
        created.Changed += OnChanged;
        created.Renamed += OnChanged;
        created.Error += (_, e) => OnError(created, e.GetException());
        try
        {
            created.EnableRaisingEvents = true;
            logger.LogInformation("Watching {Folder} for new media", folder);
            return created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(ex, "Cannot watch folder {Folder}", folder);
            created.Dispose();
            return null;
        }
    }

    private void StopWatcher()
    {
        watcher?.Dispose();
        watcher = null;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // A rename reports the new name; a partial download renamed to .mkv counts from then on.
        if (!e.FullPath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (gate)
        {
            if (ReferenceEquals(sender, watcher))
            {
                tracker?.Notice(e.FullPath, time.GetUtcNow());
            }
        }
    }

    private void OnError(FileSystemWatcher failed, Exception error)
    {
        // A buffer overflow loses events, and a removed folder ends the watch; the next Watch call recreates it.
        logger.LogWarning(error, "Watch folder watcher failed");
        lock (gate)
        {
            if (ReferenceEquals(failed, watcher))
            {
                StopWatcher();
            }
        }
    }

    private void Poll()
    {
        IReadOnlyList<string> arrived;
        lock (gate)
        {
            if (tracker is null || tracker.IsEmpty)
            {
                return;
            }

            arrived = tracker.Poll(time.GetUtcNow(), Probe);
        }

        foreach (string path in arrived)
        {
            logger.LogInformation("New media finished arriving in the watch folder: {Input}", path);
            FileArrived?.Invoke(this, path);
        }
    }

    private static ArrivingFile Probe(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return new(false, 0, false);
        }

        try
        {
            // Sharing read access only fails while another process has the file open for writing.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new(true, stream.Length, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(true, info.Length, true);
        }
    }
}
