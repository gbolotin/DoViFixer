using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Updates;
using DoViFixer.Application.Diagnostics;
using WpfFoundation.Operations;

namespace DoViFixer.App.ViewModels;
public sealed partial class SettingsViewModel : OperationViewModel
{
    public string Version { get; } = ApplicationVersion.Of(typeof(SettingsViewModel).Assembly);

    private readonly SettingsService settings;
    private readonly IThemeService themeService;
    private readonly IMediaLibrary library;
    private readonly Application.Library.MediaLibraryService libraryService;
    private readonly DependencySetup setup;
    private readonly IFileDialogService files;
    private readonly IFileExplorer explorer;
    private readonly DiagnosticsService diagnostics;
    private readonly PresetService presets;
    private readonly IDialogService dialogs;
    public string DiagnosticContentsNotice => DiagnosticsService.ContentsNotice;
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
    public event Action? LibraryDeleted;
    public string LibraryDirectory => Path.GetDirectoryName(library.FilePath)!;
    [ObservableProperty]
    public partial string LibrarySizeText { get; private set; } = "Library: …";

    public DependencyReportViewModel Dependencies { get; }

    public SettingsViewModel(SettingsService settings, DependencySetup setup, DependencyReportViewModel dependencies, IFileDialogService files, IFileExplorer explorer, IMediaLibrary library, Application.Library.MediaLibraryService libraryService, IThemeService themeService, DiagnosticsService diagnostics, PresetService presets, IDialogService dialogs, IOperationFeedback? feedback = null)
        : base(feedback)
    {
        this.settings = settings;
        this.themeService = themeService;
        this.library = library;
        this.libraryService = libraryService;
        this.setup = setup;
        this.files = files;
        this.explorer = explorer;
        this.diagnostics = diagnostics;
        this.presets = presets;
        this.dialogs = dialogs;
        Dependencies = dependencies;
        var general = new GeneralSettingsSection(this);
        Sections = new NavigationService(
            [general, new PresetsSettingsSection(this), new AnalysisSettingsSection(this), new ConversionSettingsSection(this), new SafetySettingsSection(this), new PerformanceSettingsSection(this), new NativeToolsSettingsSection(this), new AboutSettingsSection(this)],
            initialPage: general);
    }

    /// <summary>
    /// The pages of the settings frame. Moving between them never waits: saving, errors and progress show in the frame's footer.
    /// </summary>
    public NavigationService Sections { get; }

    [ObservableProperty]
    public partial int BrightnessMarginNits { get; set; } = 50;
    partial void OnBrightnessMarginNitsChanged(int value) => NumericPreferenceChanged(nameof(BrightnessMarginText));

    [ObservableProperty]
    public partial int AssumedPeakBrightnessNits { get; set; } = 1000;
    partial void OnAssumedPeakBrightnessNitsChanged(int value) => NumericPreferenceChanged(nameof(AssumedPeakBrightnessText));

    [RelayCommand]
    private void ResetBrightnessMargin() => BrightnessMarginText = "50";

    [RelayCommand]
    private void ResetAssumedPeakBrightness() => AssumedPeakBrightnessText = "1000";

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

