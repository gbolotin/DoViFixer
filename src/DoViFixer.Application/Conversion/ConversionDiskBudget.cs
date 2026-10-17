using DoViFixer.Application.Abstractions;
using DoViFixer.Domain.Conversion;
using DoViFixer.Application.Settings;

namespace DoViFixer.Application.Conversion;

/// <summary>Reserves the conservative scratch and output preflight budget across active conversions.</summary>
public sealed class ConversionDiskBudget(IFileOperations files, ISettingsStore? settings = null)
{
    private readonly object gate = new();
    private readonly Dictionary<string, long> reserved = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IDisposable> ReserveAsync(CandidateConversionRequest request, CancellationToken cancellationToken)
    {
        long sourceLength = files.Identify(request.Path).Length;
        double marginPercent = settings is null ? 20 : (await settings.ReadAsync(cancellationToken)).FreeSpaceMarginPercent;
        string temporary = Path.GetFullPath(string.IsNullOrWhiteSpace(request.TemporaryDirectory) ? Path.GetTempPath() : request.TemporaryDirectory);
        string output = Path.GetFullPath(string.IsNullOrWhiteSpace(request.OutputDirectory) ? Path.GetDirectoryName(request.Path)! : request.OutputDirectory);
        string temporaryRoot = Path.GetPathRoot(temporary)!;
        var needs = new Dictionary<string, (string Directory, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        Add(temporary, ConversionPolicy.RequiredScratchBytes(sourceLength));
        Add(output, ConversionSpaceMargin.OutputBytes(sourceLength, marginPercent));

        void Add(string directory, long bytes)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(directory))!;
            if (needs.TryGetValue(root, out var entry))
            {
                needs[root] = (entry.Directory, checked(entry.Bytes + bytes));
            }
            else
            {
                needs.Add(root, (directory, bytes));
            }
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task wait;
            lock (gate)
            {
                try
                {
                    foreach (var (root, need) in needs)
                    {
                        try
                        {
                            files.EnsureAvailableSpace(need.Directory, checked(reserved.GetValueOrDefault(root) + need.Bytes));
                        }
                        catch (IOException ex)
                        {
                            string location = string.Equals(root, temporaryRoot, StringComparison.OrdinalIgnoreCase) ? "temporary" : "output";
                            throw new IOException($"Insufficient {location} disk space: {ex.Message}", ex);
                        }
                    }

                    foreach (var (root, need) in needs)
                    {
                        reserved[root] = checked(reserved.GetValueOrDefault(root) + need.Bytes);
                    }

                    return new Reservation(this, needs);
                }
                catch (IOException) when (reserved.Count > 0)
                {
                    wait = released.Task;
                }
            }

            await wait.WaitAsync(cancellationToken);
        }
    }

    private void Release(Dictionary<string, (string Directory, long Bytes)> needs)
    {
        lock (gate)
        {
            foreach (var (root, need) in needs)
            {
                long remaining = reserved[root] - need.Bytes;
                if (remaining == 0)
                {
                    reserved.Remove(root);
                }
                else
                {
                    reserved[root] = remaining;
                }
            }

            released.TrySetResult();
            released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed class Reservation(ConversionDiskBudget owner, Dictionary<string, (string Directory, long Bytes)> needs) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Release(needs);
            }
        }
    }
}
