namespace DoViFixer.Application.Operations;
/// <summary>One operation's synchronized pending selection, pause and current-file cancellation.</summary>
public sealed class BatchControl : IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<string> pending;
    private readonly Dictionary<string, CancellationTokenSource> active = new(StringComparer.OrdinalIgnoreCase);
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
            if (!active.TryGetValue(path, out var source))
            {
                return false;
            }

            source.Cancel();
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

                    var source = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
                    active.Add(path, source);
                    return source.Token;
                }

                resumeTask = resume.Task;
            }

            await resumeTask.WaitAsync(batchToken);
        }
    }

    internal void Finish(string path)
    {
        lock (gate)
        {
            if (active.Remove(path, out var source))
            {
                source.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            foreach (var source in active.Values)
            {
                source.Dispose();
            }

            active.Clear();
        }
    }
}
