using DoViFixer.Application.Settings;
using DoViFixer.Infrastructure.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;
[TestClass]
public sealed class PresetStoreTests
{
    [TestMethod]
    public async Task MissingFileHasNoSavedPresets()
    {
        var store = new PresetStore(new StorageOptions(directory));
        Assert.IsEmpty(await store.ReadAsync(default));
    }

    [TestMethod]
    public async Task SavedPresetsRoundTripWithoutBuiltIns()
    {
        var store = new PresetStore(new StorageOptions(directory));
        var mine = new SettingsPreset("Mine", "For films I keep", BuiltInPresets.ReplaceOriginalsKeepArchive.Values with { ScanSampleSeconds = 1.5, FreeSpaceMarginPercent = 35.5 });

        await store.WriteAsync([BuiltInPresets.Default, mine], default);
        var read = await store.ReadAsync(default);

        Assert.AreEqual(mine, read.Single());
        StringAssert.Contains(await File.ReadAllTextAsync(new StorageOptions(directory).PresetsPath), "\"FullIntegrity\"");
    }

    [TestMethod]
    public async Task DamagedFileIsReportedWithItsPath()
    {
        var storage = new StorageOptions(directory);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(storage.PresetsPath, "{ not json");

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new PresetStore(storage).ReadAsync(default));
        StringAssert.Contains(error.Message, storage.PresetsPath);
    }

    private readonly string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-preset-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
