using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Restore;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.ViewModels;
public sealed class MediaViewModel : OperationViewModel, INavigationPage, IPageActivation, IDisposable
{
    public string NavigationName => "Media";
    public string? NavigationIcon => "\uE714";
    public override IReadOnlyList<StatusItem> StatusItems =>
    [
        new(SelectionSummary),
        new(StatusText),
        new(OutputSummary, OutputSummaryToolTip, Command: OpenSettingsCommand),
        new(RetentionSummary, IsEmphasized: IsReplaceOriginalActive),
        new(FelSummary),
        new(ArchiveSummary)
    ];

    private readonly IFileDiscovery discovery;
    private readonly InspectionService inspection;
    private readonly ConversionService conversion;
    private readonly RestoreService restore;
    private readonly ControlledBatchService batch;
    private readonly SettingsService settings;
    private readonly DependencySetup dependencies;
    private readonly IDialogService dialogs;
    private readonly IFileDialogService files;
    private readonly IFileExplorer explorer;
    private readonly ILogger<MediaViewModel> logger;
    private BatchControl? control;
    private MediaRow? focused;
    private readonly IMediaPreview mediaPreview;
    private CancellationTokenSource? previewCancellation;
    private CancellationTokenSource? backgroundPreviewCancellation;
    private Task previewCompletion = Task.CompletedTask;
    private BitmapSource? framePreview;
    private string previewStatus = "";
    private readonly ISourceFileMonitor sourceFiles;
    private readonly SynchronizationContext? uiContext;
    private int availabilityCheckQueued;

