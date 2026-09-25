using DoViFixer.App.Composition;
using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Configuration;
using Prism.DryIoc;
using Prism.Ioc;

namespace DoViFixer.App.Tests;
internal sealed class TestRuntime : IFileDiscovery, IFileOperations, IMediaProbe, ISettingsStore, IDependencyDetector, IDependencyInstaller, ITemporaryWorkspaceFactory, IAnalysisCache, IVideoProcessor, IMediaVerifier, IOutputPublisher, IUserDialogs, IThemeService, IDisposable
{
    public IContainerProvider Container
    {
        get;
    }
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
        var container = new DryIocContainerExtension();
        AppComposition.Register(container, new ConfigurationBuilder().Build(), false);
        Type[] contracts = [typeof(IFileDiscovery), typeof(IFileOperations), typeof(IMediaProbe), typeof(ISettingsStore), typeof(IDependencyDetector), typeof(IDependencyInstaller), typeof(ITemporaryWorkspaceFactory), typeof(IAnalysisCache), typeof(IVideoProcessor), typeof(IMediaVerifier), typeof(IOutputPublisher), typeof(IUserDialogs), typeof(IThemeService)];
        foreach (var contract in contracts)
        {
            container.RegisterInstance(contract, this);
        }

        Container = container;
    }

    public void Dispose() => Container.GetContainer().Dispose();
    public IReadOnlyList<string> Discover(string input, int recursiveDepth, bool cleanup = false) => [Path.GetFullPath(input)];
    public FileIdentity Identify(string path) => new(Path.GetFullPath(path), 40000000000, new DateTime(2026, 9, 11));
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
    public Task DeleteAsync(FileIdentity identity, CancellationToken cancellationToken) => Task.CompletedTask;
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
    public Task<MediaAnalysis?> ReadAsync(FileIdentity source, AnalysisMethod method, CancellationToken cancellationToken) => Task.FromResult(cache.GetValueOrDefault((source, method)));
    public Task WriteAsync(MediaAnalysis analysis, CancellationToken cancellationToken)
    {
        cache[(analysis.Media.Source, analysis.Evidence.Method)] = analysis;
        return Task.CompletedTask;
    }
    public Task<int> ClearAsync(CancellationToken cancellationToken)
    {
        int count = cache.Count;
        cache.Clear();
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
    public Task RestoreAsync(MediaInfo media, ArchiveManifest? manifest, ITemporaryWorkspace workspace, string stagedOutput, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<string>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null, Guid operationId = default) => Task.FromResult<IReadOnlyList<string>>([]);
    public IStagedOutput Stage(string destination) => new Staged(destination);
    public string[] PickFiles(string filter = "Matroska media|*.mkv") => [];
    public string? PickFolder() => null;
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

    public bool Review(string title, string content, string approveLabel, string? requiredText = null)
    {
        Reviews.Add(content);
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
