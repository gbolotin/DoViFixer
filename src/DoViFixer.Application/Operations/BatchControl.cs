namespace DoViFixer.Application.Operations;
/// <summary>One operation's synchronized pending selection and current-file cancellation.</summary>
public sealed class BatchControl : IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<string> pending;
    private CancellationTokenSource? current;
    private string? currentPath;
    public BatchControl(IEnumerable<string> paths) => pending = new(paths, StringComparer.OrdinalIgnoreCase);
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

    internal CancellationToken? Start(string path, CancellationToken batchToken)
    {
        lock (gate)
        {
            batchToken.ThrowIfCancellationRequested();
            if (!pending.Remove(path))
            {
                return null;
            }

            current = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
            currentPath = path;
            return current.Token;
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
