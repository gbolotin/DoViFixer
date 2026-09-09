using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Scanning;
using DoViFixer.Application.Inspection;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DoViFixer.Application.Tests;

[TestClass]
public sealed class WorkflowTests
{
    [TestMethod]
    public async Task ScanReportsEachSuccessOrFailureBeforeStartingNextFile()
    {
        var runtime = new Runtime();
        var inspection = new InspectionService(runtime.Dependencies(), runtime, runtime, runtime, runtime, NullLogger<InspectionService>.Instance);
        var service = new ScanService(runtime, inspection, NullLogger<ScanService>.Instance);
        var reported = new List<ScanItem>();
        var results = await service.ScanAsync("fixture", 0, null, null, default, new InlineScanProgress(item =>
        {
            reported.Add(item);
            Assert.AreEqual(reported.Count, runtime.ProbeCalls, "Result must be delivered before the next probe starts.");
        }));
        CollectionAssert.AreEqual(results.ToArray(), reported.ToArray());
        Assert.IsNotNull(reported[0].Error);
        Assert.AreEqual(AnalysisVerdict.Mel, reported[1].Analysis!.Verdict);
    }

    private sealed class InlineScanProgress(Action<ScanItem> report) : IProgress<ScanItem>
    {
        public void Report(ScanItem value) => report(value);
    }
    [TestMethod]
    public async Task DependencyCheckDoesNotInstallAndMissingBlocksExecution()
    {
        var runtime = new Runtime { Ready = false };
        var dependencies = runtime.Dependencies();
        Assert.IsFalse((await dependencies.CheckAsync(DependencyRequirements.All, default)).Ready);
        await Assert.ThrowsExactlyAsync<DependencyNotReadyException>(() => dependencies.RequireAsync(DependencyRequirements.All, default));
        Assert.AreEqual(0, runtime.InstallCalls);
    }

