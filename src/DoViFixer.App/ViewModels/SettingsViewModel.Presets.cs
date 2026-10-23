using System.Collections.ObjectModel;
using System.Text;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Settings;
using WpfFoundation.Dialogs;

namespace DoViFixer.App.ViewModels;
public sealed partial class SettingsViewModel
{
    private string? lastAppliedPreset;

    /// <summary>Built-in presets first, then the user's.</summary>
    public ObservableCollection<SettingsPreset> Presets { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    public partial SettingsPreset? SelectedPreset { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePresetCommand))]
    public partial string NewPresetName { get; set; } = "";

    /// <summary>Names the preset the settings match, or the one they were changed from.</summary>
    [ObservableProperty]
    public partial string ActivePresetText { get; private set; } = "Custom settings";

    public int PresetNameMaximumLength => PresetService.MaximumNameLength;

    private async Task LoadPresetsAsync(CancellationToken token)
    {
        string? selected = SelectedPreset?.Name;
        IReadOnlyList<SettingsPreset> list;
        try
        {
            list = await presets.ListAsync(token);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // A damaged presets file must not hide the built-in presets or the rest of Settings.
            list = BuiltInPresets.All;
            SetStatus(ViewStatus.Error, ex.Message);
        }

        Presets.Clear();
        foreach (var preset in list)
        {
            Presets.Add(preset);
        }

        SelectedPreset = Presets.FirstOrDefault(p => p.Name == selected) ?? PresetService.FindActive(Presets, CreateSnapshot()) ?? Presets.FirstOrDefault();
        RefreshActivePreset();
    }

    private void RefreshActivePreset()
    {
        var active = PresetService.FindActive(Presets, CreateSnapshot());
        ActivePresetText = active is not null
            ? $"Preset: {active.Name}"
            : lastAppliedPreset is not null ? $"Custom settings (changed from {lastAppliedPreset})" : "Custom settings";
    }

    private bool CanApplyPreset() => IsIdle && SelectedPreset is not null;

    [RelayCommand(CanExecute = nameof(CanApplyPreset))]
    private Task ApplyPreset() => RunAsync(async (token, _) =>
    {
        var preset = SelectedPreset!;
        await FlushAsync(token);
        var changes = await presets.PreviewAsync(preset, token);
        if (changes.Count == 0)
        {
            SetStatus(ViewStatus.Result, $"Settings already match the {preset.Name} preset.");
            return;
        }

        if (!await dialogs.ReviewAsync($"Apply {preset.Name}", DescribeChanges(preset, changes), "Apply preset"))
        {
            SetStatus(ViewStatus.Result, "Preset not applied. Settings were not changed.");
            return;
        }

        await Task.Run(() => presets.ApplyAsync(preset, token), token);
        lastSavedSettings = await settings.ReadAsync(token);
        lastAppliedPreset = lastSavedSettings.LastAppliedPreset;
        ApplyPreferences(lastSavedSettings);
        Saved?.Invoke(lastSavedSettings);
        SetStatus(ViewStatus.Result, $"Applied the {preset.Name} preset. {changes.Count} setting{(changes.Count == 1 ? "" : "s")} changed.");
    });

    private bool CanSavePreset() => IsIdle && !string.IsNullOrWhiteSpace(NewPresetName);

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private Task SavePreset() => RunAsync(async (token, _) =>
    {
        string name = NewPresetName.Trim();
        var existing = Presets.FirstOrDefault(p => !p.IsBuiltIn && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !await dialogs.ConfirmAsync("Replace preset", $"A preset named {existing.Name} already exists. Replace it with the current settings?", "Replace", "Keep"))
        {
            return;
        }

        await FlushAsync(token);
        var saved = await Task.Run(() => presets.SaveCurrentAsync(name, null, token), token);
        lastAppliedPreset = saved.Name;
        NewPresetName = "";
        SelectedPreset = null;
        await LoadPresetsAsync(token);
        SelectedPreset = Presets.FirstOrDefault(p => p.Name == saved.Name);
        SetStatus(ViewStatus.Result, $"Saved the current settings as the {saved.Name} preset.");
    });

    private bool CanDeletePreset() => IsIdle && SelectedPreset is { IsBuiltIn: false };

    [RelayCommand(CanExecute = nameof(CanDeletePreset))]
    private Task DeletePreset() => RunAsync(async (token, _) =>
    {
        var preset = SelectedPreset!;
        if (!await dialogs.ConfirmAsync("Delete preset", $"Delete the {preset.Name} preset? Your current settings do not change.", "Delete", "Keep", isDestructive: true))
        {
            return;
        }

        await Task.Run(() => presets.DeleteAsync(preset.Name, token), token);
        SelectedPreset = null;
        await LoadPresetsAsync(token);
        SetStatus(ViewStatus.Result, $"Deleted the {preset.Name} preset. Settings were not changed.");
    });

    private static string DescribeChanges(SettingsPreset preset, IReadOnlyList<PresetChange> changes)
    {
        var text = new StringBuilder();
        var risky = changes.Where(change => change.IncreasesRisk).ToList();
        if (risky.Count > 0)
        {
            text.AppendLine("Caution: these changes can lose data or picture detail that the current settings keep:");
            foreach (var change in risky)
            {
                text.AppendLine($"  • {change.Setting}: {change.To}");
            }

            text.AppendLine();
        }

        if (preset.Description.Length > 0)
        {
            text.AppendLine(preset.Description);
            text.AppendLine();
        }

        text.AppendLine("These settings will change:");
        foreach (var change in changes)
        {
            text.AppendLine($"  • {change.Setting}: {change.From} → {change.To}");
        }

        text.AppendLine();
        text.Append("Folders, worker counts, tool paths and appearance stay as they are. Nothing is converted until you start a conversion.");
        return text.ToString();
    }
}
