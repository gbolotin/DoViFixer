using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Abstractions;
public interface IFileDiscovery
{
    bool IsSupportedInput(string input);
    IReadOnlyList<string> Discover(string input, int recursiveDepth, bool cleanup = false);
}

public interface IFileOperations
{
    FileIdentity Identify(string path);
    /// <summary>The output path for <paramref name="input"/>; an existing file there is handled as <paramref name="existing"/> says.</summary>
    /// <exception cref="OutputExistsException">The output exists and <paramref name="existing"/> is <see cref="ExistingOutputHandling.Skip"/>.</exception>
    string PrepareOutputPath(string input, string? outputDirectory, string suffix, bool allowInput = false, ExistingOutputHandling existing = ExistingOutputHandling.Skip);
    FileIdentity RenameOriginal(FileIdentity identity);
    void RestoreOriginal(FileIdentity backupIdentity, string originalPath);
    void EnsureAvailableSpace(string directory, long requiredBytes);
    void EnsureWritableDirectory(string directory);
    ValueTask<IAsyncDisposable> AcquireReadLeaseAsync(FileIdentity identity, CancellationToken cancellationToken);
    Task DeleteAsync(FileIdentity identity, CancellationToken cancellationToken);
}

public interface ITemporaryWorkspace : IAsyncDisposable
{
    string DirectoryPath
    {
        get;
    }

    string File(string name);
}

public interface ITemporaryWorkspaceFactory
{
    ValueTask<ITemporaryWorkspace> CreateAsync(long requiredBytes, string? directory, CancellationToken cancellationToken);
}

public interface IStagedOutput : IAsyncDisposable
{
    string Path
    {
        get;
    }

    Task PublishAsync(CancellationToken cancellationToken);
}

public interface IOutputPublisher
{
    IStagedOutput Stage(string destination, bool replaceExisting = false);
}

/// <summary>What to do when a conversion output already exists.</summary>
public enum ExistingOutputHandling
{
    /// <summary>Leave the existing file and do not convert.</summary>
    Skip,
    /// <summary>Replace the existing file once the new output is verified.</summary>
    Replace,
    /// <summary>Keep the existing file and save under a numbered name, such as "Movie - DV P8.1 (1).mkv".</summary>
    KeepBoth
}

public sealed class OutputExistsException(string path) : IOException($"Output collision: {path}. Existing files will not be overwritten.")
{
    public string Path { get; } = path;
}
