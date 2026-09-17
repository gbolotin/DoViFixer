using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Operations;
using DoViFixer.Infrastructure.Archives;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.TemporaryStorage;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class RpuCoverageTests
{
    [TestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitTailFixtureInspectsConvertsAndVerifies(bool safe)
    {
        string? fixture = Environment.GetEnvironmentVariable("DOVIFIXER_TAIL_FIXTURE");
        if (fixture is null)
        {
            Assert.Inconclusive("Set DOVIFIXER_TAIL_FIXTURE to an HEVC diagnostic fixture with a metadata-free ending.");
        }

        var tools = new ToolCatalog();
        foreach (var tool in DependencyRequirements.All)
        {
            string? path = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_" + tool.ToString().ToUpperInvariant());
            if (path is null || !File.Exists(path))
            {
                Assert.Inconclusive($"Set DOVIFIXER_TEST_{tool.ToString().ToUpperInvariant()}.");
            }

            tools.Refresh([new(tool, DependencyState.Ready, path, "test", "explicit fixture")]);
        }

        var files = new FileOperations();
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var probe = new MediaProbe(tools, runner, files, NullLogger<MediaProbe>.Instance);
        var processor = new VideoProcessor(tools, runner, TimeProvider.System, NullLogger<VideoProcessor>.Instance);
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(1L << 30, null, default);
        string input = workspace.File("fixture.mkv");
        await runner.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), ["-o", input, fixture], AllowWarnings: true), default);
        var media = await probe.ProbeAsync(input, default);
        var evidence = await probe.AnalyzeAsync(media, AnalysisMethod.FullRpu, workspace, default);
        Assert.IsNull(evidence.Error, evidence.Error);
        Assert.IsTrue(MediaClassifier.HasVerifiedTail(evidence));
        Assert.IsTrue(ConversionPolicy.Evaluate(MediaClassifier.Classify(media, evidence), true, true).Allowed);
        string output = workspace.File("output.mkv");
        var verifier = new MediaVerifier(probe, processor, tools, runner);
        var dependencies = new DependencyService(new ReadyDetector(tools), null!, null!, tools, NullLogger<DependencyService>.Instance);
        var service = new ConversionService(dependencies, files, factory, processor, verifier, new OutputPublisher(NullLogger<OutputPublisher>.Instance), new BackupArchiveStore(), NullLogger<ConversionService>.Instance);
        var result = await service.ExecuteAsync(new(Guid.NewGuid(), MediaClassifier.Classify(media, evidence), ConversionTarget.Profile81, output, null, workspace.DirectoryPath, ConversionPolicy.RequiredScratchBytes(media.Source.Length), "explicit tail fixture test", Safe: safe), null, default);
        Assert.AreEqual(OperationStatus.Completed, result.Status, result.Message);
        Assert.AreEqual(media.Source, files.Identify(input));
        Assert.IsTrue(File.Exists(output));
    }

    [TestMethod]
    public void PresentationOrderAllowsReorderedEndingButRejectsInternalGaps()
    {
        AccessUnit[] units = [new(1, true, 1), new(0, false, 1), new(1, true, 1), new(0, false, 1)];
        var result = RpuCoverage.Validate(units, [0, 2, 1, 3], 2);
        Assert.AreEqual(2L, result.TailFrames);
        Assert.HasCount(64, result.PositionHash);
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate(units, [0, 1, 2, 3], 2));
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate(units, [2, 0, 1, 3], 2));
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate(units, [0, 2, 1, 1], 2));
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate(units, [0, 2, 1], 2));
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate(units, [0, 2, 1, 3], 3));
    }

    [TestMethod]
    public void RejectsUnverifiableFrameStructures()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate([new(1, true, 1), new(0, true, 1)], [0, 1], 1));
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate([new(2, true, 1), new(0, false, 1)], [0, 1], 2));
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate([new(1, true, 2), new(0, false, 1)], [0, 1], 1));
        Assert.ThrowsExactly<InvalidDataException>(() => RpuCoverage.Validate([new(0, false, 1)], [0], 0));
    }

    [TestMethod]
    public void CoverageFingerprintDetectsMovedMetadataEvenWithEqualCounts()
    {
        var before = RpuCoverage.Validate([new(1, true, 1), new(0, false, 1), new(1, true, 1)], [0, 2, 1], 2);
        var after = RpuCoverage.Validate([new(1, false, 1), new(1, false, 1), new(0, false, 1)], [0, 1, 2], 2);
        Assert.AreEqual(before.RpuFrames, after.RpuFrames);
        Assert.AreNotEqual(before.PositionHash, after.PositionHash);
    }

    [TestMethod]
    public async Task AnnexBReaderHandlesChunkBoundariesAndCancellation()
    {
        byte[] nal(int type, byte payload = 128) => [0, 0, 0, 1, (byte)(type << 1), 1, payload];
        byte[] bytes = [.. nal(35), .. nal(1), .. nal(62), .. nal(63), .. nal(35), .. nal(1)];
        await using var stream = new ShortReads(bytes);
        var units = await RpuCoverage.ReadAsync(stream, default);
        Assert.HasCount(2, units);
        Assert.AreEqual(new AccessUnit(1, true, 1), units[0]);
        Assert.AreEqual(new AccessUnit(0, false, 1), units[1]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RpuCoverage.ReadAsync(new MemoryStream(bytes), cancellation.Token));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RpuCoverage.ReadAsync(new MemoryStream(nal(1)), default));
    }

    [TestMethod]
    public void UnclassifiedFelRequiresVerifiedTailAndExplicitApproval()
    {
        var media = new MediaInfo(new("fixture.mkv", 123, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 10, 24, 1, 0, null, [], 0, 0, null, "{}");
        var evidence = new RpuEvidence(AnalysisMethod.FullRpu, EnhancementLayer.Fel, 8, 1000, 1, 1, MetadataFreeTail: new(10, 8, new string('A', 64)));
        var analysis = MediaClassifier.Classify(media, evidence);
        Assert.AreEqual(AnalysisVerdict.FelUnclassified, analysis.Verdict);
        Assert.IsFalse(ConversionPolicy.Evaluate(analysis, true, false).Allowed);
        Assert.IsTrue(ConversionPolicy.Evaluate(analysis, false, true).Allowed);
        Assert.IsTrue(analysis.Reason.Contains("Verified metadata-free ending"));
        foreach (var invalid in new[]
        {
            evidence with
            {
                MetadataFreeTail = null
            },
            evidence with
            {
                Method = AnalysisMethod.SampledRpu
            },
            evidence with
            {
                Method = AnalysisMethod.DeepInspection
            },
            evidence with
            {
                Error = "failure"
            },
            evidence with
            {
                Frames = 7
            }
        })
        {
            Assert.IsFalse(ConversionPolicy.Evaluate(MediaClassifier.Classify(media, invalid), true, true).Allowed);
        }
    }

    private sealed class ShortReads(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
    }

    private sealed class ReadyDetector(IToolCatalog catalog) : IDependencyDetector
    {
        public Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken, bool skipConfiguredPaths = false) => Task.FromResult(new DependencyReport(tools.Select(t => new DependencyStatus(t, DependencyState.Ready, catalog.GetPath(t), "test", "fixture")).ToArray()));
        public Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
