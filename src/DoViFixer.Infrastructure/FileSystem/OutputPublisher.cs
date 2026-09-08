using DoViFixer.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.FileSystem;

internal sealed class OutputPublisher(ILogger<OutputPublisher> logger) : IOutputPublisher
{
    public IStagedOutput Stage(string destination)
    {
        string full = Path.GetFullPath(destination);
        FileOperations.RejectReparsePoints(full);
        if (File.Exists(full) || Directory.Exists(full))
        {
            throw new IOException($"Output already exists: {full}");
        }
        // Unique directory on the destination volume reserves ownership of the partial output.
        string directory = Path.Combine(Path.GetDirectoryName(full)!, ".dovifixer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new StagedOutput(full, directory, logger);
    }

    private sealed class StagedOutput(string destination, string directory, ILogger logger) : IStagedOutput
    {
        public string Path => System.IO.Path.Combine(directory, "output.partial");
        public async Task PublishAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(Path) || new FileInfo(Path).Length == 0)
            {
                throw new IOException("Verified output is missing or empty.");
            }
            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileOperations.RejectReparsePoints(destination);
                try
                {
                    File.Move(Path, destination, overwrite: false);
                    return;
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33 && attempt < 8)
                {
                    await Task.Delay(250, cancellationToken);
                }
            }
        }
        public ValueTask DisposeAsync()
        {
            try
            {
                FileOperations.RejectReparsePoints(directory);
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Partial output remains in {Directory}", directory);
            }
            return ValueTask.CompletedTask;
        }
    }
}
