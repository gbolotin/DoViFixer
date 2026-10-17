using DoViFixer.Application.Abstractions;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Library;

/// <summary>Reconciles remembered paths without running media analysis or removing missing rows.</summary>
public sealed class MediaLibraryService(IMediaLibrary library, IFileOperations files, IFileDiscovery discovery, ILogger<MediaLibraryService> logger)
{
    public Task<int> ForgetAsync(IReadOnlyList<string> paths, CancellationToken token) => library.ForgetAsync(paths, token);

    public async Task<int> CleanMissingAsync(CancellationToken token)
    {
        // Recover same-folder renames before deciding that a record is missing.
        await LoadAsync(token);
        return await library.CleanMissingAsync(token);
    }

    public Task<IReadOnlyList<LibraryEvent>> ReadHistoryAsync(string fingerprint, CancellationToken token) => library.ReadHistoryAsync(fingerprint, token);

    public async Task<LibraryFile> RememberAsync(string path, CancellationToken cancellationToken) =>
        await library.RememberAsync(files.Identify(path), cancellationToken);

    public async Task<IReadOnlyList<LibraryFile>> LoadAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, LibraryFile>(StringComparer.Ordinal);
        foreach (var entry in await library.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = entry;
            try
            {
                var source = files.Identify(entry.Source.Path);
                if (source != entry.Source)
                {
                    current = await library.RememberAsync(source, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                current = await FindRenamedAsync(entry, cancellationToken) ?? entry;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Library file is temporarily unavailable: {Path}", entry.Source.Path);
            }
            result[current.Fingerprint] = current;
        }
        return result.Values.ToArray();
    }

    private async Task<LibraryFile?> FindRenamedAsync(LibraryFile entry, CancellationToken cancellationToken)
    {
        string? folder = Path.GetDirectoryName(entry.Source.Path);
        if (folder is null)
        {
            return null;
        }
        try
        {
            foreach (string candidate in discovery.Discover(folder, 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var source = files.Identify(candidate);
                    if (source.Length == entry.Source.Length &&
                        await library.FindAsync(source, cancellationToken) is { } found && found.Fingerprint == entry.Fingerprint)
                    {
                        return await library.RememberAsync(source, cancellationToken);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not identify library candidate {Path}", candidate);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not search the last known folder {Folder}", folder);
        }
        return null;
    }
}
