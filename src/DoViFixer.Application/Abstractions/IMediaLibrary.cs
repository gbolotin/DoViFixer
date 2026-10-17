using DoViFixer.Application.Library;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Abstractions;

/// <summary>Persistent collection records. Cleaning never deletes media or preview files.</summary>
public interface IMediaLibrary
{
    string FilePath { get; }
    Task<LibraryFile?> FindAsync(FileIdentity source, CancellationToken cancellationToken);
    Task<LibraryFile> RememberAsync(FileIdentity source, CancellationToken cancellationToken);
    Task<IReadOnlyList<LibraryFile>> ReadAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LibraryEvent>> ReadHistoryAsync(string fingerprint, CancellationToken cancellationToken);
    Task<int> ForgetAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken);
    Task<int> DeleteAsync(CancellationToken cancellationToken);
    Task<int> CleanMissingAsync(CancellationToken cancellationToken);
    Task<long> GetSizeAsync(CancellationToken cancellationToken);
}
