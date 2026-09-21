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
    private readonly ConversionPlanner planner;
    private readonly ConversionService conversion;
    private readonly ControlledBatchService batch;
    private readonly SettingsService settings;
    private readonly DependencySetup dependencies;
    private readonly IUserDialogs dialogs;
    private readonly ILogger<MediaViewModel> logger;
    private readonly List<ConversionPlan> plans = [];
    private BatchControl? control;
    private MediaRow? focused;
    private bool optionsOpen;
    private bool hdr10;
    private bool otherFolder;
    private bool replaceOriginal;
    private bool createArchive;
    private string destination = "";
    private string review = "";
    public MediaViewModel(IFileDiscovery discovery, InspectionService inspection, ConversionPlanner planner, ConversionService conversion, ControlledBatchService batch, SettingsService settings, DependencySetup dependencies, IUserDialogs dialogs, ILogger<MediaViewModel> logger)
    {
        this.discovery = discovery;
        this.inspection = inspection;
        this.planner = planner;
        this.conversion = conversion;
        this.batch = batch;
        this.settings = settings;
        this.dependencies = dependencies;
        this.dialogs = dialogs;
        this.logger = logger;
        AddFilesCommand = new(() => AddAsync(dialogs.PickFiles()), () => IsIdle);
        AddFolderCommand = new(() => AddAsync(dialogs.PickFolder()is
        {
        }
        folder ? [folder] : []), () => IsIdle);
        ScanCommand = new(() => AnalyzeAsync(AnalysisMethod.SampledRpu), CanOperate);
        InspectCommand = new(() => AnalyzeAsync(AnalysisMethod.FullRpu), CanOperate);
        DeepInspectCommand = new(() => AnalyzeAsync(AnalysisMethod.DeepInspection), CanOperate);
        ConvertCommand = new(OpenConversionAsync, CanOperate);
        ReviewCommand = new(PrepareAsync, CanOperate);
        ApproveCommand = new(ConvertAsync, () => IsIdle && plans.Count > 0);
        CloseOptionsCommand = new(() => OptionsOpen = false);
        BrowseDestinationCommand = new(() => {
            if (dialogs.PickFolder() is { } folder)
            {
                Destination = folder;
            }
        }, () => IsIdle);

        SkipCommand = new DelegateCommand<MediaRow>(Skip);

        CancelFileCommand = new DelegateCommand<MediaRow>(row =>
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
    public DelegateCommand PauseBatchCommand { get; }
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

    public MediaRow? Focused
    {
        get => focused;
        set
        {
            SetProperty(ref focused, value);
            OpenOutputCommand.RaiseCanExecuteChanged();
        }
    }

    public bool OptionsOpen
    {
        get => optionsOpen;
        set => SetProperty(ref optionsOpen, value);
    }

    public bool Hdr10
    {
        get => hdr10;
        set
        {
            if (SetProperty(ref hdr10, value))
            {
                InvalidatePlan();
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
                InvalidatePlan();
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
                InvalidatePlan();
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
                InvalidatePlan();
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
                InvalidatePlan();
            }
        }
    }

    public string Review
    {
        get => review;
        private set => SetProperty(ref review, value);
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
    public bool? AllFilesSelected => Files.Count == 0 || Files.All(row => !row.IsSelected)
        ? false
        : Files.All(row => row.IsSelected) ? true : null;

    public DelegateCommand ToggleSelectAllCommand
    {
        get;
    }

    public DelegateCommand ClearAllCommand
    {
        get;
    }

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
        OptionsOpen = false;
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

    public bool CanInspect => CanOperate();
    public AsyncCommand AddFilesCommand
    {
        get;
    }
    public AsyncCommand AddFolderCommand
    {
        get;
    }
    public AsyncCommand ScanCommand
    {
        get;
    }
    public AsyncCommand InspectCommand
    {
        get;
    }
    public AsyncCommand DeepInspectCommand
    {
        get;
    }
    public AsyncCommand ConvertCommand
    {
        get;
    }
    public AsyncCommand ReviewCommand
    {
        get;
    }
    public AsyncCommand ApproveCommand
    {
        get;
    }
    public DelegateCommand CloseOptionsCommand
    {
        get;
    }
    public DelegateCommand BrowseDestinationCommand
    {
        get;
    }
    public DelegateCommand<MediaRow> SkipCommand
    {
        get;
    }
    public DelegateCommand<MediaRow> CancelFileCommand
    {
        get;
    }
    public DelegateCommand OpenOutputCommand
    {
        get;
    }
    public DelegateCommand OpenLogsCommand
    {
        get;
    }

    private bool CanOperate() => IsIdle && Files.Any(f => f.IsSelected);
    public AsyncCommand<MediaRow> RetryAnalysisCommand
    {
        get;
    }
    public AsyncCommand<MediaRow> InspectIncompleteCommand
    {
        get;
    }
    public DelegateCommand<MediaRow> OpenRowOutputCommand
    {
        get;
    }
    protected override void CommandsChanged()
    {
        RaisePropertyChanged(nameof(CanInspect));
        RetryAnalysisCommand?.RaiseCanExecuteChanged();
        InspectIncompleteCommand?.RaiseCanExecuteChanged();
        OpenRowOutputCommand?.RaiseCanExecuteChanged();
        ToggleSelectAllCommand?.RaiseCanExecuteChanged();
        ClearAllCommand?.RaiseCanExecuteChanged();
        AddFilesCommand?.RaiseCanExecuteChanged();
        AddFolderCommand?.RaiseCanExecuteChanged();
        ScanCommand?.RaiseCanExecuteChanged();
        InspectCommand?.RaiseCanExecuteChanged();
        DeepInspectCommand?.RaiseCanExecuteChanged();
        ConvertCommand?.RaiseCanExecuteChanged();
        ReviewCommand?.RaiseCanExecuteChanged();
        ApproveCommand?.RaiseCanExecuteChanged();
        BrowseDestinationCommand?.RaiseCanExecuteChanged();
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
            catch (Exception ex)when (ex is not OperationCanceledException)
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
            await AnalyzeRowsAsync(added.ToArray(), AnalysisMethod.SampledRpu, token);
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
        plans.Clear();
        foreach (var row in Files)
        {
            row.ClearPlan();
        }

        Review = "Review the current options to prepare exact output paths and warnings.";
        ApproveCommand?.RaiseCanExecuteChanged();
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

    private Task AnalyzeAsync(AnalysisMethod method) => RunAsync(async (token, _) =>
    {
        await AnalyzeRowsAsync(Files.Where(r => r.IsSelected).ToArray(), method, token);
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
                        row.IsSelected = ConversionPolicy.ShouldAutoSelectAfterAnalysis(row.Analysis);
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

    private async Task OpenConversionAsync()
    {
        await RunAsync(async (token, _) =>
        {
            var defaults = await settings.ReadAsync(token);
            OtherFolder = defaults.OutputDirectory is not null;
            Destination = defaults.OutputDirectory ?? "";
            ReplaceOriginal = defaults.ReplaceOriginal;
            CreateArchive = defaults.CreateElArchive;
            OptionsOpen = true;
        });
        await PrepareAsync();
    }

    private Task PrepareAsync() => RunAsync(async (token, progress) =>
    {
        InvalidatePlan();
        if (OtherFolder && string.IsNullOrWhiteSpace(Destination))
        {
            throw new InvalidOperationException("Choose an output folder.");
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

        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedResults = new List<OperationItemResult>();
        bool targetHdr10 = Hdr10;
        string? outputDir = OtherFolder ? Destination : null;
        bool deleteBackup = ReplaceOriginal;
        bool createArchive = CreateArchive;

        BeginBatch(rows, "Conversion planning");
        try
        {
            var results = await batch.ExecuteAsync(rows, row => row.Path, async (row, itemToken) =>
            {
                var jobProgress = Activate(row, "Planning");
                row.AnalysisError = null;
                row.Notice = "";
                row.Warning = null;
                row.PlannedOutput = "";
                try
                {
                    var request = new ConversionRequest(
                        row.Path,
                        Target: targetHdr10 ? ConversionTarget.Hdr10 : ConversionTarget.Profile81,
                        OutputDirectory: outputDir,
                        IncludeSimple: true,
                        ForceComplex: true,
                        CreateBackup: createArchive,
                        DeleteBackup: deleteBackup
                    );
                    var prepared = await Task.Run(() => planner.PlanAsync(request, jobProgress, itemToken, existingOutputs: outputs), itemToken);
                    if (prepared.Plans.Count > 0)
                    {
                        var plan = prepared.Plans[0];
                        plans.Add(plan);
                        row.Analysis = plan.Analysis;
                        string? warning = plan.Analysis.Verdict is AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel or AnalysisVerdict.FelUnclassified
                            ? "Enhancement-layer picture data will be lost."
                            : null;
                        row.SetPlan(plan.Output, warning);
                        return new OperationItemResult(row.Path, OperationStatus.Completed, plan.Output, plan.Decision);
                    }

                    if (prepared.Skipped.Count > 0)
                    {
                        var skipped = prepared.Skipped[0];
                        skippedResults.Add(skipped);
                        return skipped;
                    }

                    return new OperationItemResult(row.Path, OperationStatus.Failed, null, "Conversion planning produced no plan.");
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<OperationItemResult>(result =>
            {
                BatchProgress.Complete();
                var row = rows.First(r => r.Path == result.Item);
                row.CanRetryAnalysis = false;
                if (result.Status == OperationStatus.Completed)
                {
                    row.Status = "Ready to convert";
                }
                else
                {
                    row.Status = $"Conversion planning {result.Status.ToString().ToLowerInvariant()}";
                    row.Notice = result.Message;
                    row.Warning = null;
                    if (result.Status == OperationStatus.Failed)
                    {
                        row.AnalysisError = result.Message;
                    }
                }
            }), token);

            string warnings = string.Join("\n", plans.Where(p => p.Analysis.Verdict is AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel or AnalysisVerdict.FelUnclassified).Select(p => $"WARNING — {Path.GetFileName(p.Analysis.Media.Source.Path)}: Enhancement-layer picture data will be lost."));
            var planText = string.Join("\n\n", plans.Select(p =>
            {
                string tempFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(p.TemporaryDirectory) ? Path.GetTempPath() : p.TemporaryDirectory);
                return $"{p.Analysis.Media.Source.Path}\n→ {p.Output}\nTarget: {(p.Target == ConversionTarget.Profile81 ? "Profile 8.1" : "HDR10")}; original: {(p.DeleteBackup ? "replace after verification; original backup deleted" : "retained")}\n" +
                    $"Temporary folder: {tempFolder}\n" +
                    "A separate job subfolder is created here when conversion starts.\n" +
                    $"Required scratch space for this file: {p.ScratchBytes / 1073741824d:0.0} GiB ({p.ScratchBytes:N0} bytes)\nOutput space is additional.\n" +
                    (p.Archive is null ? "" : $"EL archive: {p.Archive}\n") + p.Decision;
            }));
            var skipText = string.Join("\n", skippedResults.Select(s => $"{s.Item}: {s.Status} — {s.Message}"));
            Review = (warnings.Length == 0 ? "" : warnings + "\n\n") + planText + (planText.Length > 0 && skipText.Length > 0 ? "\n\n" : "") + skipText;
            Status = $"{plans.Count} plans ready for approval.";
        }
        finally
        {
            EndBatch(deselectPending: true);
            ApproveCommand?.RaiseCanExecuteChanged();
        }
    });
    private Task ConvertAsync() => RunAsync(async (token, _) =>
    {
        var approved = plans.ToArray();
        plans.Clear();
        foreach (var plan in approved)
        {
            OperationLog.Audit(logger, "ApproveConversion", Review, "ApprovedByButton", plan.Id);
        }

        OptionsOpen = false;
        var rows = Files.Where(r => approved.Any(p => p.Analysis.Media.Source.Path == r.Path)).ToArray();
        foreach (var row in rows)
        {
            row.LastAnalysisMethod = null;
            row.Warning = null;
        }

        BeginBatch(rows, "Conversion");
        try
        {
            var result = await batch.ExecuteAsync(approved, p => p.Analysis.Media.Source.Path, async (plan, itemToken) =>
            {
                var row = rows.First(r => r.Path == plan.Analysis.Media.Source.Path);
                var jobProgress = Activate(row, "Preparing conversion");
                try
                {
                    return await Task.Run(() => conversion.ExecuteAsync(plan, jobProgress, itemToken), itemToken);
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<OperationItemResult>(result =>
            {
                BatchProgress.Complete();
                var row = rows.First(r => r.Path == result.Item);
                if (result.Status is OperationStatus.Completed or OperationStatus.Partial)
                {
                    row.SetConverted(result);
                }
                else
                {
                    row.Result = result;
                    row.Status = $"Conversion {result.Status.ToString().ToLowerInvariant()}";
                }
            }), token);
            Status = $"Conversion: {Summary(result)}";
        }
        finally
        {
            EndBatch();
        }
    });
    private static string Summary(BatchResult result) => string.Join(" · ", result.Items.GroupBy(r => r.Status).Select(g => $"{g.Count()} {g.Key}"));
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
