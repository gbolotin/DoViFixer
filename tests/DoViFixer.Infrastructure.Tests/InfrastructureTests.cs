using System.Formats.Tar;
using System.Text;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Settings;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Archives;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.DoviTool;
using DoViFixer.Infrastructure.MediaTools.Processes;
using DoViFixer.Infrastructure.TemporaryStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class InfrastructureTests
{
    private string directory = null!;
    private readonly FileOperations files = new();

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "DoViFixer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task TemporaryDirectorySettingCreatesMissingParentsAndPreservesExistingContents()
    {
        var service = CreateSettingsService();
        string path = Path.Combine(directory, "missing", "scratch");
        await service.SetTemporaryDirectoryAsync(path, default);
        Assert.IsTrue(Directory.Exists(path));
        Assert.AreEqual(Path.GetFullPath(path), (await service.ReadAsync(default)).TemporaryDirectory);
        Assert.AreEqual(0, Directory.GetFileSystemEntries(path).Length);

        string existing = Path.Combine(path, "keep.txt");
        await File.WriteAllTextAsync(existing, "keep");
        await service.SetTemporaryDirectoryAsync(path, default);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(existing));
        Assert.AreEqual(1, Directory.GetFileSystemEntries(path).Length);
    }

    [TestMethod]
    public async Task InvalidOrCancelledTemporaryDirectorySettingPreservesSavedValue()
    {
        var service = CreateSettingsService();
        await service.SetTemporaryDirectoryAsync(directory, default);
        string file = Path.Combine(directory, "file.txt");
        await File.WriteAllTextAsync(file, "keep");
        await Assert.ThrowsExactlyAsync<IOException>(() => service.SetTemporaryDirectoryAsync(file, default));
        Assert.AreEqual(directory, (await service.ReadAsync(default)).TemporaryDirectory);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(file));

        string cancelled = Path.Combine(directory, "cancelled");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.SetTemporaryDirectoryAsync(cancelled, new CancellationToken(true)));
        Assert.IsFalse(Directory.Exists(cancelled));
        Assert.AreEqual(directory, (await service.ReadAsync(default)).TemporaryDirectory);
    }

    private SettingsService CreateSettingsService()
    {
        var options = new StorageOptions(Path.Combine(directory, "settings"));
        var store = new SettingsStore(options, NullLogger<SettingsStore>.Instance);
        var processes = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var detector = new DependencyDetector(store, options, processes, NullLogger<DependencyDetector>.Instance);
        return new SettingsService(store, detector, new ToolCatalog(), files, NullLogger<SettingsService>.Instance);
    }

    [TestMethod]
    public void UserPathAppendPreservesEntriesAndAvoidsEquivalentDuplicates()
    {
        string app = Path.Combine(directory, "App Folder");
        string original = @"%SystemRoot%\System32;C:\Other";
        Assert.AreEqual(original + ";" + app, UserPathRegistration.AppendDirectory(original, app));
        Assert.AreEqual(app, UserPathRegistration.AppendDirectory(null, app));
        Assert.AreEqual(original + ";" + app, UserPathRegistration.AppendDirectory(original + ";", app));
        string duplicate = original + ";\"" + app.ToUpperInvariant() + "\\\";C:\\Keep";
        Assert.AreEqual(duplicate, UserPathRegistration.AppendDirectory(duplicate, app));
        string variable = @"%SystemRoot%\System32;C:\Keep";
        Assert.AreEqual(variable, UserPathRegistration.AppendDirectory(variable,
            Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "System32")));
    }

    [TestMethod]
    public void UserPathCancellationDoesNotChangeEnvironment()
    {
        var registration = new UserPathRegistration(NullLogger<UserPathRegistration>.Instance);
        string? saved = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        string? process = Environment.GetEnvironmentVariable("Path");
        Assert.ThrowsExactly<OperationCanceledException>(() => registration.AddDirectory(directory, new CancellationToken(true)));
        Assert.AreEqual(saved, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
        Assert.AreEqual(process, Environment.GetEnvironmentVariable("Path"));
    }

    [TestMethod]
    public async Task ArchiveRoundTripsAndLegacyNeedsExplicitChoice()
    {
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var writeWorkspace = await factory.CreateAsync(0, directory, default);
        await File.WriteAllTextAsync(writeWorkspace.File("el.hevc"), "test enhancement payload");
        string hash = await VideoProcessor.HashAsync(writeWorkspace.File("el.hevc"), default);
        var manifest = new ArchiveManifest(1, "movie.mkv", new string('A', 64), hash, new FileInfo(writeWorkspace.File("el.hevc")).Length, 24, DateTimeOffset.UnixEpoch);
        var archive = new BackupArchiveStore();
        string path = Path.Combine(directory, "movie.dovi");
        await archive.WriteAsync(path, manifest, writeWorkspace, default);
        await using var readWorkspace = await factory.CreateAsync(0, directory, default);
        Assert.AreEqual(manifest, await archive.ReadAsync(path, readWorkspace, false, default));
        Assert.AreEqual(hash, await VideoProcessor.HashAsync(readWorkspace.File("el.hevc"), default));

        string legacy = Path.Combine(directory, "legacy.dovi");
        await WriteTarAsync(legacy, ("el.hevc", "payload"));
        await using var legacyWorkspace = await factory.CreateAsync(0, directory, default);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => archive.ReadAsync(legacy, legacyWorkspace, false, default));
        await using var allowedWorkspace = await factory.CreateAsync(0, directory, default);
        Assert.IsNull(await archive.ReadAsync(legacy, allowedWorkspace, true, default));
    }

    [TestMethod]
    [DataRow("../el.hevc")]
    [DataRow("C:/escape.hevc")]
    [DataRow("unexpected.txt")]
    public async Task ArchiveRejectsUnexpectedAndTraversalEntries(string name)
    {
        string archive = Path.Combine(directory, "bad.dovi");
        await WriteTarAsync(archive, (name, "payload"));
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(0, directory, default);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new BackupArchiveStore().ReadAsync(archive, workspace, true, default));
    }

    [TestMethod]
    public async Task ArchiveRejectsHashMismatchAndDuplicatePayload()
    {
        string archive = Path.Combine(directory, "bad.dovi");
        var manifest = new ArchiveManifest(1, "source", new string('A', 64), new string('B', 64), 7, 1, DateTimeOffset.UnixEpoch);
        await WriteTarAsync(archive, ("el.hevc", "payload"), ("manifest.json", JsonSerializer.Serialize(manifest)));
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(0, directory, default);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new BackupArchiveStore().ReadAsync(archive, workspace, false, default));
        string duplicate = Path.Combine(directory, "duplicate.dovi");
        await WriteTarAsync(duplicate, ("el.hevc", "first"), ("el.hevc", "second"));
        await using var another = await factory.CreateAsync(0, directory, default);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new BackupArchiveStore().ReadAsync(duplicate, another, true, default));
    }

    [TestMethod]
    public async Task PublisherRejectsCollisionThatAppearsAfterStaging()
    {
        string output = Path.Combine(directory, "movie.mkv");
        var publisher = new OutputPublisher(NullLogger<OutputPublisher>.Instance);
        await using (var staged = publisher.Stage(output))
        {
            Assert.IsFalse(staged.Path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase));
            await File.WriteAllTextAsync(staged.Path, "new");
            await File.WriteAllTextAsync(output, "existing");
            await Assert.ThrowsExactlyAsync<IOException>(() => staged.PublishAsync(default));
        }
        Assert.AreEqual("existing", await File.ReadAllTextAsync(output));
        Assert.AreEqual(0, Directory.GetDirectories(directory).Length);
    }

    [TestMethod]
    public async Task SuccessfulPublicationAndWorkspaceDisposalCleanUpOwnedFolders()
    {
        string output = Path.Combine(directory, "movie.mkv");
        var publisher = new OutputPublisher(NullLogger<OutputPublisher>.Instance);
        await using (var staged = publisher.Stage(output))
        {
            await File.WriteAllTextAsync(staged.Path, "verified");
            await staged.PublishAsync(default);
        }
        Assert.AreEqual("verified", await File.ReadAllTextAsync(output));
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        string workspacePath;
        await using (var workspace = await factory.CreateAsync(0, directory, default))
        {
            workspacePath = workspace.DirectoryPath;
            Assert.ThrowsExactly<ArgumentException>(() => workspace.File("../escape"));
        }
        Assert.IsFalse(Directory.Exists(workspacePath));
    }

    [TestMethod]
    public async Task SourceLeaseRejectsChangesAndBlocksWriters()
    {
        string source = Path.Combine(directory, "movie.mkv");
        await File.WriteAllTextAsync(source, "original");
        var identity = files.Identify(source);
        await using (await files.AcquireReadLeaseAsync(identity, default))
        {
            await Assert.ThrowsExactlyAsync<IOException>(() => File.WriteAllTextAsync(source, "changed"));
        }
        await File.WriteAllTextAsync(source, "changed length");
        await Assert.ThrowsExactlyAsync<IOException>(async () => await files.AcquireReadLeaseAsync(identity, default));
    }

    [TestMethod]
    public async Task CleanupRevalidatesAndDeletesOnlyTheApprovedBackupHandle()
    {
        string path = Path.Combine(directory, "movie.dovi");
        await File.WriteAllTextAsync(path, "backup");
        var identity = files.Identify(path);
        await File.WriteAllTextAsync(path, "changed backup");
        await Assert.ThrowsExactlyAsync<IOException>(() => files.DeleteAsync(identity, default));
        Assert.IsTrue(File.Exists(path));
        await files.DeleteAsync(files.Identify(path), default);
        Assert.IsFalse(File.Exists(path));
        string media = Path.Combine(directory, "movie.mkv");
        await File.WriteAllTextAsync(media, "original");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => files.DeleteAsync(files.Identify(media), default));
    }

    [TestMethod]
    public async Task ConcurrentSettingsUpdatesDoNotLoseOtherToolPaths()
    {
        var options = new StorageOptions(Path.Combine(directory, "settings"));
        var a = new SettingsStore(options, NullLogger<SettingsStore>.Instance);
        var b = new SettingsStore(options, NullLogger<SettingsStore>.Instance);
        await Task.WhenAll(a.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.SetItem(NativeTool.MkvMerge, "A") }, default),
            b.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.SetItem(NativeTool.DoviTool, "B") }, default));
        var settings = await a.ReadAsync(default);
        Assert.AreEqual(2, settings.ToolPaths.Count);
        Assert.AreEqual("A", settings.ToolPaths[NativeTool.MkvMerge]);
        Assert.AreEqual("B", settings.ToolPaths[NativeTool.DoviTool]);
    }

    [TestMethod]
    public async Task RpuParserUsesLevel1OnlyAndIsInsensitiveToWhitespace()
    {
        const string data = """
            [ { "el_type": "FEL", "other": { "max_pq":4095 }, "vdr_dm_data": {
               "cmv29_metadata": { "ext_metadata_blocks": [ { "Level1": { "max_pq": 2081 } } ] } } } ]
            """;
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
        var evidence = await RpuParser.ParseAsync(stream, AnalysisMethod.FullRpu, default);
        Assert.AreEqual(1L, evidence.Frames);
        Assert.AreEqual(EnhancementLayer.Fel, evidence.Layer);
        Assert.AreEqual(MediaClassifier.PqToNits(2081), evidence.PeakNits);
    }

    [TestMethod]
    [DataRow("fel-rpu.json", EnhancementLayer.Fel)]
    [DataRow("mel-rpu.json", EnhancementLayer.Mel)]
    public async Task PublishedDoviToolRpuFixturesParse(string name, EnhancementLayer expected)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        var result = await RpuParser.ParseAsync(stream, AnalysisMethod.FullRpu, default);
        Assert.AreEqual(expected, result.Layer);
        Assert.AreEqual(1L, result.Frames);
        Assert.IsNotNull(result.PeakNits);
    }

    [TestMethod]
    public async Task InvalidConfiguredToolDoesNotFallBackToInstalledCopy()
    {
        var storage = new StorageOptions(directory);
        var settings = new SettingsStore(storage, NullLogger<SettingsStore>.Instance);
        await settings.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.SetItem(NativeTool.MkvMerge, Path.Combine(directory, "missing.exe")) }, default);
        var runner = new FakeProcessRunner();
        var detector = new DependencyDetector(settings, storage, runner, NullLogger<DependencyDetector>.Instance);
        var report = await detector.DetectAsync(new[] { NativeTool.MkvMerge }, default);
        Assert.AreEqual(DependencyState.Unusable, report.Tools[0].State);
        Assert.AreEqual(0, runner.Calls);
        string installed = Path.Combine(storage.ToolsDirectory, "mkvmerge.exe");
        Directory.CreateDirectory(storage.ToolsDirectory);
        await File.WriteAllTextAsync(installed, "fixture executable");
        runner.Output = "mkvmerge v100.0.0";
        var rediscovered = await detector.DetectAsync(new[] { NativeTool.MkvMerge }, default, skipConfiguredPaths: true);
        Assert.AreEqual(DependencyState.Ready, rediscovered.Tools[0].State);
        Assert.AreEqual(installed, rediscovered.Tools[0].Path);
        Assert.AreEqual(Path.Combine(directory, "missing.exe"), (await settings.ReadAsync(default)).ToolPaths[NativeTool.MkvMerge]);
    }

    [TestMethod]
    public async Task VersionValidationRejectsOldAndGuiTools()
    {
        string console = Path.Combine(directory, "tool.exe");
        await File.WriteAllTextAsync(console, "fake console executable");
        var runner = new FakeProcessRunner { Output = "mkvmerge v79.0.0" };
        var detector = new DependencyDetector(new SettingsStore(new(directory), NullLogger<SettingsStore>.Instance), new(directory), runner, NullLogger<DependencyDetector>.Instance);
        Assert.AreEqual(DependencyState.Incompatible, (await detector.ValidatePathAsync(NativeTool.MkvMerge, console, default)).State);
        runner.Output = "MediaInfo Graphical interface v26.05";
        Assert.AreEqual(DependencyState.Unusable, (await detector.ValidatePathAsync(NativeTool.MediaInfo, console, default)).State);
        runner.Output = "MediaInfo Command line, MediaInfoLib - v26.05";
        Assert.AreEqual(DependencyState.Ready, (await detector.ValidatePathAsync(NativeTool.MediaInfo, console, default)).State);
    }

    [TestMethod]
    public async Task TimestampMismatchAndMissingEvidenceFailVerification()
    {
        string a = Path.Combine(directory, "a.txt");
        string b = Path.Combine(directory, "b.txt");
        await File.WriteAllTextAsync(a, "# timestamp format v2\n0\n41.708333\n");
        await File.WriteAllTextAsync(b, "# timestamp format v2\n0\n41.708\n");
        Assert.IsTrue(await MediaVerifier.TimestampsMatchAsync(a, b, default));
        await File.WriteAllTextAsync(b, "# timestamp format v2\n0\n44\n");
        Assert.IsFalse(await MediaVerifier.TimestampsMatchAsync(a, b, default));
    }

    [TestMethod]
    [DataRow(DependencyState.Unusable)]
    [DataRow(DependencyState.Incompatible)]
    public async Task InstallationPlanIncludesReplacementsWithVerifiedSources(DependencyState state)
    {
        using var http = new HttpClient();
        var installer = new DependencyInstaller(new(directory), new FakeProcessRunner(), http, NullLogger<DependencyInstaller>.Instance);
        var missing = new DependencyReport(new[] { new DependencyStatus(NativeTool.DoviTool, DependencyState.Missing, null, null, "missing") });
        var plan = await installer.PrepareAsync(missing, default);
        Assert.AreEqual(1, plan.Items.Count);
        Assert.AreEqual(InstallationProvider.VerifiedZip, plan.Items[0].Provider);
        Assert.AreEqual(64, plan.Items[0].Sha256!.Length);
        Assert.IsFalse(plan.Items[0].RequiresElevation);
        var invalid = new DependencyReport(new[] { new DependencyStatus(NativeTool.DoviTool, state, "invalid", null, "bad path"),
            new DependencyStatus(NativeTool.MediaInfo, DependencyState.Missing, null, null, "missing"),
            new DependencyStatus(NativeTool.FFmpeg, DependencyState.Ready, "working", "9", "ready") });
        var repair = await installer.PrepareAsync(invalid, default);
        Assert.AreEqual(2, repair.Items.Count);
        Assert.AreEqual(0, repair.Unavailable.Count);
        Assert.AreEqual(invalid.Tools[0], repair.Replacements!.Single());
        Assert.IsFalse(repair.Items.Any(i => i.Tools.Contains(NativeTool.FFmpeg)));
        var modified = plan with { Items = new[] { plan.Items[0] with { Source = "https://example.invalid/unapproved.zip" } } };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => installer.InstallAsync(modified, null, default));
    }

    [TestMethod]
    [DataRow("dvhe.07 / ", DolbyVisionProfile.Profile7)]
    [DataRow("dvhe.07.06", DolbyVisionProfile.Profile7)]
    [DataRow("dvh1.07", DolbyVisionProfile.Profile7)]
    [DataRow("dvhe.05", DolbyVisionProfile.Profile5)]
    [DataRow("dvhe.08", DolbyVisionProfile.Profile81)]
    [DataRow("dvhe.070", DolbyVisionProfile.Unknown)]
    [DataRow("dvhe.07.invalid", DolbyVisionProfile.Unknown)]
    [DataRow("", DolbyVisionProfile.Unknown)]
    public async Task MetadataParserAcceptsSeparateAndCombinedProfileLevels(string profile, DolbyVisionProfile expected)
    {
        string mkv = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mkvmerge.json"));
        string mi = JsonSerializer.Serialize(new
        {
            media = new { track = new[] { new Dictionary<string, string>
            {
                ["@type"] = "Video", ["Format"] = "HEVC",
                ["HDR_Format"] = "Dolby Vision / SMPTE ST 2086",
                ["HDR_Format_Profile"] = profile, ["HDR_Format_Level"] = "06 / ",
                ["HDR_Format_Compatibility"] = "Blu-ray / HDR10"
            } } }
        });
        var result = MediaMetadataParser.Parse(new("fixture.mkv", 1000, DateTime.UnixEpoch), mkv, mi);
        Assert.AreEqual(expected, result.Profile);
    }

    [TestMethod]
    public async Task MetadataParserHandlesProfilesAndRejectsMalformedInputs()
    {
        string mkv = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mkvmerge.json"));
        var identity = new FileIdentity("fixture.mkv", 1000, DateTime.UnixEpoch);
        const string mi = """
            {"media":{"track":[{"@type":"Video","Format":"HEVC","Width":"256","Height":"144",
             "HDR_Format":"Dolby Vision","HDR_Format_Profile":"dvhe.07.06"}]}}
            """;
        var original = MediaMetadataParser.Parse(identity, mkv, mi);
        Assert.AreEqual(DolbyVisionProfile.Profile7, original.Profile);
        Assert.IsNull(original.MaxCll);
        Assert.AreEqual(10.803, original.DurationSeconds);
        Assert.ThrowsExactly<InvalidDataException>(() => MediaMetadataParser.Parse(identity, mkv, "{}"));
        var p8 = mi.Replace("dvhe.07.06", "dvhe.08.06");
        Assert.AreEqual(DolbyVisionProfile.Other, MediaMetadataParser.Parse(identity, mkv, p8).Profile);
        p8 = p8.Replace("\"Format\":", "\"HDR_Format_Compatibility\":\"HDR10\",\"Format\":");
        Assert.AreEqual(DolbyVisionProfile.Profile81, MediaMetadataParser.Parse(identity, mkv, p8).Profile);
    }

    private static async Task WriteTarAsync(string path, params (string Name, string Content)[] entries)
    {
        await using var file = File.Create(path);
        await using var writer = new TarWriter(file);
        foreach (var entry in entries)
        {
            await using var data = new MemoryStream(Encoding.UTF8.GetBytes(entry.Content));
            await writer.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, entry.Name) { DataStream = data });
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public int Calls;
        public string Output { get; set; } = "";
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ProcessResult(0, Output, ""));
        }
    }
}
