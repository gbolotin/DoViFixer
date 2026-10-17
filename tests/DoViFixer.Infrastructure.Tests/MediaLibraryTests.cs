using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Library;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class MediaLibraryTests
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-library-" + Guid.NewGuid().ToString("N"));
    private StorageOptions Options => new(directory);
    private ISettingsStore Settings => new SettingsStore(Options, NullLogger<SettingsStore>.Instance);
    private MediaLibrary Library() => new(Options, Settings, TimeProvider.System, NullLogger<MediaLibrary>.Instance, NullLogger<AnalysisCache>.Instance);
    private FileIdentity Identify(string path) => new(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path));

    private async Task<FileIdentity> CreateAsync(string name, byte content)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        await File.WriteAllBytesAsync(path, Enumerable.Repeat(content, 1024).ToArray());
        return Identify(path);
    }

    private MediaAnalysis Analysis(FileIdentity source, AnalysisMethod method = AnalysisMethod.FullRpu) => MediaClassifier.Classify(
        new(source, DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 24, 1, 0, 1000, [], 0, 0, null, "{}"),
        new(method, EnhancementLayer.Mel, 24, null, 1, 1, Options: new UserSettings().AnalysisOptions));

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task AnalysisAndHistorySurviveRestartAndFollowRenamedContent()
    {
        var source = await CreateAsync("Before.mkv", 1);
        using var library = Library();
        await library.WriteAsync(Analysis(source), default);
        var original = (await library.ReadAllAsync(default)).Single();
        string renamed = Path.Combine(directory, "After.mkv");
        File.Move(source.Path, renamed);
        using var restarted = Library();
        var remembered = await restarted.RememberAsync(Identify(renamed), default);
        Assert.AreEqual(original.Fingerprint, remembered.Fingerprint);
        Assert.AreEqual(renamed, remembered.Analysis!.Media.Source.Path);
        Assert.AreEqual(AnalysisMethod.FullRpu, (await restarted.ReadAsync(Identify(renamed), AnalysisMethod.SampledRpu, default))!.Evidence.Method);
        var history = await restarted.ReadHistoryAsync(original.Fingerprint, default);
        CollectionAssert.AreEqual(new[] { "Added", "FullRpu", "Moved or renamed" }, history.Select(item => item.Action).ToArray());
        Assert.IsGreaterThan(0L, await restarted.GetSizeAsync(default));
        Assert.HasCount(1, await restarted.ReadAllAsync(default));
    }

    [TestMethod]
    public async Task FullInspectionIsNotDowngradedByAScanAndChangedOptionsDoNotAuthorizeReuse()
    {
        var source = await CreateAsync("Movie.mkv", 2);
        using var library = Library();
        await library.WriteAsync(Analysis(source), default);
        await library.WriteAsync(Analysis(source, AnalysisMethod.SampledRpu), default);
        Assert.AreEqual(AnalysisMethod.FullRpu, (await library.ReadAsync(source, AnalysisMethod.SampledRpu, default))!.Evidence.Method);
        await Settings.UpdateAsync(value => value with { BrightnessMarginNits = 75 }, default);
        Assert.IsNull(await library.ReadAsync(source, AnalysisMethod.FullRpu, default));
        Assert.IsNull((await library.ReadAllAsync(default)).Single().Analysis);
        Assert.HasCount(3, await library.ReadHistoryAsync((await library.RememberAsync(source, default)).Fingerprint, default));
    }

    [TestMethod]
    public async Task ReplacingContentAtTheSamePathDoesNotReuseOldAnalysis()
    {
        var source = await CreateAsync("Movie.mkv", 3);
        using var library = Library();
        await library.WriteAsync(Analysis(source), default);
        await File.WriteAllBytesAsync(source.Path, [5, 6, 7]);
        var current = Identify(source.Path);
        Assert.IsNull(await library.ReadAsync(current, AnalysisMethod.FullRpu, default));
        await library.RememberAsync(current, default);
        var row = (await library.ReadAllAsync(default)).Single();
        Assert.IsNull(row.Analysis);
        Assert.AreEqual(current, row.Source);
    }

    [TestMethod]
    public async Task CleaningRemovesOnlyMissingRecordsAndCascadesTheirHistory()
    {
        var present = await CreateAsync("Present.mkv", 4);
        var missing = await CreateAsync("Missing.mkv", 5);
        using var library = Library();
        await library.WriteAsync(Analysis(present), default);
        await library.WriteAsync(Analysis(missing), default);
        string missingKey = (await library.FindAsync(missing, default))!.Fingerprint;
        string preview = Path.Combine(directory, "preview.png");
        await File.WriteAllBytesAsync(preview, [9]);
        File.Delete(missing.Path);
        Assert.AreEqual(1, await library.CleanMissingAsync(default));
        Assert.AreEqual(present.Path, (await library.ReadAllAsync(default)).Single().Source.Path);
        Assert.HasCount(0, await library.ReadHistoryAsync(missingKey, default));
        Assert.IsNotNull(await library.ReadAsync(present, AnalysisMethod.FullRpu, default));
        CollectionAssert.AreEqual(new byte[] { 9 }, await File.ReadAllBytesAsync(preview));
        Assert.IsTrue(File.Exists(present.Path));
        Assert.AreEqual(0, await library.CleanMissingAsync(default));
        string renamed = Path.Combine(directory, "Still-present.mkv");
        File.Move(present.Path, renamed);
        var service = new MediaLibraryService(library, new FileOperations(), new FileOperations(), NullLogger<MediaLibraryService>.Instance);
        Assert.AreEqual(0, await service.CleanMissingAsync(default), "Cleaning must recover a rename before pruning missing paths.");
        Assert.AreEqual(renamed, (await library.ReadAllAsync(default)).Single().Source.Path);
    }

    [TestMethod]
    public async Task StartupRecoversSameFolderRenameAndKeepsUnlocatedFiles()
    {
        var source = await CreateAsync("Before.mkv", 6);
        var missing = await CreateAsync("Missing.mkv", 7);
        using var library = Library();
        await library.WriteAsync(Analysis(source), default);
        await library.WriteAsync(Analysis(missing), default);
        string renamed = Path.Combine(directory, "After.mkv");
        File.Move(source.Path, renamed);
        File.Delete(missing.Path);
        var service = new MediaLibraryService(library, new FileOperations(), new FileOperations(), NullLogger<MediaLibraryService>.Instance);
        var result = await service.LoadAsync(default);
        Assert.HasCount(2, result);
        Assert.IsTrue(result.Any(entry => entry.Source.Path == renamed && entry.Analysis is not null));
        Assert.IsTrue(result.Any(entry => entry.Source.Path == missing.Path));
    }

    [TestMethod]
    public async Task LegacyAnalysisIsImportedWithoutRemovingTheOriginalCache()
    {
        var source = await CreateAsync("Legacy.mkv", 8);
        var legacy = new AnalysisCache(Options, NullLogger<AnalysisCache>.Instance, Settings);
        await legacy.WriteAsync(Analysis(source), default);
        using var library = Library();
        Assert.IsNotNull((await library.ReadAllAsync(default)).Single().Analysis);
        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(Options.CacheDirectory, "analysis"), "*.json").Any());
        Assert.HasCount(1, await library.ReadAllAsync(default));
    }

    [TestMethod]
    public async Task HashAndConversionResultFollowMovedSourceAndWritesRemainIndependent()
    {
        var source = await CreateAsync("Source.mkv", 9);
        using var library = Library();
        await library.WriteBaseLayerHashAsync(source, new string('A', 64), default);
        var result = new OperationItemResult(source.Path, OperationStatus.Completed, Path.Combine(directory, "Output.mkv"));
        await library.WriteConversionResultAsync(source, result, default);
        string renamed = Path.Combine(directory, "Renamed.mkv");
        File.Move(source.Path, renamed);
        Assert.AreEqual(new string('A', 64), await library.ReadBaseLayerHashAsync(Identify(renamed), default));
        var read = await library.ReadConversionResultAsync(Identify(renamed), default);
        Assert.AreEqual(renamed, read!.Item);
        Assert.AreEqual(result.Output, read.Output);
    }

    [TestMethod]
    public async Task ConcurrentWritersAndCancelledCleanupDoNotLoseRecords()
    {
        var sources = new List<FileIdentity>();
        for (int index = 0; index < 8; index++)
        {
            sources.Add(await CreateAsync($"{index}.mkv", (byte)(index + 10)));
        }
        using var library = Library();
        await library.GetSizeAsync(default);
        await Task.WhenAll(sources.Select(source => Task.Run(() => library.WriteAsync(Analysis(source), default))));
        Assert.HasCount(8, await library.ReadAllAsync(default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => library.CleanMissingAsync(cancellation.Token));
        Assert.HasCount(8, await library.ReadAllAsync(default));
    }

    [TestMethod]
    public async Task NewerSchemaIsRejectedWithoutOverwritingTheDatabase()
    {
        using var library = Library();
        await library.GetSizeAsync(default);
        await using (var connection = new SqliteConnection($"Data Source={library.FilePath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=999";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => library.ReadAllAsync(default));
    }

    [TestMethod]
    public async Task CorruptSavedEvidenceAndHashesAreNotReused()
    {
        var source = await CreateAsync("Movie.mkv", 42);
        using var library = Library();
        await library.WriteAsync(Analysis(source), default);
        await using (var connection = new SqliteConnection($"Data Source={library.FilePath}"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE analyses SET json='{'; UPDATE media SET base_hash='invalid',conversion_json='{';";
            await command.ExecuteNonQueryAsync();
        }
        Assert.IsNull(await library.ReadAsync(source, AnalysisMethod.FullRpu, default));
        Assert.IsNull(await library.ReadBaseLayerHashAsync(source, default));
        Assert.IsNull(await library.ReadConversionResultAsync(source, default));
        Assert.HasCount(1, await library.ReadAllAsync(default), "A damaged result must not erase the collection record.");
    }
}
