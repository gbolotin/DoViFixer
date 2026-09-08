using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Abstractions;

public interface IFileDiscovery
{
    IReadOnlyList<string> Discover(string input, int recursiveDepth, bool cleanup = false);
}
public interface IFileOperations
{
    FileIdentity Identify(string path);
    string PrepareOutputPath(string input, string? outputDirectory, string suffix);
    void EnsureAvailableSpace(string directory, long requiredBytes);
    void EnsureWritableDirectory(string directory);
    ValueTask<IAsyncDisposable> AcquireReadLeaseAsync(FileIdentity identity, CancellationToken cancellationToken);
    Task DeleteAsync(FileIdentity identity, CancellationToken cancellationToken);
}
public interface ITemporaryWorkspace : IAsyncDisposable
{
    string DirectoryPath { get; }
    string File(string name);
}
public interface ITemporaryWorkspaceFactory
{
    ValueTask<ITemporaryWorkspace> CreateAsync(long requiredBytes, string? directory, CancellationToken cancellationToken);
}
public interface IStagedOutput : IAsyncDisposable
{
    string Path { get; }
    Task PublishAsync(CancellationToken cancellationToken);
}
public interface IOutputPublisher
{
    IStagedOutput Stage(string destination);
}
