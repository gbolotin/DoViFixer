using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;

[TestClass]
public sealed class PresetServiceTests
{
    [TestMethod]
    public void BuiltInPresetsAreValidAndNeverSampleLessThanTheDefault()
    {
        var defaults = new UserSettings();
        foreach (var preset in BuiltInPresets.All)
        {
            preset.Values.Validate();
            Assert.IsTrue(preset.IsBuiltIn, preset.Name);
            Assert.IsGreaterThanOrEqualTo(defaults.ScanSampleCount, preset.Values.ScanSampleCount, preset.Name);
            Assert.IsGreaterThanOrEqualTo((int)defaults.VerificationLevel, (int)preset.Values.VerificationLevel, preset.Name);
        }

        Assert.AreEqual(BuiltInPresets.All.Count, BuiltInPresets.All.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public async Task ApplyingSetsEveryPresetValueAndKeepsMachineSettings()
    {
        var runtime = new Runtime
        {
            Settings = new UserSettings
            {
                OutputDirectory = @"D:\Converted",
                ConversionWorkers = 3,
                Theme = AppTheme.Dark,
                ReplaceOriginal = true,
                ScanSampleCount = 25
            }
        };
        var service = runtime.Service();

        await service.ApplyAsync(BuiltInPresets.ThoroughCheck, default);

        Assert.AreEqual(BuiltInPresets.ThoroughCheck.Values, PresetValues.From(runtime.Settings));
        Assert.AreEqual(@"D:\Converted", runtime.Settings.OutputDirectory);
        Assert.AreEqual(3, runtime.Settings.ConversionWorkers);
        Assert.AreEqual(AppTheme.Dark, runtime.Settings.Theme);
        Assert.AreEqual(BuiltInPresets.ThoroughCheck.Name, runtime.Settings.LastAppliedPreset);
    }

    [TestMethod]
    public async Task ApplyingInEitherOrderGivesTheSameSettings()
    {
        var first = new Runtime();
        await first.Service().ApplyAsync(BuiltInPresets.ReplaceOriginalsKeepArchive, default);
        await first.Service().ApplyAsync(BuiltInPresets.ExhaustiveCheck, default);

        var second = new Runtime();
        await second.Service().ApplyAsync(BuiltInPresets.ExhaustiveCheck, default);

        // The later preset sets every value, so nothing from the earlier one survives, such as replacing originals.
        Assert.AreEqual(PresetValues.From(second.Settings), PresetValues.From(first.Settings));
        Assert.IsFalse(first.Settings.ReplaceOriginal);
    }

    [TestMethod]
    public void ComparisonListsOnlyChangesAndMarksRiskyOnes()
    {
        var changes = PresetService.Compare(new UserSettings(), BuiltInPresets.ReplaceOriginalsKeepArchive.Values);

        var replace = changes.Single(change => change.Setting == "Replace original after verification");
        Assert.AreEqual("Off", replace.From);
        Assert.AreEqual("On", replace.To);
        Assert.IsTrue(replace.IncreasesRisk);
        Assert.IsFalse(changes.Single(change => change.Setting == "Back up enhancement layer").IncreasesRisk);
        Assert.IsFalse(changes.Single(change => change.Setting == "Clips per video").IncreasesRisk);
        Assert.IsFalse(changes.Any(change => change.Setting == "Convert to HDR10"));

        var back = PresetService.Compare(BuiltInPresets.ReplaceOriginalsKeepArchive.Values.ApplyTo(new UserSettings()), BuiltInPresets.Default.Values);
        Assert.IsTrue(back.Single(change => change.Setting == "Clips per video").IncreasesRisk);
        Assert.IsTrue(back.Single(change => change.Setting == "Back up enhancement layer").IncreasesRisk);
        Assert.IsFalse(back.Single(change => change.Setting == "Replace original after verification").IncreasesRisk);
    }

    [TestMethod]
    public async Task ActivePresetIsTheOneWhoseValuesAllMatch()
    {
        var runtime = new Runtime();
        var service = runtime.Service();
        var list = await service.ListAsync(default);
        Assert.AreEqual(BuiltInPresets.Default, PresetService.FindActive(list, runtime.Settings));
        Assert.IsNull(PresetService.FindActive(list, runtime.Settings with { ScanSampleCount = 11 }));
        Assert.AreEqual(BuiltInPresets.Default, PresetService.FindActive(list, runtime.Settings with { Theme = AppTheme.Light, ConversionWorkers = 8 }));
    }

    [TestMethod]
    public async Task SavingCapturesCurrentSettingsAndReplacesTheSameName()
    {
        var runtime = new Runtime { Settings = new UserSettings { ScanSampleCount = 30 } };
        var service = runtime.Service();

        await service.SaveCurrentAsync("  Films  ", "First", default);
        runtime.Settings = runtime.Settings with { ScanSampleCount = 50 };
        var saved = await service.SaveCurrentAsync("films", null, default);

        Assert.AreEqual("films", runtime.Saved.Single().Name);
        Assert.AreEqual(50, runtime.Saved.Single().Values.ScanSampleCount);
        Assert.AreEqual("", saved.Description);
        Assert.AreEqual("films", runtime.Settings.LastAppliedPreset);
        Assert.AreEqual(saved, PresetService.FindActive(await service.ListAsync(default), runtime.Settings));
    }

    [TestMethod]
    public async Task BuiltInPresetsCannotBeReplacedOrDeleted()
    {
        var service = new Runtime().Service();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SaveCurrentAsync("default", null, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.DeleteAsync("Default", default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SaveCurrentAsync("   ", null, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.SaveCurrentAsync(new string('x', PresetService.MaximumNameLength + 1), null, default));
    }

    [TestMethod]
    public async Task DeletingRemovesOnlyTheSavedPresetAndKeepsSettings()
    {
        var runtime = new Runtime();
        var service = runtime.Service();
        await service.SaveCurrentAsync("Mine", null, default);
        var before = runtime.Settings;

        Assert.IsTrue(await service.DeleteAsync("MINE", default));
        Assert.IsFalse(await service.DeleteAsync("Mine", default));
        Assert.IsEmpty(runtime.Saved);
        Assert.AreEqual(before, runtime.Settings);
    }

    [TestMethod]
    public async Task InvalidSavedValuesAreRejectedBeforeAnySettingChanges()
    {
        var runtime = new Runtime();
        var broken = new SettingsPreset("Broken", "", BuiltInPresets.Default.Values with { ScanSampleCount = 0 });
        var before = runtime.Settings;

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => runtime.Service().ApplyAsync(broken, default));
        Assert.AreEqual(before, runtime.Settings);
    }

    [TestMethod]
    public async Task UnknownPresetNamesAreReported()
    {
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => new Runtime().Service().FindAsync("Missing", default));
        StringAssert.Contains(error.Message, "Missing");
    }

    private sealed class Runtime : ISettingsStore, IPresetStore
    {
        public UserSettings Settings { get; set; } = new();
        public List<SettingsPreset> Saved { get; private set; } = [];

        public PresetService Service() => new(this, this, NullLogger<PresetService>.Instance);

        Task<UserSettings> ISettingsStore.ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

        public Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken cancellationToken)
        {
            Settings = update(Settings);
            return Task.CompletedTask;
        }

        Task<IReadOnlyList<SettingsPreset>> IPresetStore.ReadAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SettingsPreset>>([.. Saved]);

        public Task WriteAsync(IReadOnlyList<SettingsPreset> presets, CancellationToken cancellationToken)
        {
            Saved = [.. presets];
            return Task.CompletedTask;
        }
    }
}
