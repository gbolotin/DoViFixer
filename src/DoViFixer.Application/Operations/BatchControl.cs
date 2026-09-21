namespace DoViFixer.Application.Operations;
/// <summary>One operation's synchronized pending selection, pause and current-file cancellation.</summary>
public sealed class BatchControl : IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<string> pending;
    private CancellationTokenSource? current;
    private string? currentPath;
    private TaskCompletionSource? resume;
    public BatchControl(IEnumerable<string> paths) => pending = new(paths, StringComparer.OrdinalIgnoreCase);

    public bool IsPaused
    {
        get
        {
            lock (gate)
            {
                return resume is not null;
            }
        }
    }

    public void Pause()
    {
        lock (gate)
        {
            resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            resume?.TrySetResult();
            resume = null;
        }
    }

    public bool Skip(string path)
    {
        lock (gate)
        {
            return pending.Remove(path);
        }
    }

    public bool Cancel(string path)
    {
        lock (gate)
        {
            if (!string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current?.Cancel();
            return true;
        }
    }

    internal async ValueTask<CancellationToken?> StartAsync(string path, CancellationToken batchToken)
    {
        while (true)
        {
            Task resumeTask;
            lock (gate)
            {
                batchToken.ThrowIfCancellationRequested();
                if (resume is null)
                {
                    if (!pending.Remove(path))
                    {
                        return null;
                    }

                    current = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
                    currentPath = path;
                    return current.Token;
                }

                resumeTask = resume.Task;
            }

            await resumeTask.WaitAsync(batchToken);
        }
    }

    internal void Finish()
    {
        lock (gate)
        {
            currentPath = null;
            current?.Dispose();
            current = null;
        }
    }

    public void Dispose() => Finish();
}
