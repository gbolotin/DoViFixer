using DoViFixer.App.Composition;
using DoViFixer.App.Dialogs;
using WpfFoundation.Dialogs;
using WpfFoundation.Theming;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DoViFixer.App.Tests;
internal sealed class TestRuntime : IFileDiscovery, IFileOperations, IMediaProbe, IMediaPreview, ISettingsStore, IDependencyDetector, IDependencyInstaller, ITemporaryWorkspaceFactory, IAnalysisCache, IVideoProcessor, IMediaVerifier, IOutputPublisher, IBackupArchiveStore, IDialogService, IFileDialogService, IFileExplorer, IThemeService, ISourceFileMonitor, IDisposable
{
    private ServiceProvider? container;
    public IServiceCollection Services { get; } = new ServiceCollection();
    public ServiceProvider Container => container ??= Services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    public int Conversions
    {
        get;
        private set;
    }
    public int Probes
    {
        get;
        private set;
    }
    public int FullAnalyses
    {
        get;
        private set;
    }
    public bool Ready
    {
        get;
        set;
    }
    = true;
    public int Installations
    {
        get;
        private set;
    }
    public bool InstallationSucceeds
    {
        get;
        set;
    }
    = true;
    public bool FailDirectoryValidation
    {
        get;
        set;
    }
    public UserSettings Settings
    {
        get;
        private set;
    }
    = new();
    public bool Approval
    {
        get;
        set;
    }
    public List<string> Reviews
    {
        get;
    }
    = [];
    public Func<CancellationToken, Task>? DuringConversion
    {
        get;
        set;
    }

    public TestRuntime()
    {
        AppComposition.Register(Services, new ConfigurationBuilder().Build(), false);
        Type[] contracts = [typeof(IFileDiscovery), typeof(IFileOperations), typeof(IMediaProbe), typeof(IMediaPreview), typeof(ISettingsStore), typeof(IDependencyDetector), typeof(IDependencyInstaller), typeof(ITemporaryWorkspaceFactory), typeof(IAnalysisCache), typeof(IVideoProcessor), typeof(IMediaVerifier), typeof(IOutputPublisher), typeof(IBackupArchiveStore), typeof(IDialogService), typeof(IFileDialogService), typeof(IFileExplorer), typeof(IThemeService), typeof(ISourceFileMonitor)];
        foreach (var contract in contracts)
        {
            Services.AddSingleton(contract, this);
        }
    }

