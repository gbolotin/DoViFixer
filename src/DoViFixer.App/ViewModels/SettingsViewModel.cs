using System.Collections.ObjectModel;
using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Abstractions;

namespace DoViFixer.App.ViewModels;
public sealed class SettingsViewModel : OperationViewModel
{
    private readonly SettingsService settings;
    private readonly IThemeService themeService;
    private string temporary = "";
    private string destination = "";
    private bool otherFolder;
    private bool replaceOriginal;
    private bool createArchive;
    private bool includeSimple;
    private bool forceComplex;
    private bool automaticallyScanAddedFiles = true;
    private bool useCachedResults = true;
    private bool autoSelectAfterScan = true;
    private NativeTool selectedTool;
    private string toolPath = "";
    private AppTheme selectedTheme = AppTheme.System;
    private readonly object lockObject = new();
    private TaskCompletionSource? pendingTcs;
    private bool isSaving;
    private bool isLoading;
    private string? saveError;
    private UserSettings? lastSavedSettings;

    public Task SaveTask { get; private set; } = Task.CompletedTask;
    public bool IsSaving
    {
        get => isSaving;
        private set
        {
            SetProperty(ref isSaving, value);
            DiscardChangesCommand?.RaiseCanExecuteChanged();
        }
    }
    public string? SaveError
    {
        get => saveError;
        private set
        {
            SetProperty(ref saveError, value);
            RaisePropertyChanged(nameof(HasSaveError));
            DiscardChangesCommand?.RaiseCanExecuteChanged();
        }
    }
    public bool HasSaveError => SaveError is not null;
    public string? DestinationError => OtherFolder && string.IsNullOrWhiteSpace(Destination) ? "Choose an output folder." : null;

    public event Action<UserSettings>? Saved;
    public SettingsViewModel(SettingsService settings, DependencyService dependencies, DependencySetup setup, IUserDialogs dialogs, IAnalysisCache cache, IThemeService themeService)
    {
        this.settings = settings;
        this.themeService = themeService;
        LoadCommand = new(() => RunAsync(async (token, _) =>
        {
            await FlushAsync(token);
            lastSavedSettings = await settings.ReadAsync(token);
            ApplyPreferences(lastSavedSettings);
            Status = "Settings loaded.";
        }), () => IsIdle);
        BrowseTemporaryCommand = new(() => Temporary = dialogs.PickFolder() ?? Temporary);
        BrowseDestinationCommand = new(() => Destination = dialogs.PickFolder() ?? Destination);
        BrowseToolCommand = new(() => ToolPath = dialogs.PickFiles("Executables|*.exe").FirstOrDefault() ?? ToolPath);
        SaveCommand = new(() => RunAsync(async (token, _) => await SaveAsync(token)), () => IsIdle);
        DiscardChangesCommand = new(DiscardChanges, () => IsIdle && !IsSaving && HasSaveError && lastSavedSettings is not null);
        ClearCacheCommand = new(() => RunAsync(async (token, _) =>
        {
            int removed = await Task.Run(() => cache.ClearAsync(token), token);
            Status = $"Cleared {removed} cached analysis results. Future scans will analyze those files again.";
        }), () => IsIdle);
        CheckCommand = new(() => RunAsync(async (token, _) =>
        {
            var report = await dependencies.CheckAsync(DependencyRequirements.All, token);
            Tools.Clear();
            foreach (var tool in report.Tools)
            {
                Tools.Add(tool);
            }

            Status = report.Ready ? "All tools ready." : "Tools need attention. Set a validated path or open dependency setup.";
        }), () => IsIdle);
        InstallCommand = new(() => RunAsync(async (token, progress) =>
        {
            Status = await setup.EnsureAsync(progress, token) ? "Tools ready." : "Dependency setup incomplete.";
            var report = await dependencies.CheckAsync(DependencyRequirements.All, token);
            Tools.Clear();
            foreach (var tool in report.Tools)
            {
                Tools.Add(tool);
            }
        }), () => IsIdle);
        SetToolCommand = new(() => RunAsync(async (token, _) =>
        {
            await settings.SetToolAsync(SelectedTool, ToolPath, token);
            Status = "Validated tool path saved and applied.";
        }), () => IsIdle && !string.IsNullOrWhiteSpace(ToolPath));
        ResetToolCommand = new(() => RunAsync(async (token, _) =>
        {
            await settings.ResetToolAsync(SelectedTool, token);
            Status = "Tool override reset. Check tools to rediscover.";
        }), () => IsIdle);
    }

