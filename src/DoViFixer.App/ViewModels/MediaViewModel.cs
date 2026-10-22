using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Library;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Reports;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using Microsoft.Extensions.Logging;
using WpfFoundation.Operations;

namespace DoViFixer.App.ViewModels;
public sealed partial class MediaViewModel : OperationViewModel, INavigationPage, IPageActivation, IDisposable
{
    public string NavigationName => "Media";
    public string? NavigationIcon => "\uE714";
    public override IReadOnlyList<StatusItem> StatusItems =>
    [
        new(SelectionSummary),
        new(StatusText),
        new(OutputSummary, OutputSummaryToolTip, Command: OpenSettingsCommand),
        new(RetentionSummary, IsEmphasized: IsReplaceOriginalActive),
        new(FelSummary, convertsComplexFel ? ComplexFelWarning : null, IsEmphasized: convertsComplexFel),
        new(ArchiveSummary)
    ];

    private readonly IFileDiscovery discovery;
    private readonly InspectionService inspection;
    private readonly MediaLibraryService library;
    private bool libraryLoaded;
    private bool loadingLibraryRows;
    private readonly ConversionService conversion;
    private readonly ConversionDiskBudget diskBudget;
    private readonly RestoreService restore;
    private readonly CleanupService cleanup;
    private readonly ControlledBatchService batch;
    private readonly SettingsService settings;
    private readonly DependencySetup dependencies;
    private readonly IDialogService dialogs;
    private readonly IFileDialogService files;
    private readonly IFileExplorer explorer;
    private readonly ITextClipboard clipboard;
    private readonly ILogger<MediaViewModel> logger;
    private BatchControl? control;
    private readonly IMediaPreview mediaPreview;
    private CancellationTokenSource? previewCancellation;
    private CancellationTokenSource? backgroundPreviewCancellation;
    private Task previewCompletion = Task.CompletedTask;
    private readonly ISourceFileMonitor sourceFiles;
    private readonly ReportService reports;
    private readonly SynchronizationContext? uiContext;
    private int availabilityCheckQueued;