    [TestMethod]
    public async Task SuccessfulInstallIsRedetectedPersistedAndImmediatelyAvailable()
    {
        var runtime = new Runtime { Ready = false };
        var result = await runtime.Dependencies().InstallAsync(new(Guid.NewGuid(), new[] { new InstallationItem("tools", "1", InstallationProvider.VerifiedZip,
            "https://example.test", "test", "user", false, DependencyRequirements.All) }, []), null, default);
        Assert.IsTrue(result.Report.Ready);
        Assert.AreEqual(1, runtime.InstallCalls);
        Assert.AreEqual(6, runtime.Settings.ToolPaths.Count);
        Assert.AreEqual("ready-DoviTool", runtime.GetPath(NativeTool.DoviTool));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ReplacementPathIsSavedOnlyAfterSuccessfulValidation(bool valid)
    {
        var runtime = new Runtime { InstallationMakesReady = valid };
        await runtime.UpdateAsync(s => s with { ToolPaths = s.ToolPaths
            .SetItem(NativeTool.MediaInfo, "invalid-gui.exe")
            .SetItem(NativeTool.FFmpeg, "working-ffmpeg.exe") }, default);
        var plan = new InstallationPlan(Guid.NewGuid(), new[] { new InstallationItem("mediainfo", "1", InstallationProvider.VerifiedZip,
            "https://example.test", "test", "user", false, new[] { NativeTool.MediaInfo }) }, []);
        await runtime.Dependencies().InstallAsync(plan, null, default);
        Assert.AreEqual(valid ? "ready-MediaInfo" : "invalid-gui.exe", runtime.Settings.ToolPaths[NativeTool.MediaInfo]);
        Assert.AreEqual("working-ffmpeg.exe", runtime.Settings.ToolPaths[NativeTool.FFmpeg]);
        Assert.AreEqual("invalid-gui.exe", runtime.ConfiguredPathDuringRediscovery);
        if (valid)
        {
            Assert.AreEqual("ready-MediaInfo", runtime.GetPath(NativeTool.MediaInfo));
        }
    }

    [TestMethod]
    public async Task InstallerSuccessAloneDoesNotProveReadiness()
    {
        var runtime = new Runtime { Ready = false, InstallationMakesReady = false };
        var result = await runtime.Dependencies().InstallAsync(new(Guid.NewGuid(), [], []), null, default);
        Assert.IsFalse(result.Report.Ready);
        Assert.AreEqual(0, runtime.Settings.ToolPaths.Count);
    }

    [TestMethod]
    public async Task BatchContinuesAndDisposesAfterVerificationFailure()
    {
        var runtime = new Runtime();
        var batch = new BatchConversionService(runtime.Conversion(), NullLogger<BatchConversionService>.Instance);
        var result = await batch.ExecuteAsync(new[] { Plan("bad.mkv"), Plan("good.mkv") }, null, default);
        Assert.AreEqual(OperationStatus.Partial, result.Status);
        Assert.AreEqual(2, runtime.ConvertCalls);
        Assert.AreEqual(1, runtime.Published);
        Assert.AreEqual(6, runtime.Disposed);
        Assert.AreEqual(0, runtime.Deleted);
        Assert.IsTrue(runtime.ConversionLog.Entries.Any(e => e.Exception is InvalidDataException));
        var published = runtime.ConversionLog.Entries.Where(e => Equals(e.Properties.GetValueOrDefault("Action"), "PublishConversion")).ToArray();
        Assert.AreEqual(1, published.Length);
        Assert.AreEqual(Plan("good.mkv").Output, published[0].Properties["Target"]);
        Assert.AreEqual(1, runtime.ConversionLog.Entries.Count(e => Equals(e.Properties.GetValueOrDefault("Outcome"), "Failed") && e.Properties.ContainsKey("ElapsedMilliseconds")));
    }

    [TestMethod]
    public async Task ChangedSourceStopsBeforeProcessing()
    {
        var runtime = new Runtime { Changed = true };
        await Assert.ThrowsExactlyAsync<IOException>(() => runtime.Conversion().ExecuteAsync(Plan("good.mkv"), null, default));
        Assert.AreEqual(0, runtime.ConvertCalls);
        Assert.IsTrue(runtime.ConversionLog.Entries.Any(e => e.Exception is IOException && Equals(e.Properties.GetValueOrDefault("Outcome"), "Failed")));
        Assert.IsFalse(runtime.ConversionLog.Entries.Any(e => Equals(e.Properties.GetValueOrDefault("LogKind"), "Audit")));
    }

    [TestMethod]
    public async Task CancellationStopsBatchAndDisposesOwnedResources()
    {
        using var cancellation = new CancellationTokenSource();
        var runtime = new Runtime { CancelDuringConversion = cancellation };
        var result = await new BatchConversionService(runtime.Conversion(), NullLogger<BatchConversionService>.Instance).ExecuteAsync(new[] { Plan("good.mkv"), Plan("next.mkv") }, null, cancellation.Token);
        Assert.AreEqual(OperationStatus.Cancelled, result.Status);
        Assert.AreEqual(1, runtime.ConvertCalls);
        Assert.AreEqual(3, runtime.Disposed);
        Assert.AreEqual(0, runtime.Published);
        Assert.IsTrue(runtime.ConversionLog.Entries.Any(e => Equals(e.Properties.GetValueOrDefault("Outcome"), "Cancelled")));
        Assert.IsFalse(runtime.ConversionLog.Entries.Any(e => Equals(e.Properties.GetValueOrDefault("Outcome"), "Completed")));
    }

    private static ConversionPlan Plan(string name)
    {
        string path = Path.GetFullPath(name);
        var media = new MediaInfo(new(path, 1000, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 24, 1,
            0, null, new[] { new MediaTrack(0, "video", "HEVC", "und", "", true, false, "1") }, 0, 0, "", "{}");
        var analysis = MediaClassifier.Classify(media, new(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 24, null, 1, 1));
        return new(Guid.NewGuid(), analysis, ConversionTarget.Profile81, path + ".dv81.mkv", null, null, 10000, "MEL");
    }

    private sealed class Runtime : IDependencyDetector, IDependencyInstaller, ISettingsStore, IToolCatalog, IFileOperations, IFileDiscovery, IMediaProbe,
        ITemporaryWorkspaceFactory, IVideoProcessor, IMediaVerifier, IOutputPublisher, IBackupArchiveStore
    {
        public bool Ready { get; set; } = true;
        public bool Changed { get; set; }
        public bool InstallationMakesReady { get; set; } = true;
        public string? ConfiguredPathDuringRediscovery { get; private set; }
        public CancellationTokenSource? CancelDuringConversion { get; set; }
        public int InstallCalls, ConvertCalls, Published, Disposed, Deleted;
        public int ProbeCalls;
        public IReadOnlyList<string> Discover(string input, int recursiveDepth, bool cleanup = false) => new[] { "bad.mkv", "good.mkv" };
        public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken)
        {
            ProbeCalls++;
            if (path == "bad.mkv")
            {
                throw new IOException("Unreadable fixture");
            }
            return Task.FromResult(Plan(path).Analysis.Media);
        }
        public Task<RpuEvidence> AnalyzeAsync(MediaInfo media, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken) =>
            Task.FromResult(new RpuEvidence(method, EnhancementLayer.Mel, 24, null, 10, 10));
        public UserSettings Settings { get; private set; } = new();
        private readonly Dictionary<NativeTool, string> catalog = new();
        public DependencyService Dependencies() => new(this, this, this, this, NullLogger<DependencyService>.Instance);
        public ConversionService Conversion() => new(Dependencies(), this, this, this, this, this, this, ConversionLog);
        public RecordingLogger<ConversionService> ConversionLog { get; } = new();
        public Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken, bool skipConfiguredPaths = false)
        {
            if (skipConfiguredPaths)
            {
                ConfiguredPathDuringRediscovery = Settings.ToolPaths.GetValueOrDefault(NativeTool.MediaInfo);
            }
            return Task.FromResult(new DependencyReport(tools.Select(t =>
            {
                if (!skipConfiguredPaths && Settings.ToolPaths.TryGetValue(t, out string? path))
                {
                    return new DependencyStatus(t, path.StartsWith("invalid", StringComparison.Ordinal) ? DependencyState.Unusable : DependencyState.Ready, path, "1", "configured");
                }
                return new DependencyStatus(t, Ready ? DependencyState.Ready : DependencyState.Missing,
                    Ready ? "ready-" + t : null, Ready ? "1" : null, "fixture");
            }).ToArray()));
        }
        public Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallationPlan> PrepareAsync(DependencyReport report, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InstallationOutcome>> InstallAsync(InstallationPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
        {
            InstallCalls++;
            Ready = InstallationMakesReady;
            return Task.FromResult<IReadOnlyList<InstallationOutcome>>(approvedPlan.Items.Select(i => new InstallationOutcome(i.Id, true, "fixture")).ToArray());
        }
        public Task<UserSettings> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
        public Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken cancellationToken)
        {
            Settings = update(Settings);
            return Task.CompletedTask;
        }
        public string GetPath(NativeTool tool) => catalog[tool];
        public void Refresh(IEnumerable<DependencyStatus> statuses)
        {
            foreach (var status in statuses.Where(s => s.State == DependencyState.Ready))
            {
                catalog[status.Tool] = status.Path!;
            }
        }
        public FileIdentity Identify(string path) => Plan(path).Analysis.Media.Source;
        public string PrepareOutputPath(string input, string? outputDirectory, string suffix) => throw new NotSupportedException();
        public void EnsureAvailableSpace(string directory, long requiredBytes) { }
        public void EnsureWritableDirectory(string directory) { }
        public ValueTask<IAsyncDisposable> AcquireReadLeaseAsync(FileIdentity identity, CancellationToken cancellationToken)
        {
            if (Changed)
            {
                throw new IOException("changed");
            }
            return ValueTask.FromResult<IAsyncDisposable>(new Owned(this));
        }
        public Task DeleteAsync(FileIdentity identity, CancellationToken cancellationToken)
        {
            Deleted++;
            return Task.CompletedTask;
        }
        public ValueTask<ITemporaryWorkspace> CreateAsync(long requiredBytes, string? directory, CancellationToken cancellationToken) => ValueTask.FromResult<ITemporaryWorkspace>(new Owned(this));
        public IStagedOutput Stage(string destination) => new Owned(this);
        public Task ConvertAsync(MediaInfo media, ConversionTarget target, ITemporaryWorkspace workspace, string stagedOutput,
            IProgress<OperationProgress>? progress, Guid operationId, CancellationToken cancellationToken)
        {
            ConvertCalls++;
            CancelDuringConversion?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<string>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile, ITemporaryWorkspace workspace, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(source.Source.Path.Contains("bad", StringComparison.Ordinal) ? new[] { "invalid frame count" } : []);
        public Task<ArchiveManifest> ExtractBackupAsync(MediaInfo media, ITemporaryWorkspace workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RestoreAsync(MediaInfo media, ArchiveManifest? manifest, ITemporaryWorkspace workspace, string stagedOutput, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteAsync(string stagedArchive, ArchiveManifest manifest, ITemporaryWorkspace workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ArchiveManifest?> ReadAsync(string archive, ITemporaryWorkspace workspace, bool allowLegacy, CancellationToken cancellationToken) => throw new NotSupportedException();

        private sealed class Owned(Runtime owner) : ITemporaryWorkspace, IStagedOutput
        {
            public string DirectoryPath => "fixture";
            public string Path => "fixture";
            public string File(string name) => name;
            public Task PublishAsync(CancellationToken cancellationToken)
            {
                owner.Published++;
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync()
            {
                owner.Disposed++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