    public void Dispose() => container?.Dispose();
    public Func<string, CancellationToken, Task<byte[]>> Preview { get; set; } = (_, _) => Task.FromException<byte[]>(new IOException("Fixture preview unavailable."));
    public Task<byte[]> LoadAsync(string path, CancellationToken cancellationToken) => Preview(path, cancellationToken);
    public int CachedFrames { get; set; }
    public string RootDirectory => @"C:\FixtureData\cache";
    public long AnalysisCacheBytes { get; set; }
    public long FrameCacheBytes { get; set; }
    public Task<long> GetSizeAsync(CancellationToken cancellationToken) => Task.FromResult(AnalysisCacheBytes);
    public Task<long> GetCacheSizeAsync(CancellationToken cancellationToken) => Task.FromResult(FrameCacheBytes);
    public Task<int> ClearCacheAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int removed = CachedFrames;
        CachedFrames = 0;
        FrameCacheBytes = 0;
        return Task.FromResult(removed);
    }
    public Func<string, IReadOnlyList<string>>? DiscoverFiles { get; set; }
    public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsSupportedInput(string input) => !string.IsNullOrWhiteSpace(input) && (input.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) || Folders.Contains(input));
    public Exception? ArchiveDiscoveryFailure { get; set; }
    public IReadOnlyList<string> Discover(string input, int recursiveDepth, bool cleanup = false)
    {
        if (!cleanup)
        {
            return DiscoverFiles?.Invoke(input) ?? [Path.GetFullPath(input)];
        }

        if (ArchiveDiscoveryFailure is not null)
        {
            throw ArchiveDiscoveryFailure;
        }

        return Archives.Where(path => string.Equals(Path.GetDirectoryName(path), input, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public HashSet<string> Archives { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MissingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> WatchedPaths { get; private set; } = [];
    public event EventHandler? Changed;
    public void Watch(IEnumerable<string> paths) => WatchedPaths = [.. paths];
    public bool Exists(string path) => !MissingFiles.Contains(path);
    public void RaiseSourceFilesChanged() => Changed?.Invoke(this, EventArgs.Empty);
    public FileIdentity Identify(string path)
    {
        if (path.EndsWith(".dovi", StringComparison.OrdinalIgnoreCase) && !Archives.Contains(path))
        {
            throw new FileNotFoundException("Archive missing.", path);
        }

        if (MissingFiles.Contains(path))
        {
            throw new FileNotFoundException("Source file not found.", path);
        }
        return new(Path.GetFullPath(path), 40000000000, new DateTime(2026, 9, 11));
    }
    /// <summary>Output paths that already exist on the fixture disk.</summary>
    public HashSet<string> ExistingOutputFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Path, bool ReplaceExisting)> StagedOutputs { get; } = [];
    public string PrepareOutputPath(string input, string? outputDirectory, string suffix, bool allowInput = false, ExistingOutputHandling existing = ExistingOutputHandling.Skip)
    {
        string output = Path.Combine(outputDirectory ?? Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + suffix);
        if (!ExistingOutputFiles.Contains(output) || existing == ExistingOutputHandling.Replace)
        {
            return output;
        }

        if (existing == ExistingOutputHandling.Skip)
        {
            throw new OutputExistsException(output);
        }

        return Enumerable.Range(1, 100).Select(number => Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)} ({number}){Path.GetExtension(output)}")).First(path => !ExistingOutputFiles.Contains(path));
    }
    public FileIdentity RenameOriginal(FileIdentity identity) => identity with
    {
        Path = identity.Path + ".bak.dovi_convert"
    };
    public void RestoreOriginal(FileIdentity backupIdentity, string originalPath)
    {
    }

    public bool FailAvailableSpace
    {
        get;
        set;
    }

    public void EnsureAvailableSpace(string directory, long requiredBytes)
    {
        if (FailAvailableSpace)
        {
            throw new IOException("Insufficient disk space");
        }
    }

    public void EnsureWritableDirectory(string directory)
    {
        if (FailDirectoryValidation)
        {
            throw new IOException("Directory unavailable");
        }
    }

    public ValueTask<IAsyncDisposable> AcquireReadLeaseAsync(FileIdentity identity, CancellationToken cancellationToken) => ValueTask.FromResult<IAsyncDisposable>(new Workspace());
    public List<string> DeletedFiles { get; } = [];
    public Task DeleteAsync(FileIdentity identity, CancellationToken cancellationToken)
    {
        DeletedFiles.Add(identity.Path);
        return Task.CompletedTask;
    }
    public Func<CancellationToken, Task>? DuringProbe
    {
        get;
        set;
    }
    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        Probes++;
        if (DuringProbe is not null)
        {
            await DuringProbe(cancellationToken);
        }

        var profile = path.Contains("P81") ? DolbyVisionProfile.Profile81 : path.Contains("Sdr") ? DolbyVisionProfile.None : DolbyVisionProfile.Profile7;
        return new MediaInfo(Identify(path), profile, "HEVC", 0, 3840, 2160, 1000, 23.976, 5772, 0, 1000, [], 0, 1, null, "{}");
    }

    public Func<CancellationToken, Task>? DuringAnalysis
    {
        get;
        set;
    }
    public int Analyses
    {
        get;
        private set;
    }
    public async Task<RpuEvidence> AnalyzeAsync(MediaInfo media, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null)
    {
        Analyses++;
        progress?.Report(new(Guid.Empty, method == AnalysisMethod.SampledRpu ? "Analyzing sample 5 of 10" : "Extracting video", media.Source.Path, 40));
        if (DuringAnalysis is not null)
        {
            await DuringAnalysis(cancellationToken);
        }

        if (method == AnalysisMethod.FullRpu)
        {
            FullAnalyses++;
        }

        bool mel = media.Source.Path.Contains("Mountain");
        return new RpuEvidence(method, mel ? EnhancementLayer.Mel : EnhancementLayer.Fel, 1000, media.Source.Path.Contains("City") ? 1400 : 900, 10, 10);
    }

    public Task<UserSettings> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
    public Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken cancellationToken)
    {
        Settings = update(Settings);
        return Task.CompletedTask;
    }

    public Func<CancellationToken, Task>? DuringDependencyCheck { get; set; }
    public HashSet<NativeTool> MissingTools { get; } = [];
    public async Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken, bool skipConfiguredPaths = false)
    {
        if (DuringDependencyCheck is not null)
        {
            await DuringDependencyCheck(cancellationToken);
        }

        return new DependencyReport(tools.Select(t => new DependencyStatus(t, Ready && !MissingTools.Contains(t) ? DependencyState.Ready : DependencyState.Missing, @"C:\Tools\" + t + ".exe", "test", "Fixture")).ToArray());
    }
    public Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken) => Task.FromResult(new DependencyStatus(tool, DependencyState.Ready, path, "test", "Fixture"));
    public bool InstallationAvailable { get; set; } = true;
    public Task<InstallationPlan> PrepareAsync(DependencyReport report, CancellationToken cancellationToken) => Task.FromResult(InstallationAvailable
        ? new InstallationPlan(Guid.NewGuid(), [new InstallationItem("Fixture.Tools", "1.2.3", InstallationProvider.VerifiedZip, "https://example.invalid/tools.zip", @"C:\FixtureTools", "user", false, DependencyRequirements.All, new string ('a', 64))], [])
        : new InstallationPlan(Guid.NewGuid(), [], ["No automatic installer for the fixture tools."]));
    public Task<IReadOnlyList<InstallationOutcome>> InstallAsync(InstallationPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        Installations++;
        Ready = InstallationSucceeds;
        if (InstallationSucceeds)
        {
            MissingTools.Clear();
        }
        return Task.FromResult<IReadOnlyList<InstallationOutcome>>([new("Fixture.Tools", InstallationSucceeds, "Fixture outcome")]);
    }

    public ValueTask<ITemporaryWorkspace> CreateAsync(long requiredBytes, string? directory, CancellationToken cancellationToken) => ValueTask.FromResult<ITemporaryWorkspace>(new Workspace());
    private readonly Dictionary<(FileIdentity, AnalysisMethod), MediaAnalysis> cache = new();
    private readonly Dictionary<FileIdentity, string> hashCache = new();
    public Dictionary<FileIdentity, OperationItemResult> ConversionResults { get; } = [];
    public Task<OperationItemResult?> ReadConversionResultAsync(FileIdentity source, CancellationToken cancellationToken) => Task.FromResult(ConversionResults.GetValueOrDefault(source));
    public Task WriteConversionResultAsync(FileIdentity source, OperationItemResult result, CancellationToken cancellationToken)
    {
        ConversionResults[source] = result;
        return Task.CompletedTask;
    }
    public Task<string?> ReadBaseLayerHashAsync(FileIdentity source, CancellationToken cancellationToken) => Task.FromResult(hashCache.GetValueOrDefault(source));
    public Task WriteBaseLayerHashAsync(FileIdentity source, string sha256, CancellationToken cancellationToken)
    {
        hashCache[source] = sha256;
        return Task.CompletedTask;
    }
    public Task<MediaAnalysis?> ReadAsync(FileIdentity source, AnalysisMethod method, CancellationToken cancellationToken) => Task.FromResult(cache.GetValueOrDefault((source, method)));
    public Task WriteAsync(MediaAnalysis analysis, CancellationToken cancellationToken)
    {
        cache[(analysis.Media.Source, analysis.Evidence.Method)] = analysis;
        return Task.CompletedTask;
    }
    public Task<int> ClearAsync(CancellationToken cancellationToken)
    {
        int count = cache.Count;
        count += hashCache.Count;
        hashCache.Clear();
        cache.Clear();
        AnalysisCacheBytes = 0;
        return Task.FromResult(count);
    }
    public async Task ConvertAsync(MediaInfo media, ConversionTarget target, ITemporaryWorkspace workspace, string stagedOutput, IProgress<OperationProgress>? progress, Guid operationId, CancellationToken cancellationToken, bool safe = false)
    {
        Conversions++;
        progress?.Report(new(operationId, "Converting", media.Source.Path, 45));
        if (DuringConversion is not null)
        {
            await DuringConversion(cancellationToken);
        }
    }

    public Task<ArchiveManifest> ExtractBackupAsync(MediaInfo media, ITemporaryWorkspace workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Dictionary<string, string?> ArchiveBaseLayerHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ManifestReads { get; } = [];
    public Task<ArchiveManifest?> ReadManifestAsync(string archive, CancellationToken cancellationToken)
    {
        ManifestReads.Add(archive);
        string? hash = ArchiveBaseLayerHashes.GetValueOrDefault(archive, new string('A', 64));
        if (hash == "invalid")
        {
            throw new InvalidDataException("Invalid manifest.");
        }
        return Task.FromResult<ArchiveManifest?>(hash is null ? null : new(1, "source.mkv", hash, new string('B', 64), 1000, 1000, DateTimeOffset.UnixEpoch));
    }
    public List<string> RestoredInputs { get; } = [];
    public string? ReadArchive { get; private set; }
    public bool? AllowLegacyArchive { get; private set; }
    public bool FailArchiveValidation { get; set; }
    public Func<CancellationToken, Task>? DuringRestore { get; set; }
    public string? RestoreBaseLayerSha256 { get; set; }
    public async Task RestoreAsync(MediaInfo media, ArchiveManifest? manifest, ITemporaryWorkspace workspace, string stagedOutput, CancellationToken cancellationToken)
    {
        if (manifest is not null && RestoreBaseLayerSha256 is { } actual && !string.Equals(actual, manifest.BaseLayerSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BaseLayerMismatchException(actual);
        }

        RestoredInputs.Add(media.Source.Path);
        if (DuringRestore is not null)
        {
            await DuringRestore(cancellationToken);
        }
    }
    public Task WriteAsync(string stagedArchive, ArchiveManifest manifest, ITemporaryWorkspace workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ArchiveManifest?> ReadAsync(string archive, ITemporaryWorkspace workspace, bool allowLegacy, CancellationToken cancellationToken)
    {
        ReadArchive = archive;
        AllowLegacyArchive = allowLegacy;
        if (FailArchiveValidation)
        {
            throw new InvalidDataException("Archive payload SHA-256 is invalid.");
        }
        return Task.FromResult<ArchiveManifest?>(new(1, "source.mkv", new string('A', 64), new string('B', 64), 1000, 1000, DateTimeOffset.UnixEpoch));
    }
    public Task<IReadOnlyList<VerificationFinding>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null, Guid operationId = default) => Task.FromResult<IReadOnlyList<VerificationFinding>>([]);
    public IStagedOutput Stage(string destination, bool replaceExisting = false)
    {
        StagedOutputs.Add((destination, replaceExisting));
        return new Staged(destination);
    }
    public string[] PickedFiles { get; set; } = [];
    public string? PickedFolder { get; set; }
    public List<string> FileFilters { get; } = [];
    public IReadOnlyList<string> PickFiles(string filter, bool allowMultiple = true)
    {
        FileFilters.Add(filter);
        return allowMultiple ? PickedFiles : PickedFiles.Take(1).ToArray();
    }
    public string? PickFolder() => PickedFolder;
    public string? PickSaveFile(string filter, string? fileName = null) => null;
    public string? OpenedFolder
    {
        get;
        private set;
    }
    public void OpenFolder(string path)
    {
        OpenedFolder = path;
    }

    public string? ShownFile
    {
        get;
        private set;
    }
    public void ShowInFolder(string filePath)
    {
        ShownFile = filePath;
    }

    public void OpenLogs()
    {
    }

    public string? LastApproveLabel { get; private set; }
    public List<string> Messages { get; } = [];
    public List<string> ExistingOutputQuestions { get; } = [];
    /// <summary>The answer to "File already exists"; null cancels.</summary>
    public ExistingOutputHandling? ExistingOutputAnswer { get; set; }

    // Reviews answer with Approval; messages are recorded and closed.
    public Task<bool> ShowAsync(DialogViewModel dialog)
    {
        switch (dialog)
        {
            case ReviewDialogViewModel review:
                Reviews.Add(review.Content);
                LastApproveLabel = review.Buttons[0].Label;
                if (Approval)
                {
                    review.Accept();
                }
                else
                {
                    review.Cancel();
                }
                break;
            case MessageDialogViewModel message:
                Messages.Add($"{message.Title}: {message.Message}");
                message.Accept();
                break;
            case ExistingOutputDialogViewModel existing:
                ExistingOutputQuestions.Add(existing.ExistingPath);
                if (ExistingOutputAnswer is { } answer)
                {
                    existing.Choose(answer);
                }
                else
                {
                    existing.Cancel();
                }
                break;
            default:
                throw new NotSupportedException($"The test runtime does not answer {dialog.GetType().Name}.");
        }

        return dialog.Completion;
    }

    public ThemePreference AppliedTheme
    {
        get;
        set;
    }
    = ThemePreference.System;
    ThemePreference IThemeService.CurrentTheme => AppliedTheme;
    void IThemeService.ApplyTheme(ThemePreference theme) => AppliedTheme = theme;

    private sealed class Workspace : ITemporaryWorkspace
    {
        public string DirectoryPath => @"C:\FixtureScratch";

        public string File(string name) => Path.Combine(DirectoryPath, name);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Staged(string path) : IStagedOutput
    {
        public string Path => path;

        public Task PublishAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
