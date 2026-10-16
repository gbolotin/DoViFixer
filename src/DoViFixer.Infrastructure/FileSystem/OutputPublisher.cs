using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Diagnostics;
using DoViFixer.Infrastructure.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.FileSystem;
internal sealed class OutputPublisher(ILogger<OutputPublisher> logger, IFileLockInspector locks) : IOutputPublisher
{
    public IStagedOutput Stage(string destination, bool replaceExisting = false)
    {
        string full = Path.GetFullPath(destination);
        FileOperations.RejectReparsePoints(full);
        if ((File.Exists(full) && !replaceExisting) || Directory.Exists(full))
        {
            throw new OperationFailureException(new(FailureCategory.DestinationExists, "Publish output", full, null, "Output already exists."));
        }

        // Unique directory on the destination volume reserves ownership of the partial output.
        string directory = Path.Combine(Path.GetDirectoryName(full)!, ".dovifixer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        logger.LogDebug("Staging {Output} in {Directory}", full, directory);
        return new StagedOutput(full, directory, replaceExisting, logger, locks);
    }

    private sealed class StagedOutput(string destination, string directory, bool replaceExisting, ILogger logger, IFileLockInspector locks) : IStagedOutput
    {
        public string Path => System.IO.Path.Combine(directory, "output.partial");

        public async Task PublishAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(Path) || new FileInfo(Path).Length == 0)
            {
                throw new IOException("Verified output is missing or empty.");
            }

            for (int attempt = 0;; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileOperations.RejectReparsePoints(destination);
                try
                {
                    // Replacement happens only here, after the new output was verified.
                    File.Move(Path, destination, overwrite: replaceExisting);
                    logger.LogDebug("Published output {Output}", destination);
                    return;
                }
                catch (IOException ex)when ((ex.HResult & 0xFFFF)is 32 or 33 && attempt < 8)
                {
                    logger.LogDebug(ex, "Publication lock on {Output}; retry {Attempt}", destination, attempt + 1);
                    await Task.Delay(250, cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    var diagnosis = FailureClassifier.Describe(ex, "Publish output", destination);
                    if (diagnosis.Category is FailureCategory.FileInUse or FailureCategory.AccessDenied)
                    {
                        diagnosis = diagnosis with { FileLock = await locks.InspectAsync(destination, cancellationToken) };
                    }
                    logger.LogWarning(ex, "Output publication failed: {@Diagnosis}", diagnosis);
                    throw new OperationFailureException(diagnosis, ex);
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
            catch (Exception ex)when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Partial output remains in {Directory}", directory);
            }

            return ValueTask.CompletedTask;
        }
    }
}
