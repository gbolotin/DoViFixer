using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class PerformanceSettingsTests
{
    [TestMethod]
    [DataRow(1, 1, 1)]
    [DataRow(4, 1, 3)]
    [DataRow(16, 4, 10)]
    [DataRow(128, 8, 10)]
    [DataRow(0, 1, 1)]
    public void RecommendationsUsePhysicalCoresAndUsefulLimits(int cores, int conversions, int samples)
    {
        var hardware = new HardwareProfile(cores, Math.Max(1, cores * 2), true);
        Assert.AreEqual(conversions, hardware.ConversionWorkers(0));
        Assert.AreEqual(samples, hardware.AnalysisWorkers(0));
        Assert.AreEqual(16, hardware.ConversionWorkers(16));
        Assert.AreEqual(10, hardware.AnalysisWorkers(32));
    }

    [TestMethod]
    public void HardwareDetectionReturnsAStableUsableProfile()
    {
        var provider = new HardwareInfoProvider();
        var first = provider.Profile;
        Assert.AreSame(first, provider.Profile);
        Assert.IsGreaterThanOrEqualTo(1, provider.Profile.PhysicalCores);
        Assert.IsGreaterThanOrEqualTo(provider.Profile.PhysicalCores, provider.Profile.LogicalProcessors);
    }

    [TestMethod]
    public async Task PerformanceValuesRoundTripAndInvalidChangesPreserveSavedSettings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-performance-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new StorageOptions(directory);
            var store = new SettingsStore(options, NullLogger<SettingsStore>.Instance);
            var files = new FileOperations();
            var catalog = new ToolCatalog();
            var detector = new DependencyDetector(store, options, new MediaTools.Processes.ProcessRunner(NullLogger<MediaTools.Processes.ProcessRunner>.Instance), NullLogger<DependencyDetector>.Instance);
            var service = new SettingsService(store, detector, catalog, files, NullLogger<SettingsService>.Instance);
            await service.SetPreferencesAsync(new PreferenceChanges { FolderSearchDepth = 5, ConversionWorkers = 16, AnalysisWorkers = 10, ToolProcessPriority = ToolProcessPriority.AboveNormal, PipeBufferKiB = 65536 }, default);
            var saved = await store.ReadAsync(default);
            Assert.AreEqual(5, saved.FolderSearchDepth);
            Assert.AreEqual(16, saved.ConversionWorkers);
            Assert.AreEqual(10, saved.AnalysisWorkers);
            Assert.AreEqual(ToolProcessPriority.AboveNormal, saved.ToolProcessPriority);
            Assert.AreEqual(65536, saved.PipeBufferKiB);
            string before = await File.ReadAllTextAsync(options.SettingsPath);
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.SetPreferencesAsync(new PreferenceChanges { FolderSearchDepth = -1 }, default));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.SetPreferencesAsync(new PreferenceChanges { FolderSearchDepth = 101 }, default));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.SetPreferencesAsync(new PreferenceChanges { ConversionWorkers = 17 }, default));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.SetPreferencesAsync(new PreferenceChanges { AnalysisWorkers = 11 }, default));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.SetPreferencesAsync(new PreferenceChanges { PipeBufferKiB = 63 }, default));
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => service.SetPreferencesAsync(new PreferenceChanges { ToolProcessPriority = (ToolProcessPriority)99 }, default));
            Assert.AreEqual(before, await File.ReadAllTextAsync(options.SettingsPath));
            await File.WriteAllTextAsync(options.SettingsPath, "{\"ConversionWorkers\":2}");
            var legacy = await store.ReadAsync(default);
            Assert.AreEqual(2, legacy.ConversionWorkers);
            Assert.AreEqual(0, legacy.AnalysisWorkers);
            Assert.AreEqual(4096, legacy.PipeBufferKiB);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
