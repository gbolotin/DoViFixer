using DoViFixer.Application.Abstractions;
using DoViFixer.Domain.Media;

namespace DoViFixer.Infrastructure.FileSystem;

internal sealed class FileOperations : IFileOperations, IFileDiscovery
{
    public FileIdentity Identify(string path)
    {
        string full = Path.GetFullPath(path);
        RejectReparsePoints(full);
        var info = new FileInfo(full);
        if (!info.Exists || info.Length == 0)
        {
            throw new FileNotFoundException("A nonempty source file is required.", full);
        }
        return new(full, info.Length, info.LastWriteTimeUtc);
    }

    public IReadOnlyList<string> Discover(string input, int recursiveDepth, bool cleanup = false)
    {
        if (recursiveDepth is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(recursiveDepth), "Depth must be between 0 and 100.");
        }
        string full = Path.GetFullPath(input);
        RejectReparsePoints(full);
        bool Matches(string path) => cleanup
            ? path.EndsWith(".bak.dovi_convert", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dovi", StringComparison.OrdinalIgnoreCase)
            : path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase);
        if (File.Exists(full))
        {
            if (!Matches(full))
            {
                throw new ArgumentException(cleanup ? "Cleanup accepts only .dovi and .bak.dovi_convert backups." : "Only MKV media is supported.");
            }
            return new[] { full };
        }
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException(full);
        }
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = recursiveDepth > 0,
            MaxRecursionDepth = recursiveDepth,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        return Directory.EnumerateFiles(full, "*", enumeration).Where(Matches)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string PrepareOutputPath(string input, string? outputDirectory, string suffix)
    {
        string source = Path.GetFullPath(input);
        string directory = Path.GetFullPath(outputDirectory ?? Path.GetDirectoryName(source)!);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Create the output directory first: {directory}");
        }
        RejectReparsePoints(directory);
        string output = Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + suffix);
        if (File.Exists(output) || Directory.Exists(output) || string.Equals(output, source, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Output collision: {output}. Existing files will not be overwritten.");
        }
        return output;
    }

    public void EnsureAvailableSpace(string directory, long requiredBytes)
    {
        if (requiredBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredBytes));
        }
        string full = Path.GetFullPath(directory);
        RejectReparsePoints(full);
        // GetDiskFreeSpaceEx handles UNC paths as well as local volumes.
        if (!NativeStorage.GetDiskFreeSpaceEx(full, out ulong available, out _, out _))
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), $"Cannot determine available space at {full}.");
        }
        if (available < (ulong)requiredBytes)
        {
            throw new IOException($"Insufficient space at {full}: requires {requiredBytes:N0} bytes; available {available:N0}.");
        }
    }

    public ValueTask<IAsyncDisposable> AcquireReadLeaseAsync(FileIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejectReparsePoints(identity.Path);
        var stream = new FileStream(identity.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        try
        {
            if (Identify(identity.Path) != identity || stream.Length != identity.Length)
            {
                throw new IOException($"Source changed since planning: {identity.Path}");
            }
            return ValueTask.FromResult<IAsyncDisposable>(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public Task DeleteAsync(FileIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!identity.Path.EndsWith(".dovi", StringComparison.OrdinalIgnoreCase) && !identity.Path.EndsWith(".bak.dovi_convert", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cleanup may delete only explicitly planned backup files.");
        }
        RejectReparsePoints(identity.Path);
        using var handle = NativeStorage.OpenForDeletion(identity.Path);
        if (RandomAccess.GetLength(handle) != identity.Length || File.GetLastWriteTimeUtc(handle) != identity.LastWriteUtc)
        {
            throw new IOException("Backup changed since planning; prepare a new cleanup plan.");
        }
        // Mark the already-open, exclusively held file for deletion, avoiding a close/reopen race.
        NativeStorage.DeleteOnClose(handle);
        return Task.CompletedTask;
    }

    internal static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                // OneDrive files use reparse points too; reject link types only, not cloud placeholders.
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (info.LinkTarget is not null)
                {
                    throw new IOException($"Symbolic links and junctions are not supported for mutations: {current}");
                }
            }
        }
    }
}
