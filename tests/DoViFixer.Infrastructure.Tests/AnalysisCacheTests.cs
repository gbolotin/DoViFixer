using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;
[TestClass]
public sealed class AnalysisCacheTests
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-cache-tests", Guid.NewGuid().ToString("N"));
    private AnalysisCache Cache() => new(new StorageOptions(directory), NullLogger<AnalysisCache>.Instance);
    private MediaAnalysis Analysis() => MediaClassifier.Classify(new MediaInfo(new(Path.Combine(directory, "Movie.mkv"), 1234, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 24, 1, 0, 1000, [], 0, 0, null, "{}"), new(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 24, null, 1, 1));
    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task BaseLayerHashPersistsInvalidatesAndIsIncludedInCacheCleanup()
    {
        var source = Analysis().Media.Source;
        string hash = new string('A', 64);
        await Cache().WriteBaseLayerHashAsync(source, hash, default);
        var cache = Cache();
        Assert.AreEqual(hash, await cache.ReadBaseLayerHashAsync(source, default));
        Assert.IsNull(await cache.ReadBaseLayerHashAsync(source with { Length = source.Length + 1 }, default));
        Assert.IsNull(await cache.ReadBaseLayerHashAsync(source with { LastWriteUtc = source.LastWriteUtc.AddSeconds(1) }, default));
        Assert.IsNull(await cache.ReadBaseLayerHashAsync(source with { Path = source.Path + ".renamed" }, default));
        Assert.IsTrue(await cache.GetSizeAsync(default) > 0);
        Assert.AreEqual(1, await cache.ClearAsync(default));
        Assert.IsNull(await cache.ReadBaseLayerHashAsync(source, default));
        Assert.AreEqual(0L, await cache.GetSizeAsync(default));
    }

    [TestMethod]
    public async Task ConversionResultPersistsForTheUnchangedSourceAndIsIncludedInCacheCleanup()
    {
        var source = Analysis().Media.Source;
        var result = new OperationItemResult(source.Path, OperationStatus.Completed, Path.Combine(directory, "Movie - DV P8.1.mkv"), "Verified output published. Original retained.")
        {
            Original = source.Path, Archive = Path.Combine(directory, "Movie.dovi")
        };
        await Cache().WriteConversionResultAsync(source, result, default);
        var cache = Cache();
        Assert.AreEqual(result, await cache.ReadConversionResultAsync(source, default), "A later run reads the stored result.");
        Assert.IsNull(await cache.ReadConversionResultAsync(source with { Length = source.Length + 1 }, default));
        Assert.IsNull(await cache.ReadConversionResultAsync(source with { LastWriteUtc = source.LastWriteUtc.AddSeconds(1) }, default));
        Assert.IsNull(await cache.ReadAsync(source, AnalysisMethod.FullRpu, default), "The result does not replace analysis entries.");
        Assert.AreEqual(1, await cache.ClearAsync(default));
        Assert.IsNull(await cache.ReadConversionResultAsync(source, default));
    }

    [TestMethod]
    public async Task InvalidHashCacheEntriesAreIgnoredAndUnwritableCacheIsHarmless()
    {
        var source = Analysis().Media.Source;
        string hash = new string('A', 64);
        var cache = Cache();
        await cache.WriteBaseLayerHashAsync(source, hash, default);
        string path = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Single();
        string json = await File.ReadAllTextAsync(path);
        foreach (string invalid in new[] { "{", "null", "{}", json.Replace(hash, "bad"), json.Replace("\"Version\":1", "\"Version\":0"), json.Replace("\"Source\":{", "\"Source\":null,\"Unused\":{") })
        {
            await File.WriteAllTextAsync(path, invalid);
            Assert.IsNull(await cache.ReadBaseLayerHashAsync(source, default));
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cache.WriteBaseLayerHashAsync(source, hash, cancelled.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cache.ReadBaseLayerHashAsync(source, cancelled.Token));
        string blocked = Path.Combine(directory, "blocked");
        await File.WriteAllTextAsync(blocked, "not a directory");
        var unavailable = new AnalysisCache(new StorageOptions(blocked), NullLogger<AnalysisCache>.Instance);
        await unavailable.WriteBaseLayerHashAsync(source, hash, default);
        Assert.IsNull(await unavailable.ReadBaseLayerHashAsync(source, default));
    }

    [TestMethod]
    public async Task VerifiedTailAndUnclassifiedFelPersistWithTheirEvidence()
    {
        var original = Analysis();
        var media = original.Media with
        {
            MaxCll = null
        };
        var evidence = original.Evidence with
        {
            Layer = EnhancementLayer.Fel,
            MetadataFreeTail = new(30, original.Evidence.Frames, new string('A', 64))
        };
        var analysis = MediaClassifier.Classify(media, evidence);
        Assert.AreEqual(AnalysisVerdict.FelUnclassified, analysis.Verdict);
        await Cache().WriteAsync(analysis, default);
        var restored = await Cache().ReadAsync(media.Source, AnalysisMethod.FullRpu, default);
        Assert.IsNotNull(restored);
        Assert.AreEqual(evidence.MetadataFreeTail, restored.Evidence.MetadataFreeTail);
        Assert.AreEqual(analysis.Reason, restored.Reason);
    }

    [TestMethod]
    public async Task ClearRemovesOnlyAnalysisEntriesAndAllowsFreshResults()
    {
        var cache = Cache();
        Assert.AreEqual(0, await cache.ClearAsync(default));
        Assert.AreEqual(Path.Combine(directory, "cache"), cache.RootDirectory);
        Assert.AreEqual(0L, await cache.GetSizeAsync(default));
        var analysis = Analysis();
        await cache.WriteAsync(analysis, default);
        string settings = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(settings, "{}");
        string temporary = Path.Combine(directory, "cache", "analysis", "active.tmp");
        await File.WriteAllTextAsync(temporary, "in progress");
        long expectedBytes = new FileInfo(Directory.GetFiles(Path.GetDirectoryName(temporary)!, "*.json").Single()).Length;
        Assert.AreEqual(expectedBytes, await cache.GetSizeAsync(default));
        Assert.AreEqual(1, await cache.ClearAsync(default));
        Assert.AreEqual(0L, await cache.GetSizeAsync(default));
        Assert.IsTrue(File.Exists(settings));
        Assert.IsTrue(File.Exists(temporary));
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, default));
        await cache.WriteAsync(analysis, default);
        Assert.IsNotNull(await cache.ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, default));
    }

    [TestMethod]
    public void OlderSettingsEnableAutomaticScanningAndCacheReuse()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<DoViFixer.Application.Settings.UserSettings>("{}");
        Assert.IsTrue(settings!.AutomaticallyScanAddedFiles);
        Assert.IsTrue(settings.UseCachedResults);
    }

    [TestMethod]
    public async Task PersistsAcrossInstancesAndInvalidatesChangedIdentityAndMethod()
    {
        var analysis = Analysis();
        await Cache().WriteAsync(analysis, default);
        var cache = Cache();
        var result = await cache.ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, default);
        Assert.IsNotNull(result);
        Assert.AreEqual(analysis.Evidence, result.Evidence);
        Assert.AreEqual(analysis.Media.Source, result.Media.Source);
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source with
        {
            Length = 1235
        }, AnalysisMethod.FullRpu, default));
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source with
        {
            LastWriteUtc = DateTime.UtcNow
        }, AnalysisMethod.FullRpu, default));
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source with
        {
            Path = Path.Combine(directory, "Other.mkv")
        }, AnalysisMethod.FullRpu, default));
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source, AnalysisMethod.DeepInspection, default));
    }

    [TestMethod]
    public async Task CorruptAndObsoleteEntriesAreMissesAndCanBeReplaced()
    {
        var analysis = Analysis();
        var cache = Cache();
        await cache.WriteAsync(analysis, default);
        string path = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Single();
        string json = await File.ReadAllTextAsync(path);
        foreach (string invalid in new[]
        {
            "{",
            "null",
            "{}",
            json.Replace("\"Version\":1", "\"Version\":0"),
            json.Replace("\"Evidence\":{", "\"Evidence\":null,\"Unused\":{")
        }

        )
        {
            await File.WriteAllTextAsync(path, invalid);
            Assert.IsNull(await cache.ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, default));
        }

        await cache.WriteAsync(analysis, default);
        Assert.IsNotNull(await cache.ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, default));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task FailedIncompleteAndCancelledResultsAreNotStored()
    {
        var analysis = Analysis();
        var cache = Cache();
        await cache.WriteAsync(analysis with
        {
            Evidence = analysis.Evidence with
            {
                Error = "failed"
            }
        }, default);
        await cache.WriteAsync(analysis with
        {
            Evidence = analysis.Evidence with
            {
                Frames = 0
            }
        }, default);
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cache.WriteAsync(analysis, cancelled.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cache.ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, cancelled.Token));
    }

    [TestMethod]
    public async Task UnwritableCacheDoesNotFailAnalysis()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "cache"), "blocks directory creation");
        var analysis = Analysis();
        await Cache().WriteAsync(analysis, default);
        Assert.IsNull(await Cache().ReadAsync(analysis.Media.Source, AnalysisMethod.FullRpu, default));
    }
}