    private void ApplyPreferences(UserSettings value)
    {
        isLoading = true;
        try
        {
            Temporary = value.TemporaryDirectory ?? "";
            Destination = value.OutputDirectory ?? "";
            OtherFolder = value.OutputDirectory is not null;
            ReplaceOriginal = value.ReplaceOriginal;
            CreateArchive = value.CreateElArchive;
            IncludeSimple = value.IncludeSimple;
            ForceComplex = value.ForceComplex;
            AutomaticallyScanAddedFiles = value.AutomaticallyScanAddedFiles;
            UseCachedResults = value.UseCachedResults;
            AutoSelectAfterScan = value.AutoSelectAfterScan;
            selectedTheme = value.Theme;
            RaisePropertyChanged(nameof(SelectedTheme));
            RaisePropertyChanged(nameof(IsSystemTheme));
            RaisePropertyChanged(nameof(IsLightTheme));
            RaisePropertyChanged(nameof(IsDarkTheme));
            themeService.ApplyTheme(value.Theme);
        }
        finally
        {
            isLoading = false;
        }
    }

    private void DiscardChanges()
    {
        if (lastSavedSettings is null)
        {
            return;
        }

        // Recovery must work even while the settings store or a folder is unavailable.
        ApplyPreferences(lastSavedSettings);
        SaveError = null;
        Status = "Unsaved changes discarded. Using the last saved settings.";
        Saved?.Invoke(lastSavedSettings);
    }

