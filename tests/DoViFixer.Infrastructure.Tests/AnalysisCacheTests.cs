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
    private MediaAnalysis Analysis() => MediaClassifier.Classify(new MediaInfo(
        new(Path.Combine(directory, "Movie.mkv"), 1234, DateTime.UnixEpoch), DolbyVisionProfile.Profile7,
        "HEVC", 0, 1920, 1080, 24, 24, 1, 0, 1000, [], 0, 0, null, "{}"),
        new(AnalysisMethod.FullRpu, EnhancementLayer.Mel, 24, null, 1, 1));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
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
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source with { Length = 1235 }, AnalysisMethod.FullRpu, default));
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source with { LastWriteUtc = DateTime.UtcNow }, AnalysisMethod.FullRpu, default));
        Assert.IsNull(await cache.ReadAsync(analysis.Media.Source with { Path = Path.Combine(directory, "Other.mkv") }, AnalysisMethod.FullRpu, default));
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
        foreach (string invalid in new[] { "{", "null", "{}", json.Replace("\"Version\":1", "\"Version\":0"), json.Replace("\"Evidence\":{", "\"Evidence\":null,\"Unused\":{") })
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
        await cache.WriteAsync(analysis with { Evidence = analysis.Evidence with { Error = "failed" } }, default);
        await cache.WriteAsync(analysis with { Evidence = analysis.Evidence with { Frames = 0 } }, default);
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
