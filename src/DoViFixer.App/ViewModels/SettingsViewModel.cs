using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Updates;
using WpfFoundation.Operations;

namespace DoViFixer.App.ViewModels;
public sealed partial class SettingsViewModel : OperationViewModel, INavigationPage, IPageActivation
{
    public string NavigationName => "Settings";
    public string? NavigationIcon => "\uE713";
    public string Version { get; } = ApplicationVersion.Of(typeof(SettingsViewModel).Assembly);

    private readonly SettingsService settings;
    private readonly IThemeService themeService;
    private readonly IAnalysisCache cache;
    private readonly IMediaPreview mediaPreview;
    private readonly DependencySetup setup;
    private readonly IFileDialogService files;
    private readonly IFileExplorer explorer;
    private bool includeSimple;
    private bool forceComplex;
    private AppTheme selectedTheme = AppTheme.System;
    private readonly object lockObject = new();
    private TaskCompletionSource? pendingTcs;
    private bool isLoading;
    private UserSettings? lastSavedSettings;

    public Task SaveTask { get; private set; } = Task.CompletedTask;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    public partial bool IsSaving { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaveError))]
    [NotifyCanExecuteChangedFor(nameof(DiscardChangesCommand))]
    public partial string? SaveError { get; private set; }
    public bool HasSaveError => SaveError is not null;
    public string? DestinationError => OtherFolder && string.IsNullOrWhiteSpace(Destination) ? "Choose an output folder." : null;

    public event Action<UserSettings>? Saved;
    public event Func<Task>? CacheClearing;
    public string CacheDirectory => cache.RootDirectory;
    [ObservableProperty]
    public partial string CacheSizeText { get; private set; } = "Cache: …";

    public DependencyReportViewModel Dependencies { get; }

    public SettingsViewModel(SettingsService settings, DependencySetup setup, DependencyReportViewModel dependencies, IFileDialogService files, IFileExplorer explorer, IAnalysisCache cache, IThemeService themeService, IMediaPreview mediaPreview, IOperationFeedback? feedback = null)
        : base(feedback)
    {
        this.settings = settings;
        this.themeService = themeService;
        this.cache = cache;
        this.mediaPreview = mediaPreview;
        this.setup = setup;
        this.files = files;
        this.explorer = explorer;
        Dependencies = dependencies;
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
            ShowCompletionNotifications = value.ShowCompletionNotifications;
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

    private bool CanDiscardChanges() => IsIdle && !IsSaving && HasSaveError && lastSavedSettings is not null;

    [RelayCommand(CanExecute = nameof(CanDiscardChanges))]
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

    [ObservableProperty]
    public partial bool AutomaticallyScanAddedFiles { get; set; } = true;

    partial void OnAutomaticallyScanAddedFilesChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial bool UseCachedResults { get; set; } = true;

    partial void OnUseCachedResultsChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial bool AutoSelectAfterScan { get; set; } = true;

    partial void OnAutoSelectAfterScanChanged(bool value) => TriggerAutoSave();

    /// <summary>Whether a finished operation also shows a Windows notification while the window is not active.</summary>
    [ObservableProperty]
    public partial bool ShowCompletionNotifications { get; set; } = true;

    partial void OnShowCompletionNotificationsChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial string Temporary { get; set; } = "";

    partial void OnTemporaryChanged(string value) => TriggerAutoSave();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationError))]
    public partial string Destination { get; set; } = "";

    partial void OnDestinationChanged(string value) => TriggerAutoSave();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DestinationError))]
    public partial bool OtherFolder { get; set; }

    partial void OnOtherFolderChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial bool ReplaceOriginal { get; set; }

    partial void OnReplaceOriginalChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial bool CreateArchive { get; set; }

    partial void OnCreateArchiveChanged(bool value) => TriggerAutoSave();

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
            if (!IsSaving)
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
            if (!IsSaving)
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
            ShowCompletionNotifications = ShowCompletionNotifications,
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
            forceComplex: snapshot.ForceComplex,
            showCompletionNotifications: snapshot.ShowCompletionNotifications), token);

        lastSavedSettings = snapshot;
        SetStatus(ViewStatus.SettingsSaved);
        Saved?.Invoke(snapshot);
    }
    [ObservableProperty]
    public partial NativeTool SelectedTool { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SetToolCommand))]
    public partial string ToolPath { get; set; } = "";

    public NativeTool[] ToolNames
    {
        get;
    }
    = Enum.GetValues<NativeTool>();
    /// <summary>Settings reload every time the page is shown.</summary>
    public Task OnActivatedAsync(CancellationToken cancellationToken) => LoadCommand.ExecuteAsync(null);

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Load() => RunAsync(async (token, _) =>
    {
        await FlushAsync(token);
        lastSavedSettings = await settings.ReadAsync(token);
        ApplyPreferences(lastSavedSettings);
        await RefreshCacheSizeAsync(token);
        SetStatus(ViewStatus.SettingsLoaded);
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Save() => RunAsync(async (token, _) => await SaveAsync(token));

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ClearCache() => RunAsync(async (token, _) =>
    {
        if (CacheClearing is not null)
        {
            await CacheClearing();
        }
        int removed = await Task.Run(() => cache.ClearAsync(token), token);
        int frames = await Task.Run(() => mediaPreview.ClearCacheAsync(token), token);
        await RefreshCacheSizeAsync(token);
        SetStatus(ViewStatus.Result, $"Cleared {removed} cached analysis results and {frames} frame previews. They will be recreated when needed.");
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Check() => RunAsync(async (token, _) =>
    {
        var report = await setup.CheckAsync(token);
        SetStatus(report.Ready ? ViewStatus.ToolsReady : ViewStatus.ToolsNeedAttention);
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Install() => RunAsync("Tool installation", async (token, progress) =>
    {
        SetStatus(ViewStatus.Progress, "Checking dependencies…");
        SetStatus(await setup.EnsureAsync(progress, token) ? ViewStatus.ToolsReady : ViewStatus.DependencySetupIncomplete);
    });

    private bool CanSetTool() => IsIdle && !string.IsNullOrWhiteSpace(ToolPath);

    [RelayCommand(CanExecute = nameof(CanSetTool))]
    private Task SetTool() => RunAsync(async (token, _) =>
    {
        await settings.SetToolAsync(SelectedTool, ToolPath, token);
        await RecheckToolsAsync(token);
        SetStatus(ViewStatus.ToolPathSaved);
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ResetTool() => RunAsync(async (token, _) =>
    {
        await settings.ResetToolAsync(SelectedTool, token);
        await RecheckToolsAsync(token);
        SetStatus(ViewStatus.ToolOverrideReset);
    });

    [RelayCommand]
    private void BrowseTemporary() => Temporary = files.PickFolder() ?? Temporary;

    [RelayCommand]
    private void BrowseDestination() => Destination = files.PickFolder() ?? Destination;

    [RelayCommand]
    private void BrowseTool() => ToolPath = files.PickFiles("Executables|*.exe", allowMultiple: false).FirstOrDefault() ?? ToolPath;

    [RelayCommand]
    private void OpenCacheFolder() => explorer.OpenFolder(CacheDirectory);

    [RelayCommand]
    private void OpenLogs() => explorer.OpenLogs();

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