    private async Task RefreshLibrarySizeAsync(CancellationToken token)
    {
        LibrarySizeText = "Library: …";
        try
        {
            long bytes = await Task.Run(() => library.GetSizeAsync(token), token);
            LibrarySizeText = bytes switch
            {
                >= 1073741824 => $"Library: {bytes / 1073741824d:0.##} GiB",
                >= 1048576 => $"Library: {bytes / 1048576d:0.##} MiB",
                >= 1024 => $"Library: {bytes / 1024d:0.##} KiB",
                _ => $"Library: {bytes} B"
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LibrarySizeText = "Library size unavailable";
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
            SafeMode = value.SafeMode;
            FolderSearchDepth = value.FolderSearchDepth;
            RemoveDolbyVision = value.RemoveDolbyVision;
            ConversionWorkers = settings.Hardware.ConversionWorkers(value.ConversionWorkers);
            ScanSampleCount = value.ScanSampleCount;
            ScanSampleSeconds = value.ScanSampleSeconds;
            MinimumScanCoveragePercent = value.MinimumScanCoveragePercent;
            InspectionPeakPercentile = value.InspectionPeakPercentile;
            BrightnessMarginNits = value.BrightnessMarginNits;
            AssumedPeakBrightnessNits = value.AssumedPeakBrightnessNits;
            AnalysisWorkers = settings.Hardware.AnalysisWorkers(value.AnalysisWorkers);
            ToolProcessPriority = value.ToolProcessPriority;
            PipeBufferKiB = value.PipeBufferKiB;
            VerificationLevel = (int)value.VerificationLevel;
            RecycleDeletedOriginalsAndBackups = value.RecycleDeletedOriginalsAndBackups;
            FreeSpaceMarginPercent = value.FreeSpaceMarginPercent;
            IncludeSimple = value.IncludeSimple;
            ForceComplex = value.ForceComplex;
            AutomaticallyScanAddedFiles = value.AutomaticallyScanAddedFiles;
            UseCachedResults = value.UseCachedResults;
            ShowCompletionNotifications = value.ShowCompletionNotifications;
            ShowSelectionCheckBoxes = value.ShowSelectionCheckBoxes;
            selectedTheme = value.Theme;
            OnPropertyChanged(nameof(SelectedTheme));
            OnPropertyChanged(nameof(IsSystemTheme));
            OnPropertyChanged(nameof(IsLightTheme));
            OnPropertyChanged(nameof(IsDarkTheme));
            themeService.ApplyTheme(value.Theme.ToThemePreference());
        }
        finally
        {
            SynchronizeNumericText();
            ValidatePreferences();
            RefreshActivePreset();
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

    /// <summary>Whether a finished operation also shows a Windows notification while the window is not active.</summary>
    [ObservableProperty]
    public partial bool ShowCompletionNotifications { get; set; } = true;

    partial void OnShowCompletionNotificationsChanged(bool value) => TriggerAutoSave();

    /// <summary>Whether each media list row has a check box; otherwise the list selects rows as File Explorer does.</summary>
    [ObservableProperty]
    public partial bool ShowSelectionCheckBoxes { get; set; }

    partial void OnShowSelectionCheckBoxesChanged(bool value) => TriggerAutoSave();

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
    [NotifyPropertyChangedFor(nameof(IsComplexFelLossPermanent))]
    public partial bool ReplaceOriginal { get; set; }

    partial void OnReplaceOriginalChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial int FolderSearchDepth { get; set; } = 100;

    partial void OnFolderSearchDepthChanged(int value) => NumericPreferenceChanged(nameof(FolderSearchDepthText));

    [RelayCommand]
    private void ResetFolderSearchDepth() => FolderSearchDepthText = "100";

    [ObservableProperty]
    public partial bool SafeMode { get; set; }

    partial void OnSafeModeChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial bool RemoveDolbyVision { get; set; }

    partial void OnRemoveDolbyVisionChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComplexFelLossPermanent))]
    public partial bool CreateArchive { get; set; }

    partial void OnCreateArchiveChanged(bool value) => TriggerAutoSave();

    public sealed record NumberChoice(int Value, string Label);
    public NumberChoice[] ConversionWorkerChoices => NumberChoices(Enumerable.Range(1, 16), settings.Hardware.RecommendedConversionWorkers);
    public NumberChoice[] AnalysisWorkerChoices => NumberChoices(Enumerable.Range(1, 10), settings.Hardware.RecommendedAnalysisWorkers);
    private static NumberChoice[] NumberChoices(IEnumerable<int> values, int recommended) =>
        values.Select(value => new NumberChoice(value, value == recommended ? $"{value} (Recommended)" : value.ToString())).ToArray();
    public string HardwareSummary => settings.Hardware.PhysicalCoresDetected
        ? $"{settings.Hardware.PhysicalCores} physical CPU cores; {settings.Hardware.LogicalProcessors} logical processors available."
        : $"Physical core detection unavailable. Using a conservative estimate of {settings.Hardware.PhysicalCores} cores; {settings.Hardware.LogicalProcessors} logical processors available.";
    public string ConcurrencyRecommendation => $"Recommended: {settings.Hardware.RecommendedConversionWorkers} simultaneous conversions and {settings.Hardware.RecommendedAnalysisWorkers} scan samples per file. These CPU-based values are starting points. Use fewer workers on mechanical drives or when disk throughput falls.";
    public sealed record PriorityChoice(ToolProcessPriority Value, string Label);
    public PriorityChoice[] ToolPriorityChoices { get; } =
    [
        new(ToolProcessPriority.Idle, "Idle"),
        new(ToolProcessPriority.BelowNormal, "Below normal"),
        new(ToolProcessPriority.Normal, "Normal"),
        new(ToolProcessPriority.AboveNormal, "Above normal")
    ];
    private const int RecommendedPipeBufferKiB = 4096;
    public NumberChoice[] PipeBufferChoices { get; } = NumberChoices([64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536], RecommendedPipeBufferKiB);

    [ObservableProperty]
    public partial int AnalysisWorkers { get; set; }
    partial void OnAnalysisWorkersChanged(int value) => TriggerAutoSave();