    public AppTheme SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (SetProperty(ref selectedTheme, value))
            {
                RaisePropertyChanged(nameof(IsSystemTheme));
                RaisePropertyChanged(nameof(IsLightTheme));
                RaisePropertyChanged(nameof(IsDarkTheme));
                themeService.ApplyTheme(value);
                TriggerAutoSave();
            }
        }
    }

    public bool IsSystemTheme
    {
        get => selectedTheme == AppTheme.System;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.System;
            }
        }
    }

    public bool IsLightTheme
    {
        get => selectedTheme == AppTheme.Light;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Light;
            }
        }
    }

    public bool IsDarkTheme
    {
        get => selectedTheme == AppTheme.Dark;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Dark;
            }
        }
    }

    public AppTheme[] Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    public bool AutomaticallyScanAddedFiles
    {
        get => automaticallyScanAddedFiles;
        set
        {
            if (SetProperty(ref automaticallyScanAddedFiles, value))
            {
                TriggerAutoSave();
            }
        }
    }
    public bool UseCachedResults
    {
        get => useCachedResults;
        set
        {
            if (SetProperty(ref useCachedResults, value))
            {
                TriggerAutoSave();
            }
        }
    }
    public bool AutoSelectAfterScan
    {
        get => autoSelectAfterScan;
        set
        {
            if (SetProperty(ref autoSelectAfterScan, value))
            {
                TriggerAutoSave();
            }
        }
    }
    public AsyncCommand ClearCacheCommand
    {
        get;
    }
    public string Temporary
    {
        get => temporary;
        set
        {
            if (SetProperty(ref temporary, value))
            {
                TriggerAutoSave();
            }
        }
    }
    public string Destination
    {
        get => destination;
        set
        {
            if (SetProperty(ref destination, value))
            {
                RaisePropertyChanged(nameof(DestinationError));
                TriggerAutoSave();
            }
        }
    }
    public bool OtherFolder
    {
        get => otherFolder;
        set
        {
            if (SetProperty(ref otherFolder, value))
            {
                RaisePropertyChanged(nameof(DestinationError));
                TriggerAutoSave();
            }
        }
    }
    public bool ReplaceOriginal
    {
        get => replaceOriginal;
        set
        {
            if (SetProperty(ref replaceOriginal, value))
            {
                TriggerAutoSave();
            }
        }
    }
    public bool CreateArchive
    {
        get => createArchive;
        set
        {
            if (SetProperty(ref createArchive, value))
            {
                TriggerAutoSave();
            }
        }
    }
    public bool IncludeSimple
    {
        get => includeSimple;
        set
        {
            if (SetProperty(ref includeSimple, value))
            {
                RaisePropertyChanged(nameof(AllowFel));
                TriggerAutoSave();
            }
        }
    }
    public bool ForceComplex
    {
        get => forceComplex;
        set
        {
            if (SetProperty(ref forceComplex, value))
            {
                RaisePropertyChanged(nameof(AllowFel));
                TriggerAutoSave();
            }
        }
    }
    public bool AllowFel
    {
        get => IncludeSimple || ForceComplex;
        set
        {
            if (value != (IncludeSimple || ForceComplex))
            {
                includeSimple = value;
                forceComplex = value;
                RaisePropertyChanged(nameof(IncludeSimple));
                RaisePropertyChanged(nameof(ForceComplex));
                RaisePropertyChanged(nameof(AllowFel));
                TriggerAutoSave();
            }
        }
    }

    private void TriggerAutoSave()
    {
        if (isLoading)
        {
            return;
        }

        lock (lockObject)
        {
            pendingTcs ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            SaveTask = pendingTcs.Task;
            if (!isSaving)
            {
                IsSaving = true;
                _ = ProcessSaveQueueAsync();
            }
        }
    }

    private async Task ProcessSaveQueueAsync()
    {
        while (true)
        {
            TaskCompletionSource tcs;
            lock (lockObject)
            {
                if (pendingTcs is null)
                {
                    IsSaving = false;
                    return;
                }

                tcs = pendingTcs;
                pendingTcs = null;
            }

            try
            {
                await SaveCoreAsync(CancellationToken.None);
                SaveError = null;
            }
            catch (Exception ex)
            {
                SaveError = ex.Message;
                Status = ex.Message;
            }
            finally
            {
                // Autosave failures are displayed and checked by FlushAsync; do not
                // leave an unobserved faulted task when a property setter saves.
                tcs.TrySetResult();
            }
        }
    }

    public async Task SaveAsync(CancellationToken token = default)
    {
        if (isLoading)
        {
            return;
        }

        Task task;
        lock (lockObject)
        {
            pendingTcs ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = pendingTcs.Task;
            SaveTask = task;
            if (!isSaving)
            {
                IsSaving = true;
                _ = ProcessSaveQueueAsync();
            }
        }

        await task.WaitAsync(token);
        await FlushAsync(token);
    }

    public async Task FlushAsync(CancellationToken token = default)
    {
        Task pending;
        do
        {
            pending = SaveTask;
            await pending.WaitAsync(token);
        }
        while (pending != SaveTask);

        if (SaveError is not null)
        {
            throw new InvalidOperationException(SaveError);
        }
    }

    private async Task SaveCoreAsync(CancellationToken token)
    {
        if (OtherFolder && string.IsNullOrWhiteSpace(Destination))
        {
            throw new InvalidOperationException("Choose an output folder.");
        }

        // Capture one consistent UI snapshot before moving directory validation
        // and persistence onto the worker thread.
        var snapshot = new UserSettings
        {
            TemporaryDirectory = Temporary,
            OutputDirectory = OtherFolder ? Destination : null,
            ReplaceOriginal = ReplaceOriginal,
            CreateElArchive = CreateArchive,
            AutomaticallyScanAddedFiles = AutomaticallyScanAddedFiles,
            UseCachedResults = UseCachedResults,
            AutoSelectAfterScan = AutoSelectAfterScan,
            Theme = SelectedTheme,
            IncludeSimple = IncludeSimple,
            ForceComplex = ForceComplex
        };
        await Task.Run(() => settings.SetPreferencesAsync(
            snapshot.TemporaryDirectory,
            snapshot.OutputDirectory,
            snapshot.ReplaceOriginal,
            snapshot.CreateElArchive,
            token,
            snapshot.AutomaticallyScanAddedFiles,
            snapshot.UseCachedResults,
            snapshot.AutoSelectAfterScan,
            snapshot.Theme,
            includeSimple: snapshot.IncludeSimple,
            forceComplex: snapshot.ForceComplex), token);

        lastSavedSettings = snapshot;
        Status = "Settings saved.";
        Saved?.Invoke(snapshot);
    }
    public NativeTool SelectedTool
    {
        get => selectedTool;
        set => SetProperty(ref selectedTool, value);
    }

    public string ToolPath
    {
        get => toolPath;
        set
        {
            SetProperty(ref toolPath, value);
            SetToolCommand.RaiseCanExecuteChanged();
        }
    }

    public NativeTool[] ToolNames
    {
        get;
    }
    = Enum.GetValues<NativeTool>();
    public ObservableCollection<DependencyStatus> Tools
    {
        get;
    }
    = [];
    public AsyncCommand LoadCommand
    {
        get;
    }
    public AsyncCommand SaveCommand
    {
        get;
    }
    public RelayCommand DiscardChangesCommand { get; }
    public AsyncCommand CheckCommand
    {
        get;
    }
    public AsyncCommand InstallCommand
    {
        get;
    }
    public AsyncCommand SetToolCommand
    {
        get;
    }
    public AsyncCommand ResetToolCommand
    {
        get;
    }
    public RelayCommand BrowseTemporaryCommand
    {
        get;
    }
    public RelayCommand BrowseDestinationCommand
    {
        get;
    }
    public RelayCommand BrowseToolCommand
    {
        get;
    }

    protected override void CommandsChanged()
    {
        LoadCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged();
        DiscardChangesCommand?.RaiseCanExecuteChanged();
        ClearCacheCommand?.RaiseCanExecuteChanged();
        CheckCommand?.RaiseCanExecuteChanged();
        InstallCommand?.RaiseCanExecuteChanged();
        SetToolCommand?.RaiseCanExecuteChanged();
        ResetToolCommand?.RaiseCanExecuteChanged();
    }
}
