using DoViFixer.Application.Abstractions;
using DoViFixer.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.TemporaryStorage;

internal sealed class TemporaryWorkspaceFactory(IFileOperations files, ILogger<TemporaryWorkspaceFactory> logger) : ITemporaryWorkspaceFactory
{
    public ValueTask<ITemporaryWorkspace> CreateAsync(long requiredBytes, string? directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(directory ?? Path.GetTempPath());
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }
        files.EnsureAvailableSpace(root, requiredBytes);
        string path = Path.Combine(root, "DoViFixer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return ValueTask.FromResult<ITemporaryWorkspace>(new Workspace(path, logger));
    }

    private sealed class Workspace(string path, ILogger logger) : ITemporaryWorkspace
    {
        public string DirectoryPath => path;
        public string File(string name)
        {
            if (Path.GetFileName(name) != name || name is "." or "..")
            {
                throw new ArgumentException("Workspace names must be plain filenames.");
            }
            return Path.Combine(path, name);
        }
        public ValueTask DisposeAsync()
        {
            try
            {
                FileOperations.RejectReparsePoints(path);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Temporary workspace remains at {Path}", path);
            }
            return ValueTask.CompletedTask;
        }
    }
}