    private static string Summary(BatchResult result) => string.Join(" · ", result.Items.GroupBy(r => r.Status).Select(g => $"{g.Count()} {g.Key}"));
    private string TimedSummary(BatchResult result) => $"{Summary(result)}{(result.Status == OperationStatus.Completed ? " in " : " · Finished in ")}{BatchProgress.BatchElapsed}";
    public MediaViewModel(IFileDiscovery discovery, InspectionService inspection, ConversionService conversion, ConversionDiskBudget diskBudget, ControlledBatchService batch, SettingsService settings, DependencySetup dependencies, IDialogService dialogs, IFileDialogService files, IFileExplorer explorer, ILogger<MediaViewModel> logger, IMediaPreview mediaPreview, RestoreService restore, ISourceFileMonitor sourceFiles, ReportService reports, MediaLibraryService library, CleanupService cleanup, ITextClipboard clipboard, IOperationFeedback? feedback = null)
        : base(feedback)
    {
        this.discovery = discovery;
        this.diskBudget = diskBudget;
        this.sourceFiles = sourceFiles;
        this.reports = reports;
        this.library = library;
        uiContext = SynchronizationContext.Current;
        sourceFiles.Changed += OnSourceFilesChanged;
        this.inspection = inspection;
        this.conversion = conversion;
        this.restore = restore;
        this.cleanup = cleanup;
        this.batch = batch;
        this.settings = settings;
        this.dependencies = dependencies;
        this.dialogs = dialogs;
        this.files = files;
        this.explorer = explorer;
        this.clipboard = clipboard;
        this.logger = logger;
        this.mediaPreview = mediaPreview;

        FilterOptions =
        [
            new(MediaFileFilter.All, "All", filter => FileFilter = filter) { IsSelected = true },
            new(MediaFileFilter.Profile7, "Profile 7", filter => FileFilter = filter),
            new(MediaFileFilter.Profile81, "Profile 8.1", filter => FileFilter = filter),
            new(MediaFileFilter.OtherProfiles, "Other DV profiles", filter => FileFilter = filter),
            new(MediaFileFilter.NoDolbyVision, "Not Dolby Vision", filter => FileFilter = filter),
            new(MediaFileFilter.NotScanned, "Needs scan", filter => FileFilter = filter)
        ];
        Files.CollectionChanged += (_, e) =>
        {
            foreach (var row in e.NewItems?.OfType<MediaRow>() ?? [])
            {
                ApplyFelSettings(row);
            }

            if (loadingLibraryRows)
            {
                return;
            }
            WatchFiles();
            OnPropertyChanged(nameof(IsFileListEmpty));
            OnPropertyChanged(nameof(HasMissingFiles));
            ShownFilesChanged();
        };
        FileSort = new((column, direction) => new MediaRowComparer((MediaSortColumn)column, direction), () => IsIdle);
        FileSort.PropertyChanged += OnFileSortChanged;
        BatchProgress.PropertyChanged += (_, e) =>
        {
            NotifyActiveProgress();
            if (e.PropertyName is nameof(BatchProgress.Percent) or nameof(BatchProgress.IsRunning))
            {
                ReportActivityProgress();
            }
        };
        Progress.PropertyChanged += (_, _) => NotifyActiveProgress();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectionSummary))
            {
                OnPropertyChanged(nameof(StatusItems));
            }

            if (e.PropertyName is nameof(StatusMessage) or nameof(IsBusy))
            {
                NotifyActiveProgress();
            }
        };
    }
    public ObservableCollection<MediaRow> Files { get; } = [];
    public bool IsFileListEmpty => Files.Count == 0;

    /// <summary>
    /// Filter choices above the list. Select all, Rescan, Inspect and Convert act only on the files the filter shows,
    /// so a hidden file is never processed.
    /// </summary>
    public IReadOnlyList<MediaFilterOption> FilterOptions { get; }

    [ObservableProperty]
    public partial MediaFileFilter FileFilter { get; set; } = MediaFileFilter.All;

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    partial void OnFileFilterChanged(MediaFileFilter value)
    {
        foreach (var option in FilterOptions)
        {
            option.IsSelected = option.Filter == value;
        }

        ApplyFilter();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FileFilter;
        var text = FilterText.Trim();
        // Each filter change gets its own predicate instance, so the list sees a new value and filters again.
        ShownFilePredicate = item => item is MediaRow row && Shows(filter, text, row);
        OnPropertyChanged(nameof(ShownFilePredicate));
        if (Focused is null || !IsShown(Focused))
        {
            Focused = ShownFiles.FirstOrDefault();
        }

        ShownFilesChanged();
    }

    /// <summary>The list's filter; rows are filtered again live as their <see cref="MediaRow.FilterGroup"/> changes.</summary>
    public Predicate<object> ShownFilePredicate { get; private set; } = _ => true;

    /// <summary>Files exist, but the chosen filter shows none of them.</summary>
    public bool HasNoShownFiles => Files.Count > 0 && !Files.Any(IsShown);

    private static bool MatchesText(string text, MediaRow row) =>
        string.IsNullOrWhiteSpace(text) || row.Name.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool Shows(MediaFileFilter filter, string text, MediaRow row) =>
        (filter == MediaFileFilter.All || row.FilterGroup == filter) && MatchesText(text, row);

    private bool IsShown(MediaRow row) => Shows(FileFilter, FilterText.Trim(), row);

    private IEnumerable<MediaRow> ShownFiles => Files.Where(IsShown);

    private void ShownFilesChanged()
    {
        var text = FilterText.Trim();
        var matching = Files.Where(row => MatchesText(text, row)).ToArray();
        var groups = matching.CountBy(row => row.FilterGroup).ToDictionary();
        foreach (var option in FilterOptions)
        {
            option.Count = option.Filter == MediaFileFilter.All ? matching.Length : groups.GetValueOrDefault(option.Filter);
        }

        OnPropertyChanged(nameof(HasNoShownFiles));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllFilesSelected));
        CommandsChanged();
    }
    public BatchProgressViewModel BatchProgress { get; } = new();
    private bool CanPauseBatch() => BatchProgress.IsRunning;

    [RelayCommand(CanExecute = nameof(CanPauseBatch))]
    private void PauseBatch()
    {
        if (control?.IsPaused == true)
        {
            control.Resume();
        }
        else
        {
            control?.Pause();
        }

        SetActivityPaused(control?.IsPaused == true);
        NotifyActiveProgress();
    }
    public string PauseBatchText => control?.IsPaused == true ? "Resume" : "Pause";
    public string PauseBatchIcon => control?.IsPaused == true ? "\uE768" : "\uE769";
    public string PauseBatchToolTip => control?.IsPaused == true ? "Resume the batch" : "Pause before the next job starts";

    public double ActiveProgressPercent => BatchProgress.IsRunning ? BatchProgress.Percent : Percent;
    public bool ActiveProgressIndeterminate => !BatchProgress.IsRunning && IsIndeterminate;
    public string ActiveProgressTitle => BatchProgress.IsRunning
        ? BatchProgress.Operation
        : (string.IsNullOrWhiteSpace(Stage) ? "Working…" : Stage);
    public string ActiveProgressText => BatchProgress.IsRunning
        ? $"{BatchProgress.Percent:0}%"
        : Progress.ProgressText;
    public string ActiveProgressSummary => BatchProgress.IsRunning
        ? control?.IsPaused == true
            ? $"{BatchProgress.Summary} · {(BatchProgress.ActiveCount == 0 ? "Paused" : BatchProgress.ActiveCount == 1 ? "Pausing after current job" : "Pausing after active jobs")}"
            : BatchProgress.Summary
        : StatusMessage;
    public string? ActiveProgressToolTip => BatchProgress.IsRunning
        ? "Processed includes completed, failed, cancelled and skipped files. Each file has equal weight in the batch."
        : null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenOutputCommand), nameof(OpenFileLocationCommand), nameof(CopyDetailsCommand))]
    public partial MediaRow? Focused { get; set; }

    partial void OnFocusedChanged(MediaRow? value) => previewCompletion = Task.WhenAll(previewCompletion, LoadPreviewAsync(value), LoadLibraryHistoryAsync(value));

    [ObservableProperty]
    public partial BitmapSource? FramePreview { get; private set; }

    [ObservableProperty]
    public partial string PreviewStatus { get; private set; } = "";

    private async Task<BitmapSource> LoadBitmapAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await mediaPreview.LoadAsync(path, cancellationToken);
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private async Task LoadPreviewAsync(MediaRow? row)
    {
        previewCancellation?.Cancel();
        previewCancellation = null;
        FramePreview = null;
        PreviewStatus = "";
        if (row is null)
        {
            return;
        }

        if (row.IsMissing)
        {
            PreviewStatus = MissingPreviewStatus;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        previewCancellation = cancellation;
        PreviewStatus = "Loading frame preview…";
        try
        {
            var image = await Task.Run(() => LoadBitmapAsync(row.Path, cancellation.Token), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                FramePreview = image;
                PreviewStatus = "";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Frame preview unavailable for {Input}", row.Path);
            if (!cancellation.IsCancellationRequested)
            {
                PreviewStatus = sourceFiles.Exists(row.Path)
                    ? "Frame preview unavailable. Check media tools in Settings or try selecting the file again."
                    : MissingPreviewStatus;
            }
        }
        finally
        {
            if (ReferenceEquals(previewCancellation, cancellation))
            {
                previewCancellation = null;
            }
        }
    }

    private void StartBackgroundPreviews(bool refreshFocused = false)
    {
        if (refreshFocused)
        {
            previewCompletion = Task.WhenAll(previewCompletion, LoadPreviewAsync(Focused));
        }
        previewCompletion = Task.WhenAll(previewCompletion, GeneratePreviewsAsync(InDisplayOrder(Files)));
    }

    private async Task GeneratePreviewsAsync(MediaRow[] rows)
    {
        backgroundPreviewCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        backgroundPreviewCancellation = cancellation;
        try
        {
            foreach (var row in rows)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!Files.Contains(row) || row.IsMissing)
                {
                    continue;
                }
                try
                {
                    // Fill the disk cache one file at a time without entering the busy operation flow.
                    await Task.Run(() => mediaPreview.LoadAsync(row.Path, cancellation.Token), cancellation.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellation.IsCancellationRequested)
                {
                    logger.LogDebug(ex, "Background frame preview unavailable for {Input}", row.Path);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(backgroundPreviewCancellation, cancellation))
            {
                backgroundPreviewCancellation = null;
            }
        }
    }

    public Task CancelPreviewsAsync()
    {
        CancelPreviews();
        PreviewStatus = "";
        return previewCompletion;
    }

    private void CancelPreviews()
    {
        previewCancellation?.Cancel();
        backgroundPreviewCancellation?.Cancel();
    }

    public void Dispose()
    {
        FileSort.PropertyChanged -= OnFileSortChanged;
        sourceFiles.Changed -= OnSourceFilesChanged;
        CancelPreviews();
    }

    private const string MissingPreviewStatus = "File not found.";

    public bool HasMissingFiles => Files.Any(row => row.IsMissing);

    /// <summary>
    /// Re-checks every listed file, for example when the window is activated, because folder watchers miss
    /// changes on some network shares, removed drives and folders that were themselves moved.
    /// </summary>
    public void CheckSourceFiles()
    {
        WatchFiles();
        RefreshSourceAvailability();
    }

    // Archives usually sit beside their media, but an output folder can hold them elsewhere.
    private void WatchFiles() => sourceFiles.Watch(Files.Select(row => row.Path).Concat(Files.Select(row => row.RestoreArchive).OfType<string>()));

    private void OnSourceFilesChanged(object? sender, EventArgs e)
    {
        // Coalesce bursts, such as a whole folder being deleted, into one check on the UI thread.
        if (Interlocked.Exchange(ref availabilityCheckQueued, 1) == 1)
        {
            return;
        }

        if (uiContext is null)
        {
            RunQueuedAvailabilityCheck();
        }
        else
        {
            uiContext.Post(_ => RunQueuedAvailabilityCheck(), null);
        }
    }

    private void RunQueuedAvailabilityCheck()
    {
        Interlocked.Exchange(ref availabilityCheckQueued, 0);
        RefreshSourceAvailability();
    }

    private void RefreshSourceAvailability()
    {
        bool changed = false;
        foreach (var row in Files)
        {
            // A running job holds its source open, and Replace original moves it away on purpose.
            if (row.IsActive)
            {
                continue;
            }

            // A deleted or moved archive can no longer restore the row; PairRestoreArchives pairs it again when it returns.
            if (row.RestoreArchive is { } archive && !sourceFiles.Exists(archive))
            {
                row.RestoreArchive = null;
                changed = true;
            }

            bool missing = !sourceFiles.Exists(row.Path);
            if (missing == row.IsMissing)
            {
                continue;
            }

            if (missing && row.IsPending && control?.Skip(row.Path) == true)
            {
                row.SetSkipped(MediaRow.MissingNote);
            }

            row.IsMissing = missing;
            if (!missing && row.Notice == MediaRow.MissingNote)
            {
                row.Notice = "";
            }

            if (ReferenceEquals(row, Focused))
            {
                previewCompletion = Task.WhenAll(previewCompletion, LoadPreviewAsync(row));
            }

            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(HasMissingFiles));
            ConvertRowCommand.NotifyCanExecuteChanged();
            CommandsChanged();
        }

        PairRestoreArchives();
    }

    /// <summary>Completes when the background pairing of restored or newly added .dovi archives finishes.</summary>
    public Task ArchivePairing { get; private set; } = Task.CompletedTask;

    private bool archivePairingQueued;

    // A .dovi archive that returns, for example from the Recycle Bin, offers Restore again without a rescan.
    // Runs one pass at a time; a change during a pass queues one more.
    private void PairRestoreArchives()
    {
        if (archivePairingQueued)
        {
            return;
        }

        archivePairingQueued = true;
        ArchivePairing = PairRestoreArchivesAfterAsync(ArchivePairing);
    }

    private async Task PairRestoreArchivesAfterAsync(Task previous)
    {
        await previous;
        archivePairingQueued = false;
        // Operations pair the rows they analyze or produce; the next change retries what this pass skipped.
        if (!IsIdle)
        {
            return;
        }

        var rows = Files.Where(row => row.RestoreArchive is null && !row.IsMissing && !row.IsActive
            && row.Analysis?.Media is { } media && RestoreService.IsRestoreBase(media)).ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var pairing = BeginArchivePairing();
        foreach (var row in rows)
        {
            if (!IsIdle || !Files.Contains(row) || row.RestoreArchive is not null)
            {
                continue;
            }

            await RefreshRestoreArchiveAsync(row, pairing, CancellationToken.None);
        }
    }

    private static OperationItemResult MissingSource(MediaRow row) => new(row.Path, OperationStatus.Skipped, null, MediaRow.MissingNote);

    public string SelectionSummary
    {
        get
        {
            var total = Files.Count;
            var selected = ShownFiles.Count(f => f.IsSelected);
            var totalText = FileFilter == MediaFileFilter.All && string.IsNullOrWhiteSpace(FilterText)
                ? $"{total} {(total == 1 ? "item" : "items")}"
                : $"{ShownFiles.Count()} of {total} {(total == 1 ? "item" : "items")} shown";
            if (selected == 0)
            {
                return totalText;
            }

            var selectedText = $"{selected} {(selected == 1 ? "item" : "items")} selected";
            return $"{totalText} | {selectedText}";
        }
    }
    public bool? AllFilesSelected => !ShownFiles.Any(row => row.IsSelected) ? false : ShownFiles.All(row => row.IsSelected) ? true : null;

    /// <summary>
    /// Display order applied by the list's collection view; <see cref="Files"/> keeps the order files were added.
    /// Sorting is blocked while busy because a running batch keeps the order it started with.
    /// </summary>
    public ColumnSort<MediaRow> FileSort { get; }
    public bool CanInspect => CanInspectChecked();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task AddFiles() => AddAsync(files.PickFiles("Matroska media|*.mkv"));

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task AddFolder() => AddAsync(files.PickFolder() is { } folder ? [folder] : []);

    private bool CanExportResults() => IsIdle && ShownFiles.Any(row => row.IsSelected);

    /// <summary>Exports the checked files the current filter shows.</summary>
    [RelayCommand(CanExecute = nameof(CanExportResults))]
    private Task ExportResults() => ExportRows(ShownFiles.Where(row => row.IsSelected));

    private Task ExportRows(IEnumerable<MediaRow> rows)
    {
        string? destination = files.PickSaveFile("CSV report|*.csv|JSON report|*.json|Text report|*.txt", "media-report.csv");
        if (destination is null)
        {
            return Task.CompletedTask;
        }

        var snapshot = reports.CreateSnapshot(InDisplayOrder(rows).Select(row => new ReportInput(row.Path,
            row.IsMissing ? MediaRow.MissingStatus : row.State.ToString(), row.Analysis, row.AnalysisError, row.Result)));
        return RunAsync("Report export", async (token, _) =>
        {
            await reports.ExportAsync(destination, snapshot, token);
            SetStatus(ViewStatus.Result, $"Exported {snapshot.Entries.Count} results to {destination}.");
        });
    }

    private bool CanAddDroppedPaths(string[]? paths) => IsIdle && paths is not null && paths.Any(discovery.IsSupportedInput);

    [RelayCommand(CanExecute = nameof(CanAddDroppedPaths))]
    private Task AddDroppedPaths(string[]? paths) => AddAsync((paths ?? []).Where(discovery.IsSupportedInput));

    [RelayCommand(CanExecute = nameof(CanScan))]
    private Task Scan() => ScanAllAsync();

    [RelayCommand(CanExecute = nameof(CanInspectChecked))]
    private Task Inspect() => AnalyzeAsync(AnalysisMethod.FullRpu);

    [RelayCommand(CanExecute = nameof(CanInspectChecked))]
    private Task DeepInspect() => AnalyzeAsync(AnalysisMethod.DeepInspection);

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task Convert() => ConvertBatchAsync();

    private bool CanConvertRow(MediaRow? row) => row is not null && IsIdle && Files.Contains(row) && row.IsProfile7 && !row.IsMissing;

    [RelayCommand(CanExecute = nameof(CanConvertRow))]
    private Task ConvertRow(MediaRow? row) => ConvertBatchAsync([row!]);

    private bool CanRestoreRow(MediaRow? row) => row is not null && IsIdle && Files.Contains(row) && row.CanRestore;

    [RelayCommand(CanExecute = nameof(CanRestoreRow))]
    private Task RestoreRow(MediaRow? row) => RestoreRowsAsync([row!]);

    private bool CanRestoreSelected() => IsIdle && ShownFiles.Any(row => row.IsSelected && row.CanRestore);

    /// <summary>Restores every selected file that has a matching .dovi archive, after one review of all of them.</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private Task RestoreSelected() => RestoreRowsAsync(InDisplayOrder(ShownFiles.Where(row => row.IsSelected && row.CanRestore)));

    /// <summary>
    /// Whether the Inspect split button runs a deep inspection. Picking an entry in its menu runs that inspection
    /// and makes it what the button runs next.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDeepInspectionChosen { get; set; }

    [RelayCommand(CanExecute = nameof(CanInspectChecked))]
    private Task ChooseInspect() => ChooseAndInspectAsync(deep: false);

    [RelayCommand(CanExecute = nameof(CanInspectChecked))]
    private Task ChooseDeepInspect() => ChooseAndInspectAsync(deep: true);

    private static bool CanCancelFile(MediaRow? row) => row is not null && row.IsActive && !row.IsCancellationRequested;

    [RelayCommand(CanExecute = nameof(CanCancelFile))]
    private void CancelFile(MediaRow? row)
    {
        if (row is not null && row.IsActive && !row.IsCancellationRequested && control is not null)
        {
            row.IsCancellationRequested = true;
            row.Status = "Cancelling…";
            row.Progress.Update("Cancelling…", null);
            control.Cancel(row.Path);
        }
    }

    private bool CanOpenOutput() => Focused?.CanOpenResult == true;

    [RelayCommand(CanExecute = nameof(CanOpenOutput))]
    private void OpenOutput() => explorer.ShowInFolder(Focused!.Result!.Output!);

    private bool CanOpenFileLocation() => Focused is not null;

    [RelayCommand(CanExecute = nameof(CanOpenFileLocation))]
    private void OpenFileLocation() => explorer.ShowInFolder(Focused!.Path);

    [RelayCommand(CanExecute = nameof(CanOpenFileLocation))]
    private void CopyDetails() => clipboard.SetText(Focused!.DetailsText);

    private static bool CanShowResultFile(string? path) => !string.IsNullOrEmpty(path);

    [RelayCommand(CanExecute = nameof(CanShowResultFile))]
    private void ShowResultFile(string? path) => explorer.ShowInFolder(path!);

    private bool CanRetryAnalysis(MediaRow? row) => row is not null && IsIdle && Files.Contains(row) && row.CanRetryAnalysis && row.LastAnalysisMethod is not null;

    [RelayCommand(CanExecute = nameof(CanRetryAnalysis))]
    private Task RetryAnalysis(MediaRow? row) => RunAsync(AnalysisTitle(row!.LastAnalysisMethod!.Value), (token, _) => AnalyzeRowsAsync([row!], row!.LastAnalysisMethod!.Value, token));

    private bool CanScanRow(MediaRow? row) => row is not null && IsIdle && Files.Contains(row) && !row.IsMissing;

    [RelayCommand(CanExecute = nameof(CanScanRow))]
    private Task ScanRow(MediaRow? row) => RunAsync(AnalysisTitle(AnalysisMethod.SampledRpu), (token, _) => AnalyzeRowsAsync([row!], AnalysisMethod.SampledRpu, token));

    private bool CanInspectRow(MediaRow? row) => CanScanRow(row) && IsInspectable(row!);

    // The row context menu inspects one file whatever its scan found, like Scan does for one file.
    [RelayCommand(CanExecute = nameof(CanInspectRow))]
    private Task InspectRow(MediaRow? row) => RunAsync(AnalysisTitle(AnalysisMethod.FullRpu), (token, _) => AnalyzeRowsAsync([row!], AnalysisMethod.FullRpu, token));

    [RelayCommand(CanExecute = nameof(CanInspectRow))]
    private Task DeepInspectRow(MediaRow? row) => RunAsync(AnalysisTitle(AnalysisMethod.DeepInspection), (token, _) => AnalyzeRowsAsync([row!], AnalysisMethod.DeepInspection, token));

    private bool CanInspectIncomplete(MediaRow? row) => row is not null && IsIdle && Files.Contains(row) && row.CanInspectIncomplete;

    [RelayCommand(CanExecute = nameof(CanInspectIncomplete))]
    private Task InspectIncomplete(MediaRow? row) => RunAsync(AnalysisTitle(AnalysisMethod.FullRpu), (token, _) => AnalyzeRowsAsync([row!], AnalysisMethod.FullRpu, token));

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenSettings() => RequestOpenSettings?.Invoke();

    public event Action? RequestOpenSettings;
    public event Func<CancellationToken, Task>? ConversionStarting;

    [ObservableProperty]
    public partial string OutputSummary { get; private set; } = "Output: Same folder";

    [ObservableProperty]
    public partial string OutputSummaryToolTip { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsReplaceOriginalActive { get; private set; }

    [ObservableProperty]
    public partial string RetentionSummary { get; private set; } = "Keep originals";

    [ObservableProperty]
    public partial string FelSummary { get; private set; } = "FEL: Skip";

    [ObservableProperty]
    public partial string ArchiveSummary { get; private set; } = "EL archive: Off";

    private const string ComplexFelWarning = "Complex FEL conversion is allowed: enhancement-layer picture data that expands dynamic range will be permanently lost.";

    // The FEL settings, so rows offer Convert only where conversion would not skip them.
    private bool convertsSimpleFel;
    private bool convertsComplexFel;

    private void ApplyFelSettings(MediaRow row)
    {
        row.ConvertsSimpleFel = convertsSimpleFel;
        row.ConvertsComplexFel = convertsComplexFel;
    }

    public void UpdateSettingsSummary(UserSettings s)
    {
        convertsSimpleFel = s.IncludeSimple;
        convertsComplexFel = s.ForceComplex;
        foreach (var row in Files)
        {
            ApplyFelSettings(row);
        }

        IsReplaceOriginalActive = s.ReplaceOriginal;
        RetentionSummary = s.ReplaceOriginal ? "⚠ Replace originals" : "Keep originals";
        FelSummary = (s.IncludeSimple, s.ForceComplex) switch
        {
            (true, true) => "⚠ FEL: Simple + Complex",
            (true, false) => "FEL: Simple only",
            (false, true) => "⚠ FEL: Complex only",
            _ => "FEL: Skip"
        };
        ArchiveSummary = s.CreateElArchive ? "EL archive: On" : "EL archive: Off";
        string destination = string.IsNullOrWhiteSpace(s.OutputDirectory)
            ? "Same folder"
            : s.OutputDirectory;

        OutputSummary = s.ReplaceOriginal
            ? $"Output: {destination} (Replace original)"
            : $"Output: {destination}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Target: {(s.RemoveDolbyVision ? "HDR10" : "Dolby Vision Profile 8.1")}");
        sb.AppendLine($"Safe mode: {(s.SafeMode ? "On (disk extraction)" : "Off (streaming with automatic disk fallback)")}");
        sb.AppendLine($"Destination: {(string.IsNullOrWhiteSpace(s.OutputDirectory) ? "Same folder as source file" : s.OutputDirectory)}");
        sb.AppendLine($"Retention: {(s.ReplaceOriginal ? "Replace original after conversion (Source file will be replaced!)" : "Keep original (Non-destructive)")}");
        sb.AppendLine($"Verification: level {(int)s.VerificationLevel}");
        if (s.ReplaceOriginal)
        {
            sb.AppendLine(s.RecycleDeletedOriginalsAndBackups ? "Original removal: Recycle Bin when available" : "Original removal: permanent deletion");
        }
        sb.AppendLine(FelSummary);
        if (s.IncludeSimple || s.ForceComplex)
        {
            sb.AppendLine("FEL conversion discards enhancement-layer picture data.");
        }
        if (s.CreateElArchive)
        {
            sb.AppendLine("EL Archive: Enabled (.dovi backup file will be created)");
        }

        if (!string.IsNullOrWhiteSpace(s.TemporaryDirectory))
        {
            sb.AppendLine($"Temporary directory: {s.TemporaryDirectory}");
        }

        sb.AppendLine();
        sb.Append("Click to view or change settings.");
        OutputSummaryToolTip = sb.ToString();
        OnPropertyChanged(nameof(StatusItems));
    }

    public async Task OnActivatedAsync(CancellationToken cancellationToken)
    {
        await RefreshSettingsSummaryAsync(cancellationToken);
        await LoadLibraryAsync(cancellationToken);
    }

    public Task LoadLibraryAsync(CancellationToken cancellationToken = default)
    {
        if (libraryLoaded)
        {
            return Task.CompletedTask;
        }
        return RunAsync("Loading library", async (operationToken, _) =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationToken);
            var entries = await Task.Run(() => library.LoadAsync(linked.Token), linked.Token);
            // Remembered Profile 8.1 files offer Restore again; pairing reads only cached hashes and archive manifests.
            var pairing = BeginArchivePairing();
            loadingLibraryRows = true;
            try
            {
                foreach (var entry in entries)
                {
                    if (Files.Any(row => row.LibraryFingerprint == entry.Fingerprint || string.Equals(row.Path, entry.Source.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    var row = new MediaRow(entry.Source.Path) { LibraryFingerprint = entry.Fingerprint, IsSelected = false };
                    if (entry.Analysis is not null)
                    {
                        row.Analysis = entry.Analysis;
                        row.SetScanned();
                    }
                    if (sourceFiles.Exists(row.Path))
                    {
                        await RefreshRestoreArchiveAsync(row, pairing, linked.Token);
                        if (await Task.Run(() => conversion.ReadPublishedResultAsync(row.Path, linked.Token), linked.Token) is { } published)
                        {
                            await ReadResultMetadataAsync(row, published, linked.Token);
                            row.SetConverted(published);
                        }
                    }
                    row.PropertyChanged += OnRowPropertyChanged;
                    Files.Add(row);
                }
            }
            finally
            {
                loadingLibraryRows = false;
                WatchFiles();
                OnPropertyChanged(nameof(IsFileListEmpty));
                OnPropertyChanged(nameof(HasMissingFiles));
                ShownFilesChanged();
            }
            RefreshSourceAvailability();
            Focused ??= Files.FirstOrDefault();
            libraryLoaded = true;
        });
    }

    private async Task LoadLibraryHistoryAsync(MediaRow? row)
    {
        if (row?.LibraryFingerprint is not { } fingerprint)
        {
            return;
        }
        try
        {
            var history = await Task.Run(() => library.ReadHistoryAsync(fingerprint, CancellationToken.None));
            row.LibraryHistory = string.Join(Environment.NewLine, history.Select(entry => $"{entry.When.ToLocalTime():g} · {LibraryActionText(entry.Action)}: {entry.Detail}"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read library history for {Path}", row.Path);
        }
    }

    private static string LibraryActionText(string action) => action switch
    {
        nameof(AnalysisMethod.SampledRpu) => "Scanned",
        nameof(AnalysisMethod.FullRpu) => "Inspected",
        nameof(AnalysisMethod.DeepInspection) => "Deep inspected",
        nameof(AnalysisMethod.MetadataOnly) => "Metadata read",
        _ => action
    };

    public async Task RefreshSettingsSummaryAsync(CancellationToken token = default)
    {
        try
        {
            var snapshot = await settings.ReadAsync(token);
            LoadColumnLayout(snapshot);
            UpdateSettingsSummary(snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read settings for summary.");
        }
    }

    private bool CanScan() => IsIdle && ShownFiles.Any(row => !row.IsMissing);
    private bool CanOperate() => IsIdle && ShownFiles.Any(f => f.IsSelected);

    /// <summary>
    /// Inspection reads the RPU stream of Profile 7 files only; for any other file it repeats the metadata read. A file
    /// not analyzed yet may still be Profile 7.
    /// </summary>
    private static bool IsInspectable(MediaRow row) => !row.IsMissing && (row.Analysis is null || row.IsProfile7);

    private bool CanInspectChecked() => IsIdle && ShownFiles.Any(row => row.IsSelected && IsInspectable(row));

    // A row's profile or presence decides whether it can be inspected.
    private void InspectCommandsChanged()
    {
        OnPropertyChanged(nameof(CanInspect));
        InspectCommand.NotifyCanExecuteChanged();
        DeepInspectCommand.NotifyCanExecuteChanged();
        ChooseInspectCommand.NotifyCanExecuteChanged();
        ChooseDeepInspectCommand.NotifyCanExecuteChanged();
        InspectRowCommand.NotifyCanExecuteChanged();
        DeepInspectRowCommand.NotifyCanExecuteChanged();
    }

    protected override void CommandsChanged()
    {
        ResetColumnLayoutCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanInspect));
        ScanRowCommand?.NotifyCanExecuteChanged();
        InspectRowCommand?.NotifyCanExecuteChanged();
        DeepInspectRowCommand?.NotifyCanExecuteChanged();
        RetryAnalysisCommand?.NotifyCanExecuteChanged();
        InspectIncompleteCommand?.NotifyCanExecuteChanged();
        ToggleSelectAllCommand?.NotifyCanExecuteChanged();
        RemoveSelectedCommand?.NotifyCanExecuteChanged();
        RemoveFileCommand?.NotifyCanExecuteChanged();
        RemoveMissingCommand?.NotifyCanExecuteChanged();
        CleanupBackupsCommand?.NotifyCanExecuteChanged();
        OpenSettingsCommand?.NotifyCanExecuteChanged();
        AddFilesCommand?.NotifyCanExecuteChanged();
        AddFolderCommand?.NotifyCanExecuteChanged();
        ExportResultsCommand?.NotifyCanExecuteChanged();
        AddDroppedPathsCommand?.NotifyCanExecuteChanged();
        ScanCommand?.NotifyCanExecuteChanged();
        InspectCommand?.NotifyCanExecuteChanged();
        DeepInspectCommand?.NotifyCanExecuteChanged();
        ChooseInspectCommand?.NotifyCanExecuteChanged();
        ChooseDeepInspectCommand?.NotifyCanExecuteChanged();
        ConvertCommand?.NotifyCanExecuteChanged();
        ConvertRowCommand?.NotifyCanExecuteChanged();
        RestoreRowCommand?.NotifyCanExecuteChanged();
        RestoreSelectedCommand?.NotifyCanExecuteChanged();
    }

    public Task AddAsync(IEnumerable<string> inputs) => RunAsync("Adding files", async (token, _) =>
    {
        var preferences = await settings.ReadAsync(token);
        var added = new List<MediaRow>();
        foreach (string input in inputs)
        {
            try
            {
                var paths = await Task.Run(() => discovery.Discover(input, preferences.FolderSearchDepth), token);
                foreach (string path in paths)
                {
                    var remembered = await Task.Run(() => library.RememberAsync(path, token), token);
                    var existing = Files.FirstOrDefault(row => row.LibraryFingerprint == remembered.Fingerprint || string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null && string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (existing is not null)
                    {
                        existing.PropertyChanged -= OnRowPropertyChanged;
                        Files.Remove(existing);
                    }
                    var row = new MediaRow(path) { LibraryFingerprint = remembered.Fingerprint };
                    if (remembered.Analysis is not null)
                    {
                        row.Analysis = remembered.Analysis;
                        row.SetScanned();
                    }
                    if (existing is not null && ReferenceEquals(Focused, existing))
                    {
                        Focused = row;
                    }
                    row.PropertyChanged += OnRowPropertyChanged;
                    Files.Add(row);
                    added.Add(row);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SetStatus(ViewStatus.Error, $"Could not add {input}: {ex.Message}");
            }
        }

        RefreshSourceAvailability();
        Focused ??= Files.FirstOrDefault();
        if (added.Count > 0)
        {
            StartBackgroundPreviews();
        }
        InvalidatePlan();
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllFilesSelected));
        if (added.Count > 0 && (await settings.ReadAsync(token)).AutomaticallyScanAddedFiles)
        {
            await AnalyzeRowsAsync(InDisplayOrder(added), AnalysisMethod.SampledRpu, token);
        }
    });
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MediaRow row)
        {
            return;
        }

        if (e.PropertyName is nameof(MediaRow.Analysis) or nameof(MediaRow.Result))
        {
            previewCompletion = Task.WhenAll(previewCompletion, LoadLibraryHistoryAsync(row));
        }

        if (e.PropertyName == nameof(MediaRow.CanOpenResult))
        {
            OpenOutputCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(MediaRow.IsMissing))
        {
            ScanRowCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(MediaRow.IsProfile7))
        {
            ConvertRowCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(MediaRow.IsProfile7) or nameof(MediaRow.IsMissing))
        {
            InspectCommandsChanged();
        }

        if (e.PropertyName == nameof(MediaRow.CanRestore))
        {
            RestoreRowCommand.NotifyCanExecuteChanged();
            RestoreSelectedCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(MediaRow.RestoreArchive) && !loadingLibraryRows)
        {
            WatchFiles();
        }

        if (e.PropertyName is nameof(MediaRow.IsActive) or nameof(MediaRow.IsCancellationRequested))
        {
            CancelFileCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(MediaRow.FilterGroup))
        {
            ShownFilesChanged();
        }

        if (e.PropertyName is nameof(MediaRow.CanRetryAnalysis) or nameof(MediaRow.CanOpenResult) or nameof(MediaRow.CanInspectIncomplete))
        {
            InspectIncompleteCommand.NotifyCanExecuteChanged();
            RetryAnalysisCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName != nameof(MediaRow.IsSelected))
        {
            return;
        }

        if (IsBusy && !row.IsSelected && row.IsPending)
        {
            Skip(row);
        }
        else if (IsIdle)
        {
            InvalidatePlan();
        }

        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllFilesSelected));
        CommandsChanged();
    }

    private void InvalidatePlan()
    {
        foreach (var row in Files)
        {
            row.ClearPlan();
        }
    }

    [RelayCommand]
    private void Skip(MediaRow? row)
    {
        if (row is null || !row.IsPending || control?.Skip(row.Path) != true)
        {
            return;
        }

        row.SetSkipped("Skipped before starting.");
    }

    /// <summary>The batch's share done for the taskbar; before a batch starts, the operation's own progress.</summary>
    protected override double? ActivityProgress => BatchProgress.IsRunning ? BatchProgress.Percent / 100 : base.ActivityProgress;

    private static string AnalysisTitle(AnalysisMethod method) => method switch
    {
        AnalysisMethod.SampledRpu => "Scan",
        AnalysisMethod.DeepInspection => "Deep inspection",
        _ => "Inspection"
    };

    // A failed file makes the whole batch finish with errors on the taskbar and in the notification.
    private void CompleteBatchItem(MediaRow row, OperationStatus status)
    {
        BatchProgress.Complete(row, status);
        if (status == OperationStatus.Failed)
        {
            ReportItemFailed();
        }
    }

    private void BeginBatch(MediaRow[] rows, string operationName)
    {
        control = new(rows.Select(r => r.Path));
        BatchProgress.Begin(rows.Length, operationName);
        SetStatus(ViewStatus.Progress, $"{operationName} batch in progress.");
        foreach (var row in Files)
        {
            row.SelectionEnabled = false;
        }

        foreach (var row in rows)
        {
            row.IsCancellationRequested = false;
            row.CurrentOperation = operationName == "Conversion planning" ? "Conversion planning" : null;
            row.CanRetryAnalysis = false;
            row.IsPending = true;
            row.SelectionEnabled = true;
            row.Status = "Queued";
        }
    }

    private void EndBatch()
    {
        BatchProgress.End();
        control?.Dispose();
        control = null;
        NotifyActiveProgress();
        foreach (var row in Files)
        {
            if (row.IsPending)
            {
                row.SetCancelled();
                row.CanRetryAnalysis = row.LastAnalysisMethod is not null;
            }

            row.CurrentOperation = null;
            row.IsPending = false;
            row.IsActive = false;
            row.SelectionEnabled = true;
        }

        // Rows skipped as missing, and sources moved by Replace original, are marked now that no job holds them.
        RefreshSourceAvailability();
    }

    private IProgress<OperationProgress> Activate(MediaRow row, string stage)
    {
        row.IsPending = false;
        row.IsActive = true;
        row.SelectionEnabled = false;
        row.Status = stage;
        return BatchProgress.Start(row, stage);
    }

    public Task ScanAllAsync() => RunAsync(AnalysisTitle(AnalysisMethod.SampledRpu), async (token, _) =>
    {
        StartBackgroundPreviews(refreshFocused: true);
        await AnalyzeRowsAsync(InDisplayOrder(ShownFiles.Where(row => !row.IsMissing)), AnalysisMethod.SampledRpu, token);
    });

    private Task ChooseAndInspectAsync(bool deep)
    {
        IsDeepInspectionChosen = deep;
        return AnalyzeAsync(deep ? AnalysisMethod.DeepInspection : AnalysisMethod.FullRpu);
    }

    private Task AnalyzeAsync(AnalysisMethod method) => RunAsync(AnalysisTitle(method), async (token, _) =>
    {
        // Checked files that are missing or not Profile 7 keep their check marks, which only the user changes, but
        // inspection has nothing to add for them.
        await AnalyzeRowsAsync(InDisplayOrder(ShownFiles.Where(r => r.IsSelected && IsInspectable(r))), method, token);
    });

    private async Task AnalyzeRowsAsync(MediaRow[] rows, AnalysisMethod method, CancellationToken token)
    {
        InvalidatePlan();
        var userSettings = await settings.ReadAsync(token);
        bool? toolsReady = null;
        var pairing = BeginArchivePairing();
        // Conversions published in an earlier session, shown once the analysis of their unchanged source completes.
        var publishedResults = new Dictionary<MediaRow, OperationItemResult>();
        foreach (var row in rows)
        {
            row.LastAnalysisMethod = method;
        }

        BeginBatch(rows, method == AnalysisMethod.SampledRpu ? "Scan" : method == AnalysisMethod.DeepInspection ? "Deep inspection" : "Inspection");
        try
        {
            var results = await batch.ExecuteAsync(rows, row => row.Path, async (row, itemToken) =>
            {
                if (!sourceFiles.Exists(row.Path))
                {
                    return MissingSource(row);
                }

                var jobProgress = Activate(row, method == AnalysisMethod.SampledRpu ? "Scanning" : "Inspecting");
                row.AnalysisError = null;
                row.Notice = "";
                row.RestoreArchive = null;
                try
                {
                    // The previous analysis stays until a new one replaces it, so a failed inspection keeps the file details.
                    var analysis = method == AnalysisMethod.SampledRpu
                        ? await Task.Run(() => inspection.ReadCachedAsync(row.Path, method, itemToken), itemToken)
                        : null;
                    if (analysis is null)
                    {
                        toolsReady ??= await dependencies.EnsureAsync(jobProgress, itemToken);
                        if (toolsReady != true)
                        {
                            return new OperationItemResult(row.Path, OperationStatus.Failed, null, "Required tools are unavailable. Open Settings to configure tools, then retry Scan.");
                        }

                        analysis = await Task.Run(() => inspection.InspectAsync(row.Path, method, null, itemToken, jobProgress), itemToken);
                    }

                    row.Analysis = analysis;

                    await RefreshRestoreArchiveAsync(row, pairing, itemToken);
                    if (row.Result is null && await Task.Run(() => conversion.ReadPublishedResultAsync(row.Path, itemToken), itemToken) is { } published)
                    {
                        await ReadResultMetadataAsync(row, published, itemToken);
                        publishedResults[row] = published;
                    }

                    return new OperationItemResult(row.Path, row.Analysis.Verdict == AnalysisVerdict.AnalysisFailed ? OperationStatus.Failed : OperationStatus.Completed, null, row.Analysis.Reason);
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<OperationItemResult>(result =>
            {
                var row = rows.First(r => r.Path == result.Item);
                CompleteBatchItem(row, result.Status);
                if (result.Status != OperationStatus.Completed)
                {
                    row.Status = $"{row.OperationName} {result.Status.ToString().ToLowerInvariant()}";
                }
                row.CanRetryAnalysis = result.Status is OperationStatus.Failed or OperationStatus.Cancelled;
                if (result.Status == OperationStatus.Failed)
                {
                    row.SetAnalysisFailed(result.Message);
                }
                else if (result.Status == OperationStatus.Cancelled)
                {
                    // The row being scanned is still Active; end it as Cancelled so its result dot shows like the queued rows'.
                    row.SetCancelled();
                    row.Notice = result.Message;
                }

                if (result.Status == OperationStatus.Completed)
                {
                    row.SetScanned();
                    if (publishedResults.Remove(row, out var published))
                    {
                        row.SetConverted(published);
                    }
                }
            }), token);
            SetStatus(ViewStatus.Result, $"{(method == AnalysisMethod.SampledRpu ? "Scan" : "Inspection")}: {TimedSummary(results)}");
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>Reads the produced file's metadata for the details comparison. Best effort: the operation result stands either way.</summary>
    private async Task ReadResultMetadataAsync(MediaRow row, OperationItemResult result, CancellationToken token)
    {
        row.ResultAnalysis = null;
        if (result.Status is not (OperationStatus.Completed or OperationStatus.Partial) || result.Output is not { } output)
        {
            return;
        }

        try
        {
            row.ResultAnalysis = await Task.Run(() => inspection.ReadMetadataAsync(output, token), token);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read metadata of produced file {Output}", output);
        }
    }

    private async Task<LibraryFile?> RememberProducedFileAsync(OperationItemResult result, CancellationToken token)
    {
        if (result.Status is not (OperationStatus.Completed or OperationStatus.Partial) || result.Output is not { } output)
        {
            return null;
        }

        try
        {
            return await Task.Run(() => library.RememberAsync(output, token), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not remember produced file {Output}", output);
            return null;
        }
    }

    private void AddOrUpdateProducedRow(MediaRow sourceRow, OperationItemResult itemResult, LibraryFile? remembered)
    {
        if (itemResult.Output is not { } output)
        {
            return;
        }

        if (string.Equals(output, sourceRow.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (remembered is not null)
            {
                sourceRow.LibraryFingerprint = remembered.Fingerprint;
            }

            if (itemResult.Archive is not null)
            {
                sourceRow.RestoreArchive = itemResult.Archive;
            }

            return;
        }

        var outputRow = Files.FirstOrDefault(existing =>
            (remembered is not null && existing.LibraryFingerprint == remembered.Fingerprint && !ReferenceEquals(existing, sourceRow))
            || string.Equals(existing.Path, output, StringComparison.OrdinalIgnoreCase));
        bool added = outputRow is null;
        if (outputRow is null)
        {
            outputRow = new MediaRow(output)
            {
                IsSelected = false,
                SelectionEnabled = IsIdle
            };
        }
        else if (!string.Equals(outputRow.Path, output, StringComparison.OrdinalIgnoreCase))
        {
            bool wasFocused = ReferenceEquals(Focused, outputRow);
            outputRow.PropertyChanged -= OnRowPropertyChanged;
            Files.Remove(outputRow);
            outputRow = new MediaRow(output)
            {
                IsSelected = false,
                SelectionEnabled = IsIdle
            };
            if (wasFocused)
            {
                Focused = outputRow;
            }

            added = true;
        }

        outputRow.LibraryFingerprint = remembered?.Fingerprint ?? outputRow.LibraryFingerprint;
        outputRow.RestoreArchive = itemResult.Archive ?? outputRow.RestoreArchive;
        outputRow.AnalysisError = null;
        outputRow.Warning = null;
        outputRow.Notice = "";
        var analysis = sourceRow.ResultAnalysis ?? remembered?.Analysis;
        if (analysis is not null)
        {
            outputRow.Analysis = analysis;
            outputRow.SetScanned();
        }

        int sourceIndex = Files.IndexOf(sourceRow);
        if (added)
        {
            outputRow.PropertyChanged += OnRowPropertyChanged;
            Files.Insert(sourceIndex < 0 ? Files.Count : sourceIndex + 1, outputRow);
        }
        else if (sourceIndex >= 0)
        {
            int outputIndex = Files.IndexOf(outputRow);
            int destinationIndex = sourceIndex + (outputIndex > sourceIndex ? 1 : 0);
            if (outputIndex != destinationIndex)
            {
                Files.Move(outputIndex, destinationIndex);
            }
        }
    }

    private RestoreArchivePairing BeginArchivePairing() => restore.BeginPairing(Files.Where(row => row.RestoreArchive is not null).Select(row => (Input: row.Path, Archive: row.RestoreArchive!)));

    private async Task RefreshRestoreArchiveAsync(MediaRow row, RestoreArchivePairing pairing, CancellationToken token)
    {
        if (row.Analysis is not { } analysis)
        {
            return;
        }

        try
        {
            row.RestoreArchive = await Task.Run(() => pairing.PairAsync(analysis.Media, token), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Archive matching is optional; its failure must not fail the completed analysis.
            logger.LogWarning(ex, "Restore archive matching failed for {Input}", row.Path);
            row.Notice = $"Could not check for a matching .dovi archive: {ex.Message} Rescan to retry.";
        }
    }

    private Task RestoreRowsAsync(MediaRow[] rows) => RunAsync("Restoration", async (token, progress) =>
    {
        if (rows.Length == 0)
        {
            return;
        }

        if (rows.Any(row => row.RestoreArchive is null))
        {
            SetStatus(ViewStatus.Error, "The matching .dovi archive is no longer available. Rescan after restoring it.");
            return;
        }

        if (!await dependencies.EnsureAsync(progress, token))
        {
            SetStatus(ViewStatus.ToolsUnavailable);
            return;
        }

        var plans = new Dictionary<MediaRow, RestorePlan>();
        foreach (var row in rows)
        {
            try
            {
                plans[row] = await Task.Run(() => restore.PlanAsync(row.Path, row.RestoreArchive!, null, null, false, token), token);
            }
            catch (FileNotFoundException)
            {
                row.RestoreArchive = null;
                throw;
            }
        }

        string review = string.Join("\n\n", plans.Values.Select(plan =>
                $"Base file: {plan.Media.Source.Path}\nArchive: {plan.Archive.Path}\nOutput: {plan.Output}\nScratch: {plan.ScratchBytes / 1073741824d:0.0} GiB"))
            + "\nVerified source pairing required.\nOriginal retained.";
        if (!await dialogs.ReviewAsync("Review restoration", review, "Approve and start"))
        {
            SetStatus(ViewStatus.RestoreNotApproved);
            return;
        }

        foreach (var (row, plan) in plans)
        {
            OperationLog.Audit(logger, "ApproveRestore", review, "ApprovedByDialog", plan.Id);
            row.ClearPlan();
            row.AnalysisError = null;
            row.Notice = "";
            row.LastAnalysisMethod = null;
        }

        BeginBatch(rows, "Restoration");
        foreach (var row in rows)
        {
            row.CurrentOperation = "Restoration";
        }

        var rememberedOutputs = new ConcurrentDictionary<MediaRow, LibraryFile?>();
        try
        {
            var result = await batch.ExecuteAsync(rows, item => item.Path, async (item, itemToken) =>
            {
                var jobProgress = Activate(item, "Restoring Profile 7");
                var restored = await Task.Run(() => restore.ExecuteAsync(plans[item], jobProgress, itemToken), itemToken);
                await ReadResultMetadataAsync(item, restored, itemToken);
                rememberedOutputs[item] = await RememberProducedFileAsync(restored, itemToken);
                return restored;
            }, control!, new InlineProgress<OperationItemResult>(itemResult =>
            {
                var row = rows.First(r => r.Path == itemResult.Item);
                CompleteBatchItem(row, itemResult.Status);
                row.Result = itemResult;
                if (itemResult.Status == OperationStatus.Completed)
                {
                    row.SetRestored(itemResult);
                    AddOrUpdateProducedRow(row, itemResult, rememberedOutputs.GetValueOrDefault(row));
                }
                else if (itemResult.Status == OperationStatus.Cancelled)
                {
                    row.SetCancelled();
                }
                else
                {
                    row.SetFailed(itemResult);
                }
            }), token);
            SetStatus(ViewStatus.Result, $"Restoration: {TimedSummary(result)}");
        }
        finally
        {
            EndBatch();
        }

        // A failed restore may have learned the file's base-layer hash; pair again so a mismatched archive is not offered twice.
        foreach (var row in rows.Where(row => row.State == MediaRowState.Failed))
        {
            await RefreshRestoreArchiveAsync(row, BeginArchivePairing(), token);
        }
    });

    private Task ConvertBatchAsync(MediaRow[]? rows = null) => RunAsync("Conversion", async (token, progress) =>
    {
        if (ConversionStarting is not null)
        {
            foreach (Func<CancellationToken, Task> handler in ConversionStarting.GetInvocationList())
            {
                await handler(token);
            }
        }

        InvalidatePlan();
        var userSettings = await settings.ReadAsync(token);
        var target = userSettings.RemoveDolbyVision ? ConversionTarget.Hdr10 : ConversionTarget.Profile81;
        UpdateSettingsSummary(userSettings);
        if (!string.IsNullOrWhiteSpace(userSettings.TemporaryDirectory))
        {
            string tempPath = Path.GetFullPath(userSettings.TemporaryDirectory);
            if (!Directory.Exists(tempPath))
            {
                await dialogs.ShowMessageAsync(
                    "Temporary storage folder not found",
                    $"The configured temporary storage folder does not exist:\n{tempPath}\n\nPlease check your Settings.");
                return;
            }
        }

        if (userSettings.OutputDirectory is not null && !Directory.Exists(userSettings.OutputDirectory))
        {
            try
            {
                Directory.CreateDirectory(userSettings.OutputDirectory);
            }
            catch (Exception ex)
            {
                await dialogs.ShowMessageAsync(
                    "Output folder error",
                    $"The configured output folder cannot be created or accessed:\n{userSettings.OutputDirectory}\n\n{ex.Message}");
                return;
            }
        }

        if (!await dependencies.EnsureAsync(progress, token))
        {
            SetStatus(ViewStatus.ToolsUnavailable);
            return;
        }

        rows ??= InDisplayOrder(ShownFiles.Where(r => r.IsSelected));
        if (rows.Length == 0)
        {
            return;
        }

        string opName = target == ConversionTarget.Profile81 ? "Conversion to DV8.1" : "Conversion to HDR10";
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingOutput = ExistingOutputHandling.Skip;
        CandidateConversionRequest Request(MediaRow row) => new(
            row.Path,
            target,
            userSettings.OutputDirectory,
            userSettings.TemporaryDirectory,
            userSettings.ReplaceOriginal,
            userSettings.CreateElArchive,
            userSettings.IncludeSimple,
            userSettings.ForceComplex,
            outputs)
        {
            ExistingOutput = existingOutput,
            Safe = userSettings.SafeMode
        };

        // A single file asks what to do with an existing output; batches keep skipping such files.
        if (rows.Length == 1 && sourceFiles.Exists(rows[0].Path)
            && await Task.Run(() => conversion.FindExistingOutput(Request(rows[0])), token) is { } existing)
        {
            var question = new ExistingOutputDialogViewModel(existing);
            if (!await dialogs.ShowAsync(question))
            {
                SetStatus(ViewStatus.ConversionNotStarted);
                return;
            }

            existingOutput = question.Choice;
        }

        foreach (var row in rows)
        {
            row.LastAnalysisMethod = null;
            row.Warning = null;
            row.AnalysisError = null;
            row.Notice = "";
        }

        var rememberedOutputs = new ConcurrentDictionary<MediaRow, LibraryFile?>();
        BeginBatch(rows, opName);
        try
        {
            var result = await batch.ExecuteQueuedAsync(rows, settings.Hardware.ConversionWorkers(userSettings.ConversionWorkers), r => r.Path, async (row, itemToken) =>
            {
                if (!sourceFiles.Exists(row.Path))
                {
                    return MissingSource(row);
                }

                var jobProgress = Activate(row, "Preparing conversion");
                row.AnalysisError = null;
                row.Notice = "";
                row.Warning = null;
                using var cancellationRegistration = itemToken.Register(row.ClearPlan);
                try
                {
                    var req = Request(row);
                    IDisposable reservation;
                    try
                    {
                        reservation = await diskBudget.ReserveAsync(req, itemToken);
                    }
                    catch (IOException ex)
                    {
                        return new OperationItemResult(row.Path, OperationStatus.Skipped, null, ex.Message);
                    }

                    using var heldReservation = reservation;
                    var prep = await Task.Run(() => conversion.PrepareCandidateAsync(req, jobProgress, itemToken), itemToken);
                    itemToken.ThrowIfCancellationRequested();
                    if (prep is CandidatePreparationResult.Skipped skipped)
                    {
                        return new OperationItemResult(row.Path, OperationStatus.Skipped, null, skipped.Reason);
                    }

                    if (prep is CandidatePreparationResult.Failed failed)
                    {
                        // Preparation fails only when the file cannot be read or inspected.
                        row.AnalysisError = failed.Error;
                        return new OperationItemResult(row.Path, OperationStatus.Failed, null, failed.Error);
                    }

                    if (prep is CandidatePreparationResult.Success success)
                    {
                        row.Analysis = success.Plan.Analysis;
                        row.SetPlan(success.Warning);
                        BatchProgress.Describe(row, ConversionJob.Details(success.Plan), ConversionJob.Steps(success.Plan));
                        jobProgress.Report(new(success.Plan.Id, "Converting", row.Path));
                        var converted = await Task.Run(() => conversion.ExecuteAsync(success.Plan, jobProgress, itemToken), itemToken);
                        await ReadResultMetadataAsync(row, converted, itemToken);
                        rememberedOutputs[row] = await RememberProducedFileAsync(converted, itemToken);
                        return converted;
                    }

                    return new OperationItemResult(row.Path, OperationStatus.Failed, null, "Candidate preparation produced no result.");
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<OperationItemResult>(itemResult =>
            {
                var row = rows.First(r => r.Path == itemResult.Item);
                CompleteBatchItem(row, itemResult.Status);
                row.CanRetryAnalysis = false;
                if (itemResult.Status is OperationStatus.Completed or OperationStatus.Partial)
                {
                    row.SetConverted(itemResult);
                    rememberedOutputs.TryRemove(row, out var remembered);
                    AddOrUpdateProducedRow(row, itemResult, remembered);
                }
                else
                {
                    row.Result = itemResult;
                    if (itemResult.Status == OperationStatus.Skipped)
                    {
                        row.SetSkipped(itemResult.Message);
                    }
                    else if (itemResult.Status == OperationStatus.Failed)
                    {
                        row.SetFailed(itemResult);
                    }
                    else
                    {
                        row.SetCancelled();
                    }
                }
            }), token);

            SetStatus(ViewStatus.Result, $"{opName}: {TimedSummary(result)}");
        }
        finally
        {
            EndBatch();
        }
    });

    private bool CanRemoveFile(MediaRow? row) => row is not null && IsIdle && Files.Contains(row);

    [RelayCommand(CanExecute = nameof(CanRemoveFile))]
    private Task RemoveFile(MediaRow? row) => row is not null && CanRemoveFile(row)
        ? RemoveRowsAsync([row])
        : Task.CompletedTask;

    private bool CanRemoveMissing() => IsIdle && HasMissingFiles;

    [RelayCommand(CanExecute = nameof(CanRemoveMissing))]
    private Task RemoveMissing() => RemoveRowsAsync(Files.Where(row => row.IsMissing).ToArray());

    [RelayCommand(CanExecute = nameof(CanOperate))]
    private Task RemoveSelected() => RemoveRowsAsync(ShownFiles.Where(row => row.IsSelected).ToArray());

    /// <summary>
    /// Finds .dovi archives and .bak.dovi_convert originals in a folder and its subfolders, and deletes exactly the
    /// reviewed files. Rows whose archive goes lose Restore through the folder watcher.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task CleanupBackups() => RunAsync("Cleanup", async (token, _) =>
    {
        string? folder = files.PickFolder();
        if (folder is null)
        {
            return;
        }

        var plan = await Task.Run(() => cleanup.Plan(folder, 100), token);
        if (plan.Files.Count == 0)
        {
            SetStatus(ViewStatus.NoBackupsFound);
            return;
        }

        string review = "Remove exactly these retained backup files (Recycle Bin if enabled in Safety settings; otherwise permanent deletion):\n\n" + string.Join("\n", plan.Files.Select(f => $"{f.Path} ({f.Length:N0} bytes)"));
        if (!await dialogs.ReviewAsync("Review cleanup", review, "Delete"))
        {
            SetStatus(ViewStatus.CleanupNotApproved);
            return;
        }

        OperationLog.Audit(logger, "ApproveCleanup", review, "ApprovedByConfirmation", plan.Id);
        var result = await Task.Run(() => cleanup.ExecuteAsync(plan, token), token);
        var failed = result.Items.Where(item => item.Status == OperationStatus.Failed).ToArray();
        if (failed.Length > 0)
        {
            ReportItemFailed();
        }

        SetStatus(failed.Length > 0 ? ViewStatus.Error : ViewStatus.Result,
            string.Join("\n", failed.Select(item => $"{item.Item}: {item.Message}").Prepend($"Cleanup: {Summary(result)}")));
    });

    private Task RemoveRowsAsync(MediaRow[] rows) => RunAsync(async (token, _) =>
    {
        await library.ForgetAsync(rows.Select(row => row.Path).ToArray(), token);
        RemoveRowsFromList(rows);
    });

    /// <summary>Clears the visible collection after Settings has successfully deleted the library.</summary>
    public void ClearDeletedLibrary() => RemoveRowsFromList(Files.ToArray());

    private void RemoveRowsFromList(MediaRow[] rows)
    {
        CancelPreviews();
        bool wasFocused = rows.Contains(Focused);
        foreach (var row in rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
            Files.Remove(row);
        }
        if (wasFocused)
        {
            Focused = Files.FirstOrDefault();
        }
        StartBackgroundPreviews();
        InvalidatePlan();
        if (IsFileListEmpty)
        {
            Focused = null;
            SetStatus(ViewStatus.FileListCleared);
        }
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllFilesSelected));
        CommandsChanged();
    }

    /// <summary>Returns rows in the order the list shows them, so batches process files top to bottom.</summary>
    private MediaRow[] InDisplayOrder(IEnumerable<MediaRow> rows)
    {
        // The view is not live-sorted; re-apply the sort so values changed since the
        // last click (for example by a scan) put the screen and processing order in sync.
        FileSort.Refresh();
        return FileSort.Order(rows);
    }

    private bool CanToggleSelectAll() => IsIdle && ShownFiles.Any(row => row.SelectionEnabled);

    [RelayCommand(CanExecute = nameof(CanToggleSelectAll))]
    private void ToggleSelectAll()
    {
        if (!IsIdle)
        {
            return;
        }

        bool selectAll = AllFilesSelected != true;
        foreach (var row in ShownFiles.Where(row => row.SelectionEnabled))
        {
            row.IsSelected = selectAll;
        }

        OnPropertyChanged(nameof(AllFilesSelected));
    }
    private void NotifyActiveProgress()
    {
        OnPropertyChanged(nameof(PauseBatchText));
        OnPropertyChanged(nameof(PauseBatchIcon));
        OnPropertyChanged(nameof(PauseBatchToolTip));
        PauseBatchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ActiveProgressPercent));
        OnPropertyChanged(nameof(ActiveProgressIndeterminate));
        OnPropertyChanged(nameof(ActiveProgressTitle));
        OnPropertyChanged(nameof(ActiveProgressText));
        OnPropertyChanged(nameof(ActiveProgressSummary));
        OnPropertyChanged(nameof(ActiveProgressToolTip));
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
