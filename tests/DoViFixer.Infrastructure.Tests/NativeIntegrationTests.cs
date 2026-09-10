using DoViFixer.Application.Conversion;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Archives;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using DoViFixer.Infrastructure.TemporaryStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class NativeIntegrationTests
{
    [TestMethod]
    [TestCategory("NativeIntegration")]
    public async Task ExplicitDirectorySupportsHandleBoundBackupDeletion()
    {
        string? directory = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_DELETE_DIRECTORY");
        if (directory is null)
        {
            Assert.Inconclusive("Set DOVIFIXER_TEST_DELETE_DIRECTORY to test deletion of one newly created disposable file.");
        }
        string path = Path.Combine(directory, $".dovifixer-delete-test-{Guid.NewGuid():N}.bak.dovi_convert");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync("DoViFixer disposable deletion test"u8.ToArray());
        }
        try
        {
            var files = new FileOperations();
            await files.DeleteAsync(files.Identify(path), default);
            Assert.IsFalse(File.Exists(path), path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitSourceConvertsAndVerifiesWithoutRenamingOriginal(bool safe)
    {
        string? source = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_SOURCE");
        if (source is null)
        {
            Assert.Inconclusive("Set DOVIFIXER_TEST_SOURCE to opt in to conversion of a read-only source into a temporary workspace.");
        }
        var tools = new ToolCatalog();
        foreach (var tool in DependencyRequirements.All)
        {
            string? path = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_" + tool.ToString().ToUpperInvariant());
            if (path is null || !File.Exists(path))
            {
                Assert.Inconclusive($"Set DOVIFIXER_TEST_{tool.ToString().ToUpperInvariant()} to an installed executable.");
            }
            tools.Refresh([new(tool, DependencyState.Ready, path, "test", "explicit test tool")]);
        }
        var files = new FileOperations();
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var probe = new MediaProbe(tools, runner, files, NullLogger<MediaProbe>.Instance);
        var processor = new VideoProcessor(tools, runner, TimeProvider.System, NullLogger<VideoProcessor>.Instance);
        var media = await probe.ProbeAsync(source, default);
        await using var lease = await files.AcquireReadLeaseAsync(media.Source, default);
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(ConversionPolicy.RequiredScratchBytes(media.Source.Length), null, default);
        string input = workspace.File("copy.mkv");
        File.Copy(source, input);
        var copy = media with { Source = files.Identify(input) };
        var verifier = new MediaVerifier(probe, processor, tools, runner);
        var dependencies = new DependencyService(new ReadyDetector(tools), null!, null!, tools, NullLogger<DependencyService>.Instance);
        var service = new ConversionService(dependencies, files, factory, processor, verifier,
            new OutputPublisher(NullLogger<OutputPublisher>.Instance), new BackupArchiveStore(), NullLogger<ConversionService>.Instance);
        var analysis = MediaClassifier.Classify(copy, await probe.AnalyzeAsync(copy, AnalysisMethod.FullRpu, workspace, default));
        string converted = files.PrepareOutputPath(input, null, " - DV P8.1.mkv");
        var result = await service.ExecuteAsync(new(Guid.NewGuid(), analysis, ConversionTarget.Profile81, converted, null,
            workspace.DirectoryPath, ConversionPolicy.RequiredScratchBytes(copy.Source.Length), "explicit native test", Safe: safe), null, default);
        Assert.AreEqual(OperationStatus.Completed, result.Status, result.Message);
        Assert.IsTrue(File.Exists(input));
        Assert.IsTrue(File.Exists(converted));
        Assert.AreEqual(copy.Source, files.Identify(input));
        Assert.IsFalse(File.Exists(input + ".bak.dovi_convert"));
        Assert.AreEqual(media.Source, files.Identify(source));
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public async Task NativeMetadataRecognizesProfile7Directory()
    {
        string? directory = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_PROFILE7_DIRECTORY");
        string? mediaInfo = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_MEDIAINFO");
        string? mkvMerge = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_MKVMERGE");
        if (directory is null || mediaInfo is null || mkvMerge is null)
        {
            Assert.Inconclusive("Set PROFILE7_DIRECTORY, MEDIAINFO and MKVMERGE with the DOVIFIXER_TEST_ prefix to opt in to metadata-only validation.");
        }
        var catalog = new ToolCatalog();
        catalog.Refresh(new[]
        {
            new DependencyStatus(NativeTool.MediaInfo, DependencyState.Ready, mediaInfo, "test", "explicit test tool"),
            new DependencyStatus(NativeTool.MkvMerge, DependencyState.Ready, mkvMerge, "test", "explicit test tool")
        });
        var probe = new MediaProbe(catalog, new ProcessRunner(NullLogger<ProcessRunner>.Instance),
            new FileOperations(), NullLogger<MediaProbe>.Instance);
        string[] paths = Directory.GetFiles(directory, "*.mkv");
        Assert.IsTrue(paths.Length > 0);
        foreach (string path in paths)
        {
            var media = await probe.ProbeAsync(path, default);
            Assert.AreEqual(DolbyVisionProfile.Profile7, media.Profile, path);
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    public async Task NativeFixtureConvertsBacksUpAndRestoresWithIdenticalBaseAndEnhancementPayloads(bool includeMetadata, bool safe, bool failStreaming)
    {
        var toolPaths = new Dictionary<NativeTool, string>();
        foreach (var tool in new[] { NativeTool.MkvMerge, NativeTool.MkvExtract, NativeTool.DoviTool, NativeTool.FFprobe, NativeTool.FFmpeg })
        {
            string? path = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_" + tool.ToString().ToUpperInvariant());
            if (path is null || !File.Exists(path))
            {
                Assert.Inconclusive($"Set DOVIFIXER_TEST_{tool.ToString().ToUpperInvariant()} to opt in with an existing executable.");
            }
            toolPaths[tool] = path;
        }
        var tools = new ToolCatalog();
        tools.Refresh(toolPaths.Select(p => new DependencyStatus(p.Key, DependencyState.Ready, p.Value, "test", "explicit test tool")));
        tools.Refresh(new[] { new DependencyStatus(NativeTool.MediaInfo, DependencyState.Ready, "fixture-mediainfo", "fixture", "fixture") });
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var files = new FileOperations();
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(16 * 1024 * 1024, null, default);
        string input = workspace.File("source, (é שלום test).mkv");
        string rawFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "regular_start_code_4_muxed_el.hevc");
        string profile7 = workspace.File("profile7.hevc");
        await runner.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "-m", "1", "convert", rawFixture, "-o", profile7 }), default);
        var muxArguments = new List<string> { "-o", input, "--language", "0:eng", "--display-dimensions", "0:512x144",
            "--original-flag", "0:1", "--color-primaries", "0:9", "--color-transfer-characteristics", "0:16", "--max-content-light", "0:1000" };
        if (!includeMetadata)
        {
            // Exercise legacy track language tags without altering chapter/tag XML fixtures.
            muxArguments.Insert(0, "--disable-language-ietf");
        }
        if (includeMetadata)
        {
            string tags = workspace.File("input-video-tags.xml");
            await File.WriteAllTextAsync(tags, "<Tags><Tag><Targets/><Simple><Name>COMMENT</Name><String>Video annotation</String></Simple></Tag></Tags>");
            muxArguments.AddRange(new[] { "--tags", "0:" + tags });
        }
        muxArguments.Add(profile7);
        if (includeMetadata)
        {
            string audio = workspace.File("audio.wav");
            using (var writer = new BinaryWriter(File.Create(audio)))
            {
                const int samples = 48000 * 11;
                writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(samples * 2); writer.Write(new byte[samples * 2]);
            }
            string subtitles = workspace.File("subtitles.srt");
            await File.WriteAllTextAsync(subtitles, "1\n00:00:01,000 --> 00:00:03,000\nFixture subtitle שלום\n");
            string chapters = workspace.File("input-chapters.xml");
            await File.WriteAllTextAsync(chapters, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Chapters><EditionEntry><ChapterAtom><ChapterTimeStart>00:00:00.000</ChapterTimeStart><ChapterDisplay><ChapterString>Test chapter</ChapterString><ChapterLanguage>eng</ChapterLanguage></ChapterDisplay></ChapterAtom></EditionEntry></Chapters>");
            string attachment = workspace.File("attachment.txt");
            await File.WriteAllTextAsync(attachment, "Attachment fixture");
            muxArguments.AddRange(new[] { "--title", "Fixture title", "--chapters", chapters, "--attachment-mime-type", "text/plain", "--attach-file", attachment,
                "--language", "0:en", "--track-name", "0:Test audio", audio, "--language", "0:he", "--track-name", "0:Test subtitles", "--default-track-flag", "0:0", subtitles });
        }
        await runner.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), muxArguments), default);
        var identification = await runner.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), new[] { "-J", input }), default);
        // Synthetic MediaInfo schema for this fixed public fixture; this test exercises native processing, not the MediaInfo executable.
        const string mi = """
            {"media":{"track":[{"@type":"Video","Format":"HEVC","Width":"256","Height":"144","FrameCount":"259",
             "FrameRate":"23.976","Duration":"10.803","HDR_Format":"Dolby Vision","HDR_Format_Profile":"dvhe.07.06","MaxCLL":"1000"}]}}
            """;
        var media = MediaMetadataParser.Parse(files.Identify(input), identification.Output, mi);
        await using var sourceLease = await files.AcquireReadLeaseAsync(media.Source, default);
        var processor = new VideoProcessor(tools, failStreaming ? new FailingPipelineRunner(runner) : runner, TimeProvider.System, NullLogger<VideoProcessor>.Instance);
        var manifest = await processor.ExtractBackupAsync(media, workspace, default);
        string archive = workspace.File("fixture.dovi");
        var archiveStore = new BackupArchiveStore();
        await archiveStore.WriteAsync(archive, manifest, workspace, default);
        string output = workspace.File("converted.partial");
        var progress = new RecordingProgress();
        await processor.ConvertAsync(media, ConversionTarget.Profile81, workspace, output, progress, Guid.NewGuid(), default, true);
        foreach (string stage in safe || failStreaming ? new[] { "Extracting video", "Remuxing" } : new[] { "Remuxing" })
        {
            Assert.IsTrue(progress.Values.Any(p => p.Stage == stage && p.Percent is >= 0 and < 100), stage + " native progress");
            Assert.IsTrue(progress.Values.Any(p => p.Stage == stage && p.Percent == 100), stage + " completion");
        }
        Assert.IsTrue(new FileInfo(output).Length > 0);
        var convertedIdentification = await runner.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), new[] { "-J", output }), default);
        using (var a = JsonDocument.Parse(identification.Output))
        using (var b = JsonDocument.Parse(convertedIdentification.Output))
        {
            var changed = MkvVideoMetadata.Differences(a.RootElement.GetProperty("tracks")[0].GetProperty("properties"), b.RootElement.GetProperty("tracks")[0].GetProperty("properties")).ToArray();
            Assert.AreEqual(0, changed.Length, string.Join(", ", changed.Select(p => p + ": " + a.RootElement.GetProperty("tracks")[0].GetProperty("properties").GetProperty(p) + " -> " + MediaMetadataParser.Text(b.RootElement.GetProperty("tracks")[0].GetProperty("properties"), p))));
        }

        await using var restorationWorkspace = await factory.CreateAsync(16 * 1024 * 1024, null, default);
        var readManifest = await archiveStore.ReadAsync(archive, restorationWorkspace, false, default);
        var convertedMedia = MediaMetadataParser.Parse(files.Identify(output), convertedIdentification.Output, mi) with { Profile = DolbyVisionProfile.Profile81 };
        string beforeTimestamps = restorationWorkspace.File("before.txt");
        string afterTimestamps = restorationWorkspace.File("after.txt");
        await runner.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { input, "timestamps_v2", "0:" + beforeTimestamps }), default);
        await runner.RunAsync(new(tools.GetPath(NativeTool.MkvExtract), new[] { output, "timestamps_v2", "0:" + afterTimestamps }), default);
        Assert.IsTrue(await MediaVerifier.TimestampsMatchAsync(beforeTimestamps, afterTimestamps, default));
        string restored = restorationWorkspace.File("restored.partial");
        await processor.RestoreAsync(convertedMedia, readManifest, restorationWorkspace, restored, default);
        Assert.IsTrue(new FileInfo(restored).Length > 0);
        var restoredMedia = media with { Source = files.Identify(restored) };
        await using var verificationWorkspace = await factory.CreateAsync(16 * 1024 * 1024, null, default);
        var restoredManifest = await processor.ExtractBackupAsync(restoredMedia, verificationWorkspace, default);
        Assert.AreEqual(manifest.BaseLayerSha256, restoredManifest.BaseLayerSha256);
        Assert.AreEqual(manifest.EnhancementLayerSha256, restoredManifest.EnhancementLayerSha256);

        // Verify the produced RPU bitstream independently of the requested command mode.
        string rpu = verificationWorkspace.File("converted.rpu");
        string convertedRaw = verificationWorkspace.File("converted.hevc");
        await processor.ExtractVideoAsync(convertedMedia, convertedRaw, default);
        await runner.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "extract-rpu", convertedRaw, "-o", rpu }), default);
        await runner.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "export", "-i", "converted.rpu", "-d", "all=converted.json" }, verificationWorkspace.DirectoryPath), default);
        await using var json = File.OpenRead(verificationWorkspace.File("converted.json"));
        using var parsed = await JsonDocument.ParseAsync(json);
        Assert.AreEqual(259, parsed.RootElement.GetArrayLength());
        Assert.IsTrue(parsed.RootElement.EnumerateArray().All(r => r.GetProperty("dovi_profile").GetInt32() == 8));

        await using var hdrWorkspace = await factory.CreateAsync(16 * 1024 * 1024, null, default);
        string hdrOutput = hdrWorkspace.File("hdr10.partial");
        await processor.ConvertAsync(media, ConversionTarget.Hdr10, hdrWorkspace, hdrOutput, null, Guid.NewGuid(), default, true);
        string hdrRaw = hdrWorkspace.File("hdr10.hevc");
        string hdrClean = hdrWorkspace.File("hdr10-clean.hevc");
        await processor.ExtractVideoAsync(media with { Source = files.Identify(hdrOutput) }, hdrRaw, default);
        await processor.RunDoviAsync(new[] { "remove", hdrRaw, "-o", hdrClean }, default);
        Assert.AreEqual(manifest.BaseLayerSha256, await VideoProcessor.HashAsync(hdrClean, default));
        var noRpu = await runner.RunAsync(new(tools.GetPath(NativeTool.DoviTool), new[] { "extract-rpu", hdrRaw, "-o", hdrWorkspace.File("absent.rpu") }, AcceptedExitCodes: new[] { 0, 1 }), default);
        Assert.AreEqual(1, noRpu.ExitCode);
        Assert.AreEqual("Error: No RPU was found in input file", noRpu.Error.Trim());

        // MediaInfo is a controlled output fixture here; all processing, ffprobe counting,
        // timestamp extraction and payload verification execute real native tools.
        var mediaInfoFixtures = new Dictionary<string, string>
        {
            [input] = mi,
            [output] = mi.Replace("dvhe.07.06", "dvhe.08.06").Replace("\"MaxCLL\"", "\"HDR_Format_Compatibility\":\"HDR10\",\"MaxCLL\""),
            [restored] = mi,
            [hdrOutput] = mi.Replace("Dolby Vision", "SMPTE ST 2086").Replace("dvhe.07.06", "")
        };
        var metadataRunner = new FixtureMediaInfoRunner(runner, mediaInfoFixtures);
        var probe = new MediaProbe(tools, metadataRunner, files, NullLogger<MediaProbe>.Instance);
        var verifier = new MediaVerifier(probe, processor, tools, metadataRunner);
        foreach (var (targetPath, targetProfile, sourceMedia) in new[]
        {
            (output, DolbyVisionProfile.Profile81, media),
            (restored, DolbyVisionProfile.Profile7, convertedMedia),
            (hdrOutput, DolbyVisionProfile.None, media)
        })
        {
            await using var checkWorkspace = await factory.CreateAsync(16 * 1024 * 1024, null, default);
            var failures = await verifier.VerifyAsync(sourceMedia, targetPath, targetProfile, checkWorkspace, default);
            Assert.AreEqual(0, failures.Count, string.Join("; ", failures));
        }
        // Exercise the complete conversion workflow, including verification fallback,
        // original naming, archive retention, and deletion after publication.
        foreach (var target in new[] { ConversionTarget.Profile81, ConversionTarget.Hdr10 })
        {
            string serviceInput = workspace.File("service-" + target + ".mkv");
            File.Copy(input, serviceInput);
            string targetJson = target == ConversionTarget.Profile81 ? mediaInfoFixtures[output] : mediaInfoFixtures[hdrOutput];
            var serviceRunner = new FixtureMediaInfoRunner(runner, mediaInfoFixtures, targetJson);
            var serviceProbe = new MediaProbe(tools, serviceRunner, files, NullLogger<MediaProbe>.Instance);
            var serviceProcessor = new VideoProcessor(tools, failStreaming ? new FailingPipelineRunner(runner) : runner, TimeProvider.System, NullLogger<VideoProcessor>.Instance);
            var serviceVerifier = new MediaVerifier(serviceProbe, serviceProcessor, tools, serviceRunner);
            var dependencies = new DependencyService(new ReadyDetector(tools), null!, null!, tools, NullLogger<DependencyService>.Instance);
            var service = new ConversionService(dependencies, files, factory, serviceProcessor, serviceVerifier,
                new OutputPublisher(NullLogger<OutputPublisher>.Instance), archiveStore, NullLogger<ConversionService>.Instance);
            var sourceMedia = media with { Source = files.Identify(serviceInput) };
            var analysis = MediaClassifier.Classify(sourceMedia, new(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 259, null, 1, 1));
            string serviceArchive = Path.ChangeExtension(serviceInput, ".dovi");
            string serviceOutput = safe ? files.PrepareOutputPath(serviceInput, null,
                target == ConversionTarget.Profile81 ? " - DV P8.1.mkv" : " - HDR10.mkv") : serviceInput;
            var result = await service.ExecuteAsync(new(Guid.NewGuid(), analysis, target, serviceOutput, serviceArchive,
                workspace.DirectoryPath, 16 * 1024 * 1024, "fixture", Safe: safe, DeleteBackup: !safe), null, default);
            Assert.AreEqual(OperationStatus.Completed, result.Status, result.Message);
            Assert.IsTrue(File.Exists(serviceInput));
            Assert.IsTrue(File.Exists(serviceArchive));
            Assert.IsTrue(File.Exists(serviceOutput));
            Assert.IsFalse(File.Exists(serviceInput + ".bak.dovi_convert"));
            if (safe)
            {
                Assert.AreEqual(sourceMedia.Source, files.Identify(serviceInput));
                CollectionAssert.AreEqual(await File.ReadAllBytesAsync(input), await File.ReadAllBytesAsync(serviceInput));
            }
        }
    }

    private sealed class ReadyDetector(IToolCatalog catalog) : IDependencyDetector
    {
        public Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken, bool skipConfiguredPaths = false) =>
            Task.FromResult(new DependencyReport(tools.Select(t => new DependencyStatus(t, DependencyState.Ready, catalog.GetPath(t), "test", "fixture")).ToArray()));
        public Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FailingPipelineRunner(IProcessRunner native) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken) => native.RunAsync(request, cancellationToken);
        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => throw new IOException("Simulated streaming failure");
    }

    private sealed class FixtureMediaInfoRunner(IProcessRunner native, IReadOnlyDictionary<string, string> fixtures, string? defaultJson = null) : IProcessRunner
    {
        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => native.PipeAsync(producer, consumer, cancellationToken);
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken) => request.Executable == "fixture-mediainfo"
            ? Task.FromResult(new ProcessResult(0, fixtures.TryGetValue(request.Arguments[^1], out var json) ? json : defaultJson ?? throw new InvalidOperationException("Missing MediaInfo fixture"), "")) : native.RunAsync(request, cancellationToken);
    }
    private sealed class RecordingProgress : IProgress<DoViFixer.Application.Operations.OperationProgress>
    {
        public List<DoViFixer.Application.Operations.OperationProgress> Values { get; } = [];
        public void Report(DoViFixer.Application.Operations.OperationProgress value) => Values.Add(value);
    }
}
