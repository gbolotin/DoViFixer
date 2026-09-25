using System.Collections.ObjectModel;
using System.ComponentModel;
using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.ViewModels;
public sealed class MediaViewModel : OperationViewModel
{
    private readonly IFileDiscovery discovery;
    private readonly InspectionService inspection;
    private readonly ConversionService conversion;
    private readonly ControlledBatchService batch;
    private readonly SettingsService settings;
    private readonly DependencySetup dependencies;
    private readonly IUserDialogs dialogs;
    private readonly ILogger<MediaViewModel> logger;
    private BatchControl? control;
    private MediaRow? focused;

    private static string Summary(BatchResult result) => string.Join(" · ", result.Items.GroupBy(r => r.Status).Select(g => $"{g.Count()} {g.Key}"));
    public MediaViewModel(IFileDiscovery discovery, InspectionService inspection, ConversionService conversion, ControlledBatchService batch, SettingsService settings, DependencySetup dependencies, IUserDialogs dialogs, ILogger<MediaViewModel> logger)
    {
        this.discovery = discovery;
        this.inspection = inspection;
        this.conversion = conversion;
        this.batch = batch;
        this.settings = settings;
        this.dependencies = dependencies;
        this.dialogs = dialogs;
        this.logger = logger;

        AddFilesCommand = new(() => AddAsync(dialogs.PickFiles()), () => IsIdle);
        AddFolderCommand = new(() => AddAsync(dialogs.PickFolder() is { } folder ? [folder] : []), () => IsIdle);
        ScanCommand = new(ScanAllAsync, CanScan);
        Files.CollectionChanged += (_, _) => CommandsChanged();
        InspectCommand = new(() => AnalyzeAsync(AnalysisMethod.FullRpu), CanOperate);
        DeepInspectCommand = new(() => AnalyzeAsync(AnalysisMethod.DeepInspection), CanOperate);
        ConvertDv81Command = new(() => ConvertBatchAsync(ConversionTarget.Profile81), CanOperate);
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

        OpenOutputCommand = new(() => dialogs.OpenFolder(Path.GetDirectoryName(Focused!.Result!.Output!)!), () => Focused?.Result?.Output is not null);
        OpenRowOutputCommand = new(row => dialogs.OpenFolder(Path.GetDirectoryName(row.Result!.Output!)!), row => row is not null && row.CanOpenResult);
        RetryAnalysisCommand = new(row => RunAsync((token, _) => AnalyzeRowsAsync([row], row.LastAnalysisMethod!.Value, token)), row => IsIdle && Files.Contains(row) && row.CanRetryAnalysis && row.LastAnalysisMethod is not null);
        InspectIncompleteCommand = new(row => RunAsync((token, _) => AnalyzeRowsAsync([row], AnalysisMethod.FullRpu, token)), row => IsIdle && Files.Contains(row) && row.CanInspectIncomplete);
        OpenLogsCommand = new(dialogs.OpenLogs);
        ToggleSelectAllCommand = new(ToggleSelectAll, () => IsIdle && Files.Any(row => row.SelectionEnabled));
        ClearAllCommand = new(ClearAll, () => IsIdle && Files.Count > 0);
        OpenSettingsCommand = new(() => RequestNavigateToSettings?.Invoke(), () => IsIdle);
        BatchProgress.PropertyChanged += (_, _) => NotifyActiveProgress();
        Progress.PropertyChanged += (_, _) => NotifyActiveProgress();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Status) or nameof(IsBusy))
            {
                NotifyActiveProgress();
            }
        };
    }
    public ObservableCollection<MediaRow> Files { get; } = [];
    public BatchProgressViewModel BatchProgress { get; } = new();
    public RelayCommand PauseBatchCommand { get; }
    public string PauseBatchText => control?.IsPaused == true ? "Resume" : "Pause";

    public double ActiveProgressPercent => BatchProgress.IsRunning ? BatchProgress.Percent : Percent;
    public bool ActiveProgressIndeterminate => !BatchProgress.IsRunning && IsIndeterminate;
    public string ActiveProgressTitle => BatchProgress.IsRunning
        ? BatchProgress.Operation
        : (string.IsNullOrWhiteSpace(Stage) ? "Working…" : Stage);
    public string ActiveProgressText => BatchProgress.IsRunning
        ? $"{BatchProgress.Percent:0.##}%"
        : Progress.ProgressText;
    public string ActiveProgressSummary => BatchProgress.IsRunning
        ? control?.IsPaused == true
            ? $"{BatchProgress.Summary} · {(BatchProgress.CurrentJob is null ? "Paused" : "Pausing after current job")}"
            : BatchProgress.Summary
        : Status;
    public string? ActiveProgressToolTip => BatchProgress.IsRunning
        ? "Processed includes completed, failed, cancelled and skipped files. Each file has equal weight in the batch."
        : null;

    public MediaRow? Focused
    {
        get => focused;
        set
        {
            SetProperty(ref focused, value);
            OpenOutputCommand.RaiseCanExecuteChanged();
        }
    }

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
    public bool CanInspect => CanOperate();
    public AsyncCommand AddFilesCommand { get; }
    public AsyncCommand AddFolderCommand { get; }
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand InspectCommand { get; }
    public AsyncCommand DeepInspectCommand { get; }
    public AsyncCommand ConvertDv81Command { get; }
    public AsyncCommand ConvertHdrCommand { get; }
    public RelayCommand<MediaRow> SkipCommand { get; }
    public RelayCommand<MediaRow> CancelFileCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public AsyncCommand<MediaRow> RetryAnalysisCommand { get; }
    public AsyncCommand<MediaRow> InspectIncompleteCommand { get; }
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
    }

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

    private bool CanScan() => IsIdle && Files.Count > 0;
    private bool CanOperate() => IsIdle && Files.Any(f => f.IsSelected);

    protected override void CommandsChanged()
    {
        RaisePropertyChanged(nameof(CanInspect));
        RetryAnalysisCommand?.RaiseCanExecuteChanged();
        InspectIncompleteCommand?.RaiseCanExecuteChanged();
        OpenRowOutputCommand?.RaiseCanExecuteChanged();
        ToggleSelectAllCommand?.RaiseCanExecuteChanged();
        ClearAllCommand?.RaiseCanExecuteChanged();
        OpenSettingsCommand?.RaiseCanExecuteChanged();
        AddFilesCommand?.RaiseCanExecuteChanged();
        AddFolderCommand?.RaiseCanExecuteChanged();
        ScanCommand?.RaiseCanExecuteChanged();
        InspectCommand?.RaiseCanExecuteChanged();
        DeepInspectCommand?.RaiseCanExecuteChanged();
        ConvertDv81Command?.RaiseCanExecuteChanged();
        ConvertHdrCommand?.RaiseCanExecuteChanged();
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
                Status = $"Could not add {input}: {ex.Message}";
            }
        }

        Focused ??= Files.FirstOrDefault();
        InvalidatePlan();
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(AllFilesSelected));
        if (added.Count > 0 && (await settings.ReadAsync(token)).AutomaticallyScanAddedFiles)
        {
            await AnalyzeRowsAsync([.. added], AnalysisMethod.SampledRpu, token);
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
            OpenOutputCommand.RaiseCanExecuteChanged();
        }

        if (e.PropertyName is nameof(MediaRow.IsActive) or nameof(MediaRow.IsCancellationRequested))
        {
            CancelFileCommand.RaiseCanExecuteChanged();
        }

        if (e.PropertyName is nameof(MediaRow.CanRetryAnalysis) or nameof(MediaRow.CanOpenResult) or nameof(MediaRow.CanInspectIncomplete))
        {
            InspectIncompleteCommand.RaiseCanExecuteChanged();
            RetryAnalysisCommand.RaiseCanExecuteChanged();
            OpenRowOutputCommand.RaiseCanExecuteChanged();
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

        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(AllFilesSelected));
        CommandsChanged();
    }

    private void InvalidatePlan()
    {
        foreach (var row in Files)
        {
            row.ClearPlan();
        }
    }

    private void Skip(MediaRow row)
    {
        if (row is null || !row.IsPending || control?.Skip(row.Path) != true)
        {
            return;
        }

        row.IsPending = false;
        row.IsSelected = false;
        row.SelectionEnabled = false;
        row.Status = $"{row.OperationName} skipped";
    }

    private void BeginBatch(MediaRow[] rows, string operationName)
    {
        control = new(rows.Select(r => r.Path));
        BatchProgress.Begin(rows.Length, operationName);
        Status = $"{operationName} batch in progress.";
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

    private void EndBatch(bool deselectPending = true)
    {
        BatchProgress.End();
        control?.Dispose();
        control = null;
        NotifyActiveProgress();
        foreach (var row in Files)
        {
            bool wasPending = row.IsPending;
            bool wasActive = row.IsActive;
            if (wasPending)
            {
                row.Status = $"{row.OperationName} cancelled";
                row.CanRetryAnalysis = row.LastAnalysisMethod is not null;
            }

            row.CurrentOperation = null;
            row.IsPending = false;
            row.IsActive = false;
            row.SelectionEnabled = true;

            if (deselectPending && (wasPending || wasActive))
            {
                row.IsSelected = false;
            }
        }
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
        await AnalyzeRowsAsync([.. Files], AnalysisMethod.SampledRpu, token);
    });

    private Task AnalyzeAsync(AnalysisMethod method) => RunAsync(async (token, _) =>
    {
        await AnalyzeRowsAsync([.. Files.Where(r => r.IsSelected)], method, token);
    });

    private async Task AnalyzeRowsAsync(MediaRow[] rows, AnalysisMethod method, CancellationToken token)
    {
        InvalidatePlan();
        var userSettings = await settings.ReadAsync(token);
        bool autoSelect = userSettings.AutoSelectAfterScan;
        bool? toolsReady = null;
        foreach (var row in rows)
        {
            row.LastAnalysisMethod = method;
        }

        BeginBatch(rows, method == AnalysisMethod.SampledRpu ? "Scan" : method == AnalysisMethod.DeepInspection ? "Deep inspection" : "Inspection");
        try
        {
            var results = await batch.ExecuteAsync(rows, row => row.Path, async (row, itemToken) =>
            {
                var jobProgress = Activate(row, method == AnalysisMethod.SampledRpu ? "Scanning" : "Inspecting");
                row.AnalysisError = null;
                row.Notice = "";
                row.Analysis = null;
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

                    return new OperationItemResult(row.Path, row.Analysis.Verdict == AnalysisVerdict.AnalysisFailed ? OperationStatus.Failed : OperationStatus.Completed, null, row.Analysis.Reason);
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<OperationItemResult>(result =>
            {
                BatchProgress.Complete();
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
                else if (result.Status is OperationStatus.Failed or OperationStatus.Cancelled)
                {
                    row.IsSelected = false;
                }
            }), token);
            Status = $"{(method == AnalysisMethod.SampledRpu ? "Scan" : "Inspection")}: {Summary(results)}";
        }
        finally
        {
            EndBatch(deselectPending: true);
        }
    }

    private Task ConvertBatchAsync(ConversionTarget target) => RunAsync(async (token, progress) =>
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
                dialogs.ShowMessage(
                    $"The configured temporary storage folder does not exist:\n{tempPath}\n\nPlease check your Settings.",
                    "Temporary storage folder not found");
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
                dialogs.ShowMessage(
                    $"The configured output folder cannot be created or accessed:\n{userSettings.OutputDirectory}\n\n{ex.Message}",
                    "Output folder error");
                return;
            }
        }

        if (!await dependencies.EnsureAsync(progress, token))
        {
            Status = "Required tools are unavailable.";
            return;
        }

        var rows = Files.Where(r => r.IsSelected).ToArray();
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
                var jobProgress = Activate(row, "Preparing conversion");
                row.AnalysisError = null;
                row.Notice = "";
                row.Warning = null;
                row.PlannedOutput = "";
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
                BatchProgress.Complete();
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
                        row.SetSkipped();
                        row.Notice = itemResult.Message;
                    }
                    else if (itemResult.Status == OperationStatus.Failed)
                    {
                        row.SetFailed(itemResult.Message);
                    }
                    else
                    {
                        row.Status = $"Conversion {itemResult.Status.ToString().ToLowerInvariant()}";
                    }
                }
            }), token);

            Status = $"{opName}: {Summary(result)}";
        }
        finally
        {
            EndBatch();
        }
    });

    private void ClearAll()
    {
        if (!IsIdle)
        {
            return;
        }

        foreach (var row in Files)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Files.Clear();
        Focused = null;
        InvalidatePlan();
        Status = "File list cleared.";
        RaisePropertyChanged(nameof(SelectionSummary));
        RaisePropertyChanged(nameof(AllFilesSelected));
        CommandsChanged();
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

        RaisePropertyChanged(nameof(AllFilesSelected));
    }
    private void NotifyActiveProgress()
    {
        RaisePropertyChanged(nameof(PauseBatchText));
        PauseBatchCommand.RaiseCanExecuteChanged();
        RaisePropertyChanged(nameof(ActiveProgressPercent));
        RaisePropertyChanged(nameof(ActiveProgressIndeterminate));
        RaisePropertyChanged(nameof(ActiveProgressTitle));
        RaisePropertyChanged(nameof(ActiveProgressText));
        RaisePropertyChanged(nameof(ActiveProgressSummary));
        RaisePropertyChanged(nameof(ActiveProgressToolTip));
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