    [ObservableProperty]
    public partial ToolProcessPriority ToolProcessPriority { get; set; } = ToolProcessPriority.BelowNormal;
    partial void OnToolProcessPriorityChanged(ToolProcessPriority value) => TriggerAutoSave();

    [ObservableProperty]
    public partial int PipeBufferKiB { get; set; } = RecommendedPipeBufferKiB;
    partial void OnPipeBufferKiBChanged(int value) => TriggerAutoSave();

    [ObservableProperty]
    public partial int ConversionWorkers { get; set; }

    partial void OnConversionWorkersChanged(int value) => TriggerAutoSave();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerificationTitle))]
    [NotifyPropertyChangedFor(nameof(VerificationDescription))]
    public partial int VerificationLevel { get; set; } = 3;

    partial void OnVerificationLevelChanged(int value) => TriggerAutoSave();

    public string VerificationTitle => VerificationLevel switch
    {
        1 => "1 · No integrity check",
        2 => "2 · Frame count",
        3 => "3 · Frame count + mismatch recount",
        4 => "4 · Full integrity",
        _ => "Select a verification level"
    };

    public string VerificationDescription => VerificationLevel switch
    {
        1 => "Publish the conversion without checking its content. Fastest, but an incomplete output can replace the original.",
        2 => "Compare recorded source and output frame counts. Missing or unequal counts fail verification.",
        3 => "Compare recorded frame counts. If they differ, recount the original video before deciding. This is the default.",
        4 => "Check container metadata, retained tracks, frame counts, Dolby Vision metadata, and base-layer video content. Slowest and most thorough.",
        _ => ""
    };

    [ObservableProperty]
    public partial bool RecycleDeletedOriginalsAndBackups { get; set; } = true;

    partial void OnRecycleDeletedOriginalsAndBackupsChanged(bool value) => TriggerAutoSave();

    [ObservableProperty]
    public partial double FreeSpaceMarginPercent { get; set; } = 20;

    partial void OnFreeSpaceMarginPercentChanged(double value) => NumericPreferenceChanged(nameof(FreeSpaceMarginText));

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
                OnPropertyChanged(nameof(IsComplexFelLossPermanent));
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
                OnPropertyChanged(nameof(IsComplexFelLossPermanent));
                TriggerAutoSave();
            }
        }
    }

    // Replacing a converted Complex FEL original without an enhancement-layer backup leaves nothing Restore can rebuild.
    public bool IsComplexFelLossPermanent => ForceComplex && ReplaceOriginal && !CreateArchive;

    private void TriggerAutoSave()
    {
        ValidatePreferences();
        RefreshActivePreset();
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
        ValidatePreferences();
        if (HasErrors)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validationErrors.Values));
        }

        // Capture one consistent UI snapshot before moving directory validation
        // and persistence onto the worker thread.
        var snapshot = CreateSnapshot();
        await Task.Run(() => settings.SetPreferencesAsync(
            snapshot.TemporaryDirectory,
            snapshot.OutputDirectory,
            snapshot.ReplaceOriginal,
            snapshot.CreateElArchive,
            token,
            snapshot.AutomaticallyScanAddedFiles,
            snapshot.UseCachedResults,
            snapshot.Theme,
            includeSimple: snapshot.IncludeSimple,
            forceComplex: snapshot.ForceComplex,
            showCompletionNotifications: snapshot.ShowCompletionNotifications,
            conversionWorkers: snapshot.ConversionWorkers,
            verificationLevel: snapshot.VerificationLevel,
            recycleDeletedOriginalsAndBackups: snapshot.RecycleDeletedOriginalsAndBackups,
            freeSpaceMarginPercent: snapshot.FreeSpaceMarginPercent,
            analysisWorkers: snapshot.AnalysisWorkers,
            toolProcessPriority: snapshot.ToolProcessPriority,
            pipeBufferKiB: snapshot.PipeBufferKiB, brightnessMarginNits: snapshot.BrightnessMarginNits, assumedPeakBrightnessNits: snapshot.AssumedPeakBrightnessNits,
            scanSampleCount: snapshot.ScanSampleCount,
            scanSampleSeconds: snapshot.ScanSampleSeconds,
            minimumScanCoveragePercent: snapshot.MinimumScanCoveragePercent,
            inspectionPeakPercentile: snapshot.InspectionPeakPercentile,
            safeMode: snapshot.SafeMode,
            removeDolbyVision: snapshot.RemoveDolbyVision,
            folderSearchDepth: snapshot.FolderSearchDepth,
            showSelectionCheckBoxes: snapshot.ShowSelectionCheckBoxes), token);

        lastSavedSettings = snapshot;
        SetStatus(ViewStatus.SettingsSaved);
        Saved?.Invoke(snapshot);
    }

    private UserSettings CreateSnapshot() => new()
    {
        TemporaryDirectory = Temporary,
        OutputDirectory = OtherFolder ? Destination : null,
        ReplaceOriginal = ReplaceOriginal,
        CreateElArchive = CreateArchive,
        SafeMode = SafeMode,
        FolderSearchDepth = FolderSearchDepth,
        RemoveDolbyVision = RemoveDolbyVision,
        ConversionWorkers = ConversionWorkers,
        ScanSampleCount = ScanSampleCount,
        ScanSampleSeconds = ScanSampleSeconds,
        MinimumScanCoveragePercent = MinimumScanCoveragePercent,
        InspectionPeakPercentile = InspectionPeakPercentile,
        BrightnessMarginNits = BrightnessMarginNits,
        AssumedPeakBrightnessNits = AssumedPeakBrightnessNits,
        AnalysisWorkers = AnalysisWorkers,
        ToolProcessPriority = ToolProcessPriority,
        PipeBufferKiB = PipeBufferKiB,
        VerificationLevel = (Application.Settings.VerificationLevel)VerificationLevel,
        RecycleDeletedOriginalsAndBackups = RecycleDeletedOriginalsAndBackups,
        FreeSpaceMarginPercent = FreeSpaceMarginPercent,
        AutomaticallyScanAddedFiles = AutomaticallyScanAddedFiles,
        UseCachedResults = UseCachedResults,
        ShowCompletionNotifications = ShowCompletionNotifications,
        ShowSelectionCheckBoxes = ShowSelectionCheckBoxes,
        Theme = SelectedTheme,
        IncludeSimple = IncludeSimple,
        ForceComplex = ForceComplex
    };

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

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Load() => RunAsync(async (token, _) =>
    {
        await FlushAsync(token);
        lastSavedSettings = await settings.ReadAsync(token);
        lastAppliedPreset = lastSavedSettings.LastAppliedPreset;
        ApplyPreferences(lastSavedSettings);
        await RefreshLibrarySizeAsync(token);
        SetStatus(ViewStatus.SettingsLoaded);
        await LoadPresetsAsync(token);
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Save() => RunAsync(async (token, _) => await SaveAsync(token));

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task CleanLibrary() => RunAsync(async (token, _) =>
    {
        int removed = await Task.Run(() => libraryService.CleanMissingAsync(token), token);
        await RefreshLibrarySizeAsync(token);
        SetStatus(ViewStatus.Result, $"Removed {removed} missing file record{(removed == 1 ? "" : "s")} from the library. Media files and frame previews were not changed.");
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task DeleteLibrary() => RunAsync(async (token, _) =>
    {
        int removed = await Task.Run(() => library.DeleteAsync(token), token);
        LibraryDeleted?.Invoke();
        await RefreshLibrarySizeAsync(token);
        SetStatus(ViewStatus.Result, $"Deleted {removed} library record{(removed == 1 ? "" : "s")}, saved analyses and history. The current list was cleared. Media files and frame previews were not changed.");
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
    private void OpenLibraryFolder() => explorer.OpenFolder(LibraryDirectory);

    [RelayCommand]
    private void OpenLogs() => explorer.OpenLogs();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task ExportDiagnostics()
    {
        string? path = files.PickSaveFile("Diagnostic ZIP|*.zip", "diagnostics.zip");
        if (path is null)
        {
            return Task.CompletedTask;
        }
        return RunAsync("Diagnostic export", async (token, _) =>
        {
            // An invalid settings file must not prevent exporting the evidence needed to repair it.
            var result = await Task.Run(() => diagnostics.ExportAsync(path, token), token);
            if (result.Status == DoViFixer.Application.Operations.OperationStatus.Partial)
            {
                ReportItemFailed();
            }
            SetStatus(ViewStatus.Result, result.Message);
        });
    }

    protected override void CommandsChanged()
    {
        LoadCommand?.NotifyCanExecuteChanged();
        SaveCommand?.NotifyCanExecuteChanged();
        DiscardChangesCommand?.NotifyCanExecuteChanged();
        CleanLibraryCommand?.NotifyCanExecuteChanged();
        DeleteLibraryCommand?.NotifyCanExecuteChanged();
        CheckCommand?.NotifyCanExecuteChanged();
        InstallCommand?.NotifyCanExecuteChanged();
        SetToolCommand?.NotifyCanExecuteChanged();
        ResetToolCommand?.NotifyCanExecuteChanged();
        ExportDiagnosticsCommand?.NotifyCanExecuteChanged();
        ApplyPresetCommand?.NotifyCanExecuteChanged();
        SavePresetCommand?.NotifyCanExecuteChanged();
        DeletePresetCommand?.NotifyCanExecuteChanged();
    }
}
