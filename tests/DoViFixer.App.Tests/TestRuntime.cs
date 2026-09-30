using DoViFixer.App.Composition;
using DoViFixer.App.Dialogs;
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
internal sealed class TestRuntime : IFileDiscovery, IFileOperations, IMediaProbe, IMediaPreview, ISettingsStore, IDependencyDetector, IDependencyInstaller, ITemporaryWorkspaceFactory, IAnalysisCache, IVideoProcessor, IMediaVerifier, IOutputPublisher, IBackupArchiveStore, IUserDialogs, IThemeService, IDisposable
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
        Type[] contracts = [typeof(IFileDiscovery), typeof(IFileOperations), typeof(IMediaProbe), typeof(IMediaPreview), typeof(ISettingsStore), typeof(IDependencyDetector), typeof(IDependencyInstaller), typeof(ITemporaryWorkspaceFactory), typeof(IAnalysisCache), typeof(IVideoProcessor), typeof(IMediaVerifier), typeof(IOutputPublisher), typeof(IBackupArchiveStore), typeof(IUserDialogs), typeof(IThemeService)];
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
    public IReadOnlyList<string> Discover(string input, int recursiveDepth, bool cleanup = false) => cleanup
        ? Archives.Where(path => string.Equals(Path.GetDirectoryName(path), input, StringComparison.OrdinalIgnoreCase)).ToArray()
        : DiscoverFiles?.Invoke(input) ?? [Path.GetFullPath(input)];
    public HashSet<string> Archives { get; } = new(StringComparer.OrdinalIgnoreCase);
    public FileIdentity Identify(string path)
    {
        if (path.EndsWith(".dovi", StringComparison.OrdinalIgnoreCase) && !Archives.Contains(path))
        {
            throw new FileNotFoundException("Archive missing.", path);
        }
        return new(Path.GetFullPath(path), 40000000000, new DateTime(2026, 9, 11));
    }
    public string PrepareOutputPath(string input, string? outputDirectory, string suffix, bool allowInput = false) => Path.Combine(outputDirectory ?? Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + suffix);
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
    public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        Probes++;
        var profile = path.Contains("P81") ? DolbyVisionProfile.Profile81 : path.Contains("Sdr") ? DolbyVisionProfile.None : DolbyVisionProfile.Profile7;
        return Task.FromResult(new MediaInfo(Identify(path), profile, "HEVC", 0, 3840, 2160, 1000, 23.976, 5772, 0, 1000, [], 0, 1, null, "{}"));
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
    public async Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken, bool skipConfiguredPaths = false)
    {
        if (DuringDependencyCheck is not null)
        {
            await DuringDependencyCheck(cancellationToken);
        }

        return new DependencyReport(tools.Select(t => new DependencyStatus(t, Ready ? DependencyState.Ready : DependencyState.Missing, @"C:\Tools\" + t + ".exe", "test", "Fixture")).ToArray());
    }
    public Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken) => Task.FromResult(new DependencyStatus(tool, DependencyState.Ready, path, "test", "Fixture"));
    public Task<InstallationPlan> PrepareAsync(DependencyReport report, CancellationToken cancellationToken) => Task.FromResult(new InstallationPlan(Guid.NewGuid(), [new InstallationItem("Fixture.Tools", "1.2.3", InstallationProvider.VerifiedZip, "https://example.invalid/tools.zip", @"C:\FixtureTools", "user", false, DependencyRequirements.All, new string ('a', 64))], []));
    public Task<IReadOnlyList<InstallationOutcome>> InstallAsync(InstallationPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        Installations++;
        Ready = InstallationSucceeds;
        return Task.FromResult<IReadOnlyList<InstallationOutcome>>([new("Fixture.Tools", InstallationSucceeds, "Fixture outcome")]);
    }

    public ValueTask<ITemporaryWorkspace> CreateAsync(long requiredBytes, string? directory, CancellationToken cancellationToken) => ValueTask.FromResult<ITemporaryWorkspace>(new Workspace());
    private readonly Dictionary<(FileIdentity, AnalysisMethod), MediaAnalysis> cache = new();
    private readonly Dictionary<FileIdentity, string> hashCache = new();
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
    public Dictionary<string, string> BaseLayerHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> ArchiveBaseLayerHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> HashedInputs { get; } = [];
    public List<string> ManifestReads { get; } = [];
    public Func<CancellationToken, Task>? DuringHash { get; set; }
    public async Task<string> GetBaseLayerSha256Async(MediaInfo media, ITemporaryWorkspace workspace, CancellationToken cancellationToken)
    {
        HashedInputs.Add(media.Source.Path);
        if (DuringHash is not null)
        {
            await DuringHash(cancellationToken);
        }
        return BaseLayerHashes.GetValueOrDefault(media.Source.Path, new string('A', 64));
    }
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
    public async Task RestoreAsync(MediaInfo media, ArchiveManifest? manifest, ITemporaryWorkspace workspace, string stagedOutput, CancellationToken cancellationToken)
    {
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
    public Task<IReadOnlyList<string>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null, Guid operationId = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public IStagedOutput Stage(string destination) => new Staged(destination);
    public string[] PickedFiles { get; set; } = [];
    public string? PickedFolder { get; set; }
    public string[] PickFiles(string filter = "Matroska media|*.mkv") => PickedFiles;
    public string? PickFolder() => PickedFolder;
    public string? OpenedFolder
    {
        get;
        private set;
    }
    public void OpenFolder(string path)
    {
        OpenedFolder = path;
    }

    public void OpenLogs()
    {
    }

    public bool DeletionConfirmationRequested { get; private set; }
    public string? LastApproveLabel { get; private set; }
    public bool Review(string title, string content, string approveLabel, bool confirmDeletion = false)
    {
        Reviews.Add(content);
        DeletionConfirmationRequested = confirmDeletion;
        LastApproveLabel = approveLabel;
        return Approval;
    }

    public List<string> Messages { get; } = [];
    public void ShowMessage(string message, string title = "DoViFixer")
    {
        Messages.Add($"{title}: {message}");
    }

    public AppTheme AppliedTheme
    {
        get;
        set;
    }
    = AppTheme.System;
    AppTheme IThemeService.CurrentTheme => AppliedTheme;
    void IThemeService.ApplyTheme(AppTheme theme) => AppliedTheme = theme;

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