    private static string Summary(BatchResult result) => string.Join(" · ", result.Items.GroupBy(r => r.Status).Select(g => $"{g.Count()} {g.Key}"));
    public MediaViewModel(IFileDiscovery discovery, InspectionService inspection, ConversionService conversion, ControlledBatchService batch, SettingsService settings, DependencySetup dependencies, IDialogService dialogs, IFileDialogService files, IFileExplorer explorer, ILogger<MediaViewModel> logger, IMediaPreview mediaPreview, RestoreService restore, ISourceFileMonitor sourceFiles)
    {
        this.discovery = discovery;
        this.sourceFiles = sourceFiles;
        uiContext = SynchronizationContext.Current;
        sourceFiles.Changed += OnSourceFilesChanged;
        this.inspection = inspection;
        this.conversion = conversion;
        this.restore = restore;
        this.batch = batch;
        this.settings = settings;
        this.dependencies = dependencies;
        this.dialogs = dialogs;
        this.files = files;
        this.explorer = explorer;
        this.logger = logger;
        this.mediaPreview = mediaPreview;

        AddFilesCommand = new(() => AddAsync(files.PickFiles("Matroska media|*.mkv")), () => IsIdle);
        AddFolderCommand = new(() => AddAsync(files.PickFolder() is { } folder ? [folder] : []), () => IsIdle);
        AddDroppedPathsCommand = new(paths => AddAsync((paths ?? []).Where(discovery.IsSupportedInput)),
            paths => IsIdle && paths is not null && paths.Any(discovery.IsSupportedInput));
        ScanCommand = new(ScanAllAsync, CanScan);
        Files.CollectionChanged += (_, _) =>
        {
            sourceFiles.Watch(Files.Select(row => row.Path));
            OnPropertyChanged(nameof(IsFileListEmpty));
            OnPropertyChanged(nameof(HasMissingFiles));
            CommandsChanged();
        };
        InspectCommand = new(() => AnalyzeAsync(AnalysisMethod.FullRpu), CanOperate);
        DeepInspectCommand = new(() => AnalyzeAsync(AnalysisMethod.DeepInspection), CanOperate);
        ConvertDv81Command = new(() => ConvertBatchAsync(ConversionTarget.Profile81), CanOperate);
        ConvertRowDv81Command = new(row => ConvertBatchAsync(ConversionTarget.Profile81, [row!]), row => row is not null && IsIdle && Files.Contains(row) && row.IsProfile7 && !row.IsMissing);
        RestoreRowCommand = new(row => RestoreRowAsync(row!), row => row is not null && IsIdle && Files.Contains(row) && row.CanRestore);
        ConvertHdrCommand = new(() => ConvertBatchAsync(ConversionTarget.Hdr10), CanOperate);

        SkipCommand = new RelayCommand<MediaRow>(Skip);

        CancelFileCommand = new RelayCommand<MediaRow>(row =>
        {
            if (row is not null && row.IsActive && !row.IsCancellationRequested && control is not null)
            {
                row.IsCancellationRequested = true;
                row.Status = "Cancelling…";
                row.Progress.Update("Cancelling…", null);
                control.Cancel(row.Path);
            }
        }, row => row is not null && row.IsActive && !row.IsCancellationRequested);

        PauseBatchCommand = new(() =>
        {
            if (control?.IsPaused == true)
            {
                control.Resume();
            }
            else
            {
                control?.Pause();
            }

            NotifyActiveProgress();
        }, () => BatchProgress.IsRunning);

        OpenOutputCommand = new(() => explorer.OpenFolder(Path.GetDirectoryName(Focused!.Result!.Output!)!), () => Focused?.Result?.Output is not null);
        OpenRowOutputCommand = new(row => explorer.OpenFolder(Path.GetDirectoryName(row!.Result!.Output!)!), row => row is not null && row.CanOpenResult);
        OpenFileLocationCommand = new(() => explorer.ShowInFolder(Focused!.Path), () => Focused is not null);
        RetryAnalysisCommand = new(row => RunAsync((token, _) => AnalyzeRowsAsync([row!], row!.LastAnalysisMethod!.Value, token)), row => row is not null && IsIdle && Files.Contains(row) && row.CanRetryAnalysis && row.LastAnalysisMethod is not null);
        InspectIncompleteCommand = new(row => RunAsync((token, _) => AnalyzeRowsAsync([row!], AnalysisMethod.FullRpu, token)), row => row is not null && IsIdle && Files.Contains(row) && row.CanInspectIncomplete);
        OpenLogsCommand = new(explorer.OpenLogs);
        ToggleSelectAllCommand = new(ToggleSelectAll, () => IsIdle && Files.Any(row => row.SelectionEnabled));
        ClearAllCommand = new(ClearAll, () => IsIdle && Files.Count > 0);
        RemoveFileCommand = new(RemoveFile, row => row is not null && IsIdle && Files.Contains(row));
        RemoveMissingCommand = new(RemoveMissing, () => IsIdle && HasMissingFiles);
        OpenSettingsCommand = new(() => RequestNavigateToSettings?.Invoke(), () => IsIdle);
        FileSort = new((column, direction) => new MediaRowComparer((MediaSortColumn)column, direction), () => IsIdle);
        BatchProgress.PropertyChanged += (_, _) => NotifyActiveProgress();
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
    public BatchProgressViewModel BatchProgress { get; } = new();
    public RelayCommand PauseBatchCommand { get; }
    public string PauseBatchText => control?.IsPaused == true ? "Resume" : "Pause";
    public string PauseBatchIcon => control?.IsPaused == true ? "\uE768" : "\uE769";

    public double ActiveProgressPercent => BatchProgress.IsRunning ? BatchProgress.Percent : Percent;
    public bool ActiveProgressIndeterminate => !BatchProgress.IsRunning && IsIndeterminate;
    public string ActiveProgressTitle => BatchProgress.IsRunning
        ? BatchProgress.Operation
        : (string.IsNullOrWhiteSpace(Stage) ? "Working…" : Stage);
    public string ActiveProgressText => BatchProgress.IsRunning
        ? $"{BatchProgress.Percent:0.#}%"
        : Progress.ProgressText;
    public string ActiveProgressSummary => BatchProgress.IsRunning
        ? control?.IsPaused == true
            ? $"{BatchProgress.Summary} · {(BatchProgress.CurrentJob is null ? "Paused" : "Pausing after current job")}"
            : BatchProgress.Summary
        : StatusMessage;
    public string? ActiveProgressToolTip => BatchProgress.IsRunning
        ? "Processed includes completed, failed, cancelled and skipped files. Each file has equal weight in the batch."
        : null;

    public MediaRow? Focused
    {
        get => focused;
        set
        {
            if (SetProperty(ref focused, value))
            {
                OpenOutputCommand.NotifyCanExecuteChanged();
                OpenFileLocationCommand.NotifyCanExecuteChanged();
                previewCompletion = Task.WhenAll(previewCompletion, LoadPreviewAsync(value));
            }
        }
    }

    public BitmapSource? FramePreview
    {
        get => framePreview;
        private set => SetProperty(ref framePreview, value);
    }

    public string PreviewStatus
    {
        get => previewStatus;
        private set => SetProperty(ref previewStatus, value);
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
            var image = await Task.Run(async () =>
            {
                byte[] bytes = await mediaPreview.LoadAsync(row.Path, cancellation.Token);
                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }, cancellation.Token);
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
        sourceFiles.Watch(Files.Select(row => row.Path));
        RefreshSourceAvailability();
    }

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
            if (missing)
            {
                row.IsSelected = false;
            }
            else if (row.Notice == MediaRow.MissingNote)
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
            ConvertRowDv81Command.NotifyCanExecuteChanged();
            CommandsChanged();
        }
    }

    private static OperationItemResult MissingSource(MediaRow row) => new(row.Path, OperationStatus.Skipped, null, MediaRow.MissingNote);

    public string SelectionSummary
    {
        get
        {
            var total = Files.Count;
            var selected = Files.Count(f => f.IsSelected);
            var totalText = $"{total} {(total == 1 ? "item" : "items")}";
            if (selected == 0)
            {
                return totalText;
            }

            var selectedText = $"{selected} {(selected == 1 ? "item" : "items")} selected";
            return $"{totalText} | {selectedText}";
        }
    }
    public bool? AllFilesSelected => Files.Count == 0 || Files.All(row => !row.IsSelected) ? false : Files.All(row => row.IsSelected) ? true : null;

    public RelayCommand ToggleSelectAllCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand<MediaRow> RemoveFileCommand { get; }
    public RelayCommand RemoveMissingCommand { get; }
    /// <summary>
    /// Display order applied by the list's collection view; <see cref="Files"/> keeps the order files were added.
    /// Sorting is blocked while busy because a running batch keeps the order it started with.
    /// </summary>
    public ColumnSort<MediaRow> FileSort { get; }
    public bool CanInspect => CanOperate();
    public AsyncRelayCommand AddFilesCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand<string[]> AddDroppedPathsCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand InspectCommand { get; }
    public AsyncRelayCommand DeepInspectCommand { get; }
    public AsyncRelayCommand ConvertDv81Command { get; }
    public AsyncRelayCommand<MediaRow> ConvertRowDv81Command { get; }
    public AsyncRelayCommand<MediaRow> RestoreRowCommand { get; }
    public AsyncRelayCommand ConvertHdrCommand { get; }
    public RelayCommand<MediaRow> SkipCommand { get; }
    public RelayCommand<MediaRow> CancelFileCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand OpenFileLocationCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public AsyncRelayCommand<MediaRow> RetryAnalysisCommand { get; }
    public AsyncRelayCommand<MediaRow> InspectIncompleteCommand { get; }
    public RelayCommand<MediaRow> OpenRowOutputCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public event Action? RequestNavigateToSettings;
    public event Func<CancellationToken, Task>? ConversionStarting;

    private string outputSummary = "Output: Same folder";
    public string OutputSummary
    {
        get => outputSummary;
        private set => SetProperty(ref outputSummary, value);
    }

    private string outputSummaryToolTip = "";
    public string OutputSummaryToolTip
    {
        get => outputSummaryToolTip;
        private set => SetProperty(ref outputSummaryToolTip, value);
    }

    private bool isReplaceOriginalActive;
    public bool IsReplaceOriginalActive
    {
        get => isReplaceOriginalActive;
        private set => SetProperty(ref isReplaceOriginalActive, value);
    }

    private string retentionSummary = "Keep originals";
    public string RetentionSummary
    {
        get => retentionSummary;
        private set => SetProperty(ref retentionSummary, value);
    }

    private string felSummary = "FEL: Skip";
    public string FelSummary
    {
        get => felSummary;
        private set => SetProperty(ref felSummary, value);
    }

    private string archiveSummary = "EL archive: Off";
    public string ArchiveSummary
    {
        get => archiveSummary;
        private set => SetProperty(ref archiveSummary, value);
    }

    public void UpdateSettingsSummary(UserSettings s)
    {
        IsReplaceOriginalActive = s.ReplaceOriginal;
        RetentionSummary = s.ReplaceOriginal ? "⚠ Replace originals" : "Keep originals";
        FelSummary = (s.IncludeSimple, s.ForceComplex) switch
        {
            (true, true) => "FEL: Simple + Complex",
            (true, false) => "FEL: Simple only",
            (false, true) => "FEL: Complex only",
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
        sb.AppendLine($"Destination: {(string.IsNullOrWhiteSpace(s.OutputDirectory) ? "Same folder as source file" : s.OutputDirectory)}");
        sb.AppendLine($"Retention: {(s.ReplaceOriginal ? "Replace original after verification (Source file will be replaced!)" : "Keep original (Non-destructive)")}");
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

    public Task OnActivatedAsync(CancellationToken cancellationToken) => RefreshSettingsSummaryAsync(cancellationToken);

    public async Task RefreshSettingsSummaryAsync(CancellationToken token = default)
    {
        try
        {
            UpdateSettingsSummary(await settings.ReadAsync(token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read settings for summary.");
        }
    }

    private bool CanScan() => IsIdle && Files.Any(row => !row.IsMissing);
    private bool CanOperate() => IsIdle && Files.Any(f => f.IsSelected);

    protected override void CommandsChanged()
    {
        OnPropertyChanged(nameof(CanInspect));
        RetryAnalysisCommand?.NotifyCanExecuteChanged();
        InspectIncompleteCommand?.NotifyCanExecuteChanged();
        OpenRowOutputCommand?.NotifyCanExecuteChanged();
        ToggleSelectAllCommand?.NotifyCanExecuteChanged();
        ClearAllCommand?.NotifyCanExecuteChanged();
        RemoveFileCommand?.NotifyCanExecuteChanged();
        RemoveMissingCommand?.NotifyCanExecuteChanged();
        OpenSettingsCommand?.NotifyCanExecuteChanged();
        AddFilesCommand?.NotifyCanExecuteChanged();
        AddFolderCommand?.NotifyCanExecuteChanged();
        AddDroppedPathsCommand?.NotifyCanExecuteChanged();
        ScanCommand?.NotifyCanExecuteChanged();
        InspectCommand?.NotifyCanExecuteChanged();
        DeepInspectCommand?.NotifyCanExecuteChanged();
        ConvertDv81Command?.NotifyCanExecuteChanged();
        ConvertRowDv81Command?.NotifyCanExecuteChanged();
        RestoreRowCommand?.NotifyCanExecuteChanged();
        ConvertHdrCommand?.NotifyCanExecuteChanged();
    }

    public Task AddAsync(IEnumerable<string> inputs) => RunAsync(async (token, _) =>
    {
        var added = new List<MediaRow>();
        foreach (string input in inputs)
        {
            try
            {
                var paths = await Task.Run(() => discovery.Discover(input, 100), token);
                foreach (string path in paths)
                {
                    if (Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var row = new MediaRow(path);
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

        if (e.PropertyName == nameof(MediaRow.Result))
        {
            OpenOutputCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(MediaRow.IsProfile7))
        {
            ConvertRowDv81Command.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(MediaRow.CanRestore))
        {
            RestoreRowCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(MediaRow.IsActive) or nameof(MediaRow.IsCancellationRequested))
        {
            CancelFileCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(MediaRow.CanRetryAnalysis) or nameof(MediaRow.CanOpenResult) or nameof(MediaRow.CanInspectIncomplete))
        {
            InspectIncompleteCommand.NotifyCanExecuteChanged();
            RetryAnalysisCommand.NotifyCanExecuteChanged();
            OpenRowOutputCommand.NotifyCanExecuteChanged();
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

    private void Skip(MediaRow? row)
    {
        if (row is null || !row.IsPending || control?.Skip(row.Path) != true)
        {
            return;
        }

        row.SetSkipped("Deselected before starting.");
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

    public Task ScanAllAsync() => RunAsync(async (token, _) =>
    {
        StartBackgroundPreviews(refreshFocused: true);
        await AnalyzeRowsAsync(InDisplayOrder(Files.Where(row => !row.IsMissing)), AnalysisMethod.SampledRpu, token);
    });

    private Task AnalyzeAsync(AnalysisMethod method) => RunAsync(async (token, _) =>
    {
        await AnalyzeRowsAsync(InDisplayOrder(Files.Where(r => r.IsSelected)), method, token);
    });

    private async Task AnalyzeRowsAsync(MediaRow[] rows, AnalysisMethod method, CancellationToken token)
    {
        InvalidatePlan();
        var userSettings = await settings.ReadAsync(token);
        bool autoSelect = userSettings.AutoSelectAfterScan;
        bool? toolsReady = null;
        var pairing = BeginArchivePairing();
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
                row.Analysis = null;
                row.RestoreArchive = null;
                try
                {
                    row.Analysis = await Task.Run(() => inspection.ReadCachedAsync(row.Path, method, itemToken), itemToken);
                    if (row.Analysis is null)
                    {
                        toolsReady ??= await dependencies.EnsureAsync(jobProgress, itemToken);
                        if (toolsReady != true)
                        {
                            return new OperationItemResult(row.Path, OperationStatus.Failed, null, "Required tools are unavailable. Open Settings to configure tools, then retry Scan.");
                        }

                        row.Analysis = await Task.Run(() => inspection.InspectAsync(row.Path, method, null, itemToken, jobProgress), itemToken);
                    }

                    await RefreshRestoreArchiveAsync(row, pairing, itemToken);
                    return new OperationItemResult(row.Path, row.Analysis.Verdict == AnalysisVerdict.AnalysisFailed ? OperationStatus.Failed : OperationStatus.Completed, null, row.Analysis.Reason);
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<OperationItemResult>(result =>
            {
                BatchProgress.Complete(result.Status);
                var row = rows.First(r => r.Path == result.Item);
                row.Status = result.Status == OperationStatus.Completed ? "" : $"{row.OperationName} {result.Status.ToString().ToLowerInvariant()}";
                row.CanRetryAnalysis = result.Status is OperationStatus.Failed or OperationStatus.Cancelled;
                if (result.Status == OperationStatus.Failed)
                {
                    row.AnalysisError = result.Message;
                }
                else if (result.Status == OperationStatus.Cancelled)
                {
                    row.Notice = result.Message;
                }

                if (result.Status == OperationStatus.Completed)
                {
                    row.SetScanned();
                    if (autoSelect)
                    {
                        row.IsSelected = ConversionPolicy.ShouldAutoSelectAfterAnalysis(row.Analysis, userSettings.IncludeSimple, userSettings.ForceComplex);
                    }
                }
                else if (result.Status == OperationStatus.Failed)
                {
                    row.IsSelected = false;
                }
            }), token);
            SetStatus(ViewStatus.Result, $"{(method == AnalysisMethod.SampledRpu ? "Scan" : "Inspection")}: {Summary(results)}");
        }
        finally
        {
            EndBatch();
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

    private Task RestoreRowAsync(MediaRow row) => RunAsync(async (token, progress) =>
    {
        if (row.RestoreArchive is not { } archive)
        {
            SetStatus(ViewStatus.Error, "The matching .dovi archive is no longer available. Rescan after restoring it.");
            return;
        }

        if (!await dependencies.EnsureAsync(progress, token))
        {
            SetStatus(ViewStatus.ToolsUnavailable);
            return;
        }

        RestorePlan plan;
        try
        {
            plan = await Task.Run(() => restore.PlanAsync(row.Path, archive, null, null, false, token), token);
        }
        catch (FileNotFoundException)
        {
            row.RestoreArchive = null;
            throw;
        }
        string review = $"Base file: {plan.Media.Source.Path}\nArchive: {plan.Archive.Path}\nOutput: {plan.Output}\nScratch: {plan.ScratchBytes / 1073741824d:0.0} GiB\nVerified source pairing required.\nOriginal retained.";
        if (!await dialogs.ReviewAsync("Review restoration", review, "Approve and start"))
        {
            SetStatus(ViewStatus.RestoreNotApproved);
            return;
        }

        OperationLog.Audit(logger, "ApproveRestore", review, "ApprovedByDialog", plan.Id);
        row.ClearPlan();
        row.AnalysisError = null;
        row.Notice = "";
        row.LastAnalysisMethod = null;
        BeginBatch([row], "Restoration");
        row.CurrentOperation = "Restoration";
        try
        {
            var result = await batch.ExecuteAsync(new[] { row }, item => item.Path, async (item, itemToken) =>
            {
                var jobProgress = Activate(item, "Restoring Profile 7");
                return await Task.Run(() => restore.ExecuteAsync(plan, jobProgress, itemToken), itemToken);
            }, control!, new InlineProgress<OperationItemResult>(itemResult =>
            {
                BatchProgress.Complete(itemResult.Status);
                row.Result = itemResult;
                if (itemResult.Status == OperationStatus.Completed)
                {
                    row.SetRestored(itemResult);
                }
                else if (itemResult.Status == OperationStatus.Cancelled)
                {
                    row.SetCancelled();
                }
                else
                {
                    row.SetFailed(itemResult.Message);
                }
            }), token);
            SetStatus(ViewStatus.Result, $"Restoration: {Summary(result)}");
        }
        finally
        {
            EndBatch();
        }

        if (row.State == MediaRowState.Failed)
        {
            // A failed restore may have learned the file's base-layer hash; pair again so a mismatched archive is not offered twice.
            await RefreshRestoreArchiveAsync(row, BeginArchivePairing(), token);
        }
    });

    private Task ConvertBatchAsync(ConversionTarget target, MediaRow[]? rows = null) => RunAsync(async (token, progress) =>
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

        rows ??= InDisplayOrder(Files.Where(r => r.IsSelected));
        if (rows.Length == 0)
        {
            return;
        }

        string opName = target == ConversionTarget.Profile81 ? "Conversion to DV8.1" : "Conversion to HDR10";
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            row.LastAnalysisMethod = null;
            row.Warning = null;
            row.AnalysisError = null;
            row.Notice = "";
        }

        BeginBatch(rows, opName);
        try
        {
            var result = await batch.ExecuteAsync(rows, r => r.Path, async (row, itemToken) =>
            {
                if (!sourceFiles.Exists(row.Path))
                {
                    return MissingSource(row);
                }

                var jobProgress = Activate(row, "Preparing conversion");
                row.AnalysisError = null;
                row.Notice = "";
                row.Warning = null;
                row.PlannedOutput = "";
                using var cancellationRegistration = itemToken.Register(row.ClearPlan);
                try
                {
                    var req = new CandidateConversionRequest(
                        row.Path,
                        target,
                        userSettings.OutputDirectory,
                        userSettings.TemporaryDirectory,
                        userSettings.ReplaceOriginal,
                        userSettings.CreateElArchive,
                        userSettings.IncludeSimple,
                        userSettings.ForceComplex,
                        outputs);

                    var prep = await Task.Run(() => conversion.PrepareCandidateAsync(req, jobProgress, itemToken), itemToken);
                    itemToken.ThrowIfCancellationRequested();
                    if (prep is CandidatePreparationResult.Skipped skipped)
                    {
                        return new OperationItemResult(row.Path, OperationStatus.Skipped, null, skipped.Reason);
                    }

                    if (prep is CandidatePreparationResult.Failed failed)
                    {
                        return new OperationItemResult(row.Path, OperationStatus.Failed, null, failed.Error);
                    }

                    if (prep is CandidatePreparationResult.Success success)
                    {
                        row.Analysis = success.Plan.Analysis;
                        row.SetPlan(success.Plan.Output, success.Warning);
                        jobProgress.Report(new(success.Plan.Id, "Converting", row.Path));
                        return await Task.Run(() => conversion.ExecuteAsync(success.Plan, jobProgress, itemToken), itemToken);
                    }

                    return new OperationItemResult(row.Path, OperationStatus.Failed, null, "Candidate preparation produced no result.");
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<OperationItemResult>(itemResult =>
            {
                BatchProgress.Complete(itemResult.Status);
                var row = rows.First(r => r.Path == itemResult.Item);
                row.CanRetryAnalysis = false;
                if (itemResult.Status is OperationStatus.Completed or OperationStatus.Partial)
                {
                    row.SetConverted(itemResult);
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
                        row.SetFailed(itemResult.Message);
                    }
                    else
                    {
                        row.SetCancelled();
                    }
                }
            }), token);

            SetStatus(ViewStatus.Result, $"{opName}: {Summary(result)}");
        }
        finally
        {
            EndBatch();
        }
    });

    private void RemoveFile(MediaRow? row)
    {
        if (row is null || !RemoveFileCommand.CanExecute(row))
        {
            return;
        }

        row.PropertyChanged -= OnRowPropertyChanged;
        bool wasFocused = ReferenceEquals(Focused, row);
        Files.Remove(row);
        StartBackgroundPreviews();
        if (wasFocused)
        {
            Focused = Files.FirstOrDefault();
        }

        InvalidatePlan();
        if (IsFileListEmpty)
        {
            SetStatus(ViewStatus.FileListCleared);
        }
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(AllFilesSelected));
    }

    private void RemoveMissing()
    {
        foreach (var row in Files.Where(row => row.IsMissing).ToArray())
        {
            RemoveFile(row);
        }
    }

    private void ClearAll()
    {
        if (!IsIdle)
        {
            return;
        }

        CancelPreviews();
        foreach (var row in Files)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Files.Clear();
        Focused = null;
        InvalidatePlan();
        SetStatus(ViewStatus.FileListCleared);
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

    private void ToggleSelectAll()
    {
        if (!IsIdle)
        {
            return;
        }

        bool selectAll = AllFilesSelected != true;
        foreach (var row in Files.Where(row => row.SelectionEnabled))
        {
            row.IsSelected = selectAll;
        }

        OnPropertyChanged(nameof(AllFilesSelected));
    }
    private void NotifyActiveProgress()
    {
        OnPropertyChanged(nameof(PauseBatchText));
        OnPropertyChanged(nameof(PauseBatchIcon));
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
