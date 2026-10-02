using DoViFixer.App.Navigation;
using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Abstractions;

namespace DoViFixer.App.ViewModels;
public sealed class SettingsViewModel : OperationViewModel, INavigationPage
{
    public string NavigationName => "Settings";
    public string? NavigationIcon => "\uE713";

    private readonly SettingsService settings;
    private readonly IThemeService themeService;
    private readonly IAnalysisCache cache;
    private readonly IMediaPreview mediaPreview;
    private readonly DependencySetup setup;
    private string cacheSizeText = "Cache: …";
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
            DiscardChangesCommand?.NotifyCanExecuteChanged();
        }
    }
    public string? SaveError
    {
        get => saveError;
        private set
        {
            SetProperty(ref saveError, value);
            OnPropertyChanged(nameof(HasSaveError));
            DiscardChangesCommand?.NotifyCanExecuteChanged();
        }
    }
    public bool HasSaveError => SaveError is not null;
    public string? DestinationError => OtherFolder && string.IsNullOrWhiteSpace(Destination) ? "Choose an output folder." : null;

    public event Action<UserSettings>? Saved;
    public event Func<Task>? CacheClearing;
    public string CacheDirectory => cache.RootDirectory;
    public string CacheSizeText
    {
        get => cacheSizeText;
        private set => SetProperty(ref cacheSizeText, value);
    }

    public DependencyReportViewModel Dependencies { get; }

    public SettingsViewModel(SettingsService settings, DependencySetup setup, DependencyReportViewModel dependencies, IFileDialogService files, IFileExplorer explorer, IAnalysisCache cache, IThemeService themeService, IMediaPreview mediaPreview)
    {
        this.settings = settings;
        this.themeService = themeService;
        this.cache = cache;
        this.mediaPreview = mediaPreview;
        this.setup = setup;
        Dependencies = dependencies;
        LoadCommand = new(() => RunAsync(async (token, _) =>
        {
            await FlushAsync(token);
            lastSavedSettings = await settings.ReadAsync(token);
            ApplyPreferences(lastSavedSettings);
            await RefreshCacheSizeAsync(token);
            SetStatus(ViewStatus.SettingsLoaded);
        }), () => IsIdle);
        BrowseTemporaryCommand = new(() => Temporary = files.PickFolder() ?? Temporary);
        BrowseDestinationCommand = new(() => Destination = files.PickFolder() ?? Destination);
        BrowseToolCommand = new(() => ToolPath = files.PickFiles("Executables|*.exe", allowMultiple: false).FirstOrDefault() ?? ToolPath);
        OpenCacheFolderCommand = new(() => explorer.OpenFolder(CacheDirectory));
        SaveCommand = new(() => RunAsync(async (token, _) => await SaveAsync(token)), () => IsIdle);
        DiscardChangesCommand = new(DiscardChanges, () => IsIdle && !IsSaving && HasSaveError && lastSavedSettings is not null);
        ClearCacheCommand = new(() => RunAsync(async (token, _) =>
        {
            if (CacheClearing is not null)
            {
                await CacheClearing();
            }
            int removed = await Task.Run(() => cache.ClearAsync(token), token);
            int frames = await Task.Run(() => mediaPreview.ClearCacheAsync(token), token);
            await RefreshCacheSizeAsync(token);
            SetStatus(ViewStatus.Result, $"Cleared {removed} cached analysis results and {frames} frame previews. They will be recreated when needed.");
        }), () => IsIdle);
        CheckCommand = new(() => RunAsync(async (token, _) =>
        {
            var report = await setup.CheckAsync(token);
            SetStatus(report.Ready ? ViewStatus.ToolsReady : ViewStatus.ToolsNeedAttention);
        }), () => IsIdle);
        InstallCommand = new(() => RunAsync(async (token, progress) =>
        {
            SetStatus(ViewStatus.Progress, "Checking dependencies…");
            SetStatus(await setup.EnsureAsync(progress, token) ? ViewStatus.ToolsReady : ViewStatus.DependencySetupIncomplete);
        }), () => IsIdle);
        SetToolCommand = new(() => RunAsync(async (token, _) =>
        {
            await settings.SetToolAsync(SelectedTool, ToolPath, token);
            await RecheckToolsAsync(token);
            SetStatus(ViewStatus.ToolPathSaved);
        }), () => IsIdle && !string.IsNullOrWhiteSpace(ToolPath));
        ResetToolCommand = new(() => RunAsync(async (token, _) =>
        {
            await settings.ResetToolAsync(SelectedTool, token);
            await RecheckToolsAsync(token);
            SetStatus(ViewStatus.ToolOverrideReset);
        }), () => IsIdle);
    }

    private async Task RecheckToolsAsync(CancellationToken token)
    {
        try
        {
            await setup.CheckAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The change is already saved; the dependency warning reports the failed check.
        }
    }

    private async Task RefreshCacheSizeAsync(CancellationToken token)
    {
        CacheSizeText = "Cache: …";
        try
        {
            long bytes = await Task.Run(async () => await cache.GetSizeAsync(token) + await mediaPreview.GetCacheSizeAsync(token), token);
            CacheSizeText = bytes switch
            {
                >= 1073741824 => $"Cache: {bytes / 1073741824d:0.##} GiB",
                >= 1048576 => $"Cache: {bytes / 1048576d:0.##} MiB",
                >= 1024 => $"Cache: {bytes / 1024d:0.##} KiB",
                _ => $"Cache: {bytes} B"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CacheSizeText = "Cache size unavailable";
        }
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
            OnPropertyChanged(nameof(SelectedTheme));
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
            themeService.ApplyTheme(value.Theme.ToThemePreference());
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
        SetStatus(ViewStatus.SettingsDiscarded);
        Saved?.Invoke(lastSavedSettings);
    }

    public AppTheme SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (SetProperty(ref selectedTheme, value))
            {
                OnPropertyChanged(nameof(IsSystemTheme));
                OnPropertyChanged(nameof(IsLightTheme));
                OnPropertyChanged(nameof(IsDarkTheme));
                themeService.ApplyTheme(value.ToThemePreference());
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
    public AsyncRelayCommand ClearCacheCommand
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
                OnPropertyChanged(nameof(DestinationError));
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
                OnPropertyChanged(nameof(DestinationError));
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
                OnPropertyChanged(nameof(AllowFel));
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
                OnPropertyChanged(nameof(AllowFel));
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
                OnPropertyChanged(nameof(IncludeSimple));
                OnPropertyChanged(nameof(ForceComplex));
                OnPropertyChanged(nameof(AllowFel));
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
                SetStatus(ViewStatus.Error, ex.Message);
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
        SetStatus(ViewStatus.SettingsSaved);
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
            SetToolCommand.NotifyCanExecuteChanged();
        }
    }

    public NativeTool[] ToolNames
    {
        get;
    }
    = Enum.GetValues<NativeTool>();
    public AsyncRelayCommand LoadCommand
    {
        get;
    }
    public AsyncRelayCommand SaveCommand
    {
        get;
    }
    public RelayCommand DiscardChangesCommand { get; }
    public AsyncRelayCommand CheckCommand
    {
        get;
    }
    public AsyncRelayCommand InstallCommand
    {
        get;
    }
    public AsyncRelayCommand SetToolCommand
    {
        get;
    }
    public AsyncRelayCommand ResetToolCommand
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
    public RelayCommand OpenCacheFolderCommand
    {
        get;
    }

    protected override void CommandsChanged()
    {
        LoadCommand?.NotifyCanExecuteChanged();
        SaveCommand?.NotifyCanExecuteChanged();
        DiscardChangesCommand?.NotifyCanExecuteChanged();
        ClearCacheCommand?.NotifyCanExecuteChanged();
        CheckCommand?.NotifyCanExecuteChanged();
        InstallCommand?.NotifyCanExecuteChanged();
        SetToolCommand?.NotifyCanExecuteChanged();
        ResetToolCommand?.NotifyCanExecuteChanged();
    }
}
