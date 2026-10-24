using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class StoredUserDataTests
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-user-data-" + Guid.NewGuid().ToString("N"));
    private StorageOptions Options => new(directory);
    private SettingsStore Settings => new(Options, NullLogger<SettingsStore>.Instance);
    private StoredUserData UserData() => new(Options, Settings, new PresetStore(Options), NullLogger<StoredUserData>.Instance);
    private string LibraryPath => Path.Combine(directory, "library.db");

    private async Task CreateLibraryAsync()
    {
        using var library = new MediaLibrary(Options, Settings, TimeProvider.System, NullLogger<MediaLibrary>.Instance, NullLogger<AnalysisCache>.Instance);
        await library.GetSizeAsync(default);
        SqliteConnection.ClearAllPools();
    }

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
    public async Task EmptyFolderHasNothingToKeep()
    {
        Assert.AreEqual(new StoredUserDataState(UserDataParts.None, UserDataParts.None), await UserData().InspectAsync(default));
    }

    [TestMethod]
    public async Task SettingsAndLibraryOfThisVersionAreCompatible()
    {
        await Settings.UpdateAsync(settings => settings with { Theme = AppTheme.Dark }, default);
        await new PresetStore(Options).WriteAsync([new SettingsPreset("Mine", "", BuiltInPresets.Default.Values)], default);
        await CreateLibraryAsync();

        var state = await UserData().InspectAsync(default);

        Assert.AreEqual(new StoredUserDataState(UserDataParts.Settings | UserDataParts.Library, UserDataParts.None), state);
    }

    [TestMethod]
    public async Task UnreadableSettingsAreIncompatible()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Options.SettingsPath, "{ \"Theme\": \"FromTheFuture\" }");

        var state = await UserData().InspectAsync(default);

        Assert.AreEqual(new StoredUserDataState(UserDataParts.None, UserDataParts.Settings), state);
    }

    [TestMethod]
    public async Task PresetsOfAnotherSchemaMakeTheSettingsIncompatible()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Options.PresetsPath, "{ \"SchemaVersion\": 2, \"Presets\": [] }");

        Assert.AreEqual(UserDataParts.Settings, (await UserData().InspectAsync(default)).Incompatible);
    }

    [TestMethod]
    public async Task LibraryOfANewerSchemaIsIncompatible()
    {
        await CreateLibraryAsync();
        await using (var connection = new SqliteConnection($"Data Source={LibraryPath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version={MediaLibrary.SchemaVersion + 1}";
            await command.ExecuteNonQueryAsync();
        }

        Assert.AreEqual(new StoredUserDataState(UserDataParts.None, UserDataParts.Library), await UserData().InspectAsync(default));
    }

    [TestMethod]
    public async Task LibraryThatIsNotADatabaseIsIncompatible()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(LibraryPath, new string('x', 4096));

        Assert.AreEqual(UserDataParts.Library, (await UserData().InspectAsync(default)).Incompatible);
    }

    [TestMethod]
    public async Task CacheWithoutADatabaseIsACompatibleLibrary()
    {
        Directory.CreateDirectory(Path.Combine(Options.CacheDirectory, "analysis"));

        Assert.AreEqual(UserDataParts.Library, (await UserData().InspectAsync(default)).Compatible);
    }

    [TestMethod]
    public async Task RemovingTheLibraryKeepsSettingsToolsAndLogs()
    {
        await Settings.UpdateAsync(settings => settings with { Theme = AppTheme.Dark }, default);
        await CreateLibraryAsync();
        Directory.CreateDirectory(Path.Combine(Options.CacheDirectory, "frames"));
        Directory.CreateDirectory(Path.Combine(Options.ToolsDirectory, "ffmpeg"));
        Directory.CreateDirectory(Path.Combine(directory, "Logs"));

        await UserData().RemoveAsync(UserDataParts.Library, default);

        Assert.IsFalse(File.Exists(LibraryPath));
        Assert.IsFalse(Directory.Exists(Options.CacheDirectory));
        Assert.IsTrue(File.Exists(Options.SettingsPath));
        Assert.IsTrue(Directory.Exists(Path.Combine(Options.ToolsDirectory, "ffmpeg")));
        Assert.IsTrue(Directory.Exists(Path.Combine(directory, "Logs")));
        Assert.AreEqual(new StoredUserDataState(UserDataParts.Settings, UserDataParts.None), await UserData().InspectAsync(default));
    }

    [TestMethod]
    public async Task RemovingTheSettingsKeepsTheLibrary()
    {
        await Settings.UpdateAsync(settings => settings with { Theme = AppTheme.Dark }, default);
        await new PresetStore(Options).WriteAsync([new SettingsPreset("Mine", "", BuiltInPresets.Default.Values)], default);
        await CreateLibraryAsync();

        await UserData().RemoveAsync(UserDataParts.Settings, default);

        Assert.IsFalse(File.Exists(Options.SettingsPath));
        Assert.IsFalse(File.Exists(Options.PresetsPath));
        Assert.AreEqual(new StoredUserDataState(UserDataParts.Library, UserDataParts.None), await UserData().InspectAsync(default));
    }
}
