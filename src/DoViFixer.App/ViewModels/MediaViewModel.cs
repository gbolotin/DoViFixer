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
            if (row is not null)
            {
                control?.Cancel(row.Path);
            }
        });

        OpenOutputCommand = new(() => dialogs.OpenFolder(Path.GetDirectoryName(Focused!.Result!.Output!)!), () => Focused?.Result?.Output is not null);
        OpenLogsCommand = new(dialogs.OpenLogs);
        ToggleSelectAllCommand = new(ToggleSelectAll, () => IsIdle && Files.Any(row => row.SelectionEnabled));
        ClearAllCommand = new(ClearAll, () => IsIdle && Files.Count > 0);
    }

    public ObservableCollection<MediaRow> Files { get; } = [];

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
    public string SelectionSummary => $"{Files.Count(f => f.IsSelected)} selected / {Files.Count(f => !f.IsSelected)} excluded";
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
    protected override void CommandsChanged()
    {
        RaisePropertyChanged(nameof(CanInspect));
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

        if (e.PropertyName != nameof(MediaRow.IsSelected))
        {
            return;
        }

        if (IsBusy && !row.IsSelected)
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
            row.PlannedOutput = "";
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
        row.Status = "Skipped";
    }

    private void BeginBatch(MediaRow[] rows)
    {
        control = new(rows.Select(r => r.Path));
        foreach (var row in Files)
        {
            row.SelectionEnabled = false;
        }

        foreach (var row in rows)
        {
            row.IsPending = true;
            row.SelectionEnabled = true;
            row.Status = "Pending";
        }
    }

    private void EndBatch()
    {
        control?.Dispose();
        control = null;
        foreach (var row in Files)
        {
            if (row.IsPending)
            {
                row.Status = "Not run";
            }

            row.IsPending = false;
            row.IsActive = false;
            row.SelectionEnabled = true;
        }
    }

    private static void Activate(MediaRow row, string stage)
    {
        row.IsPending = false;
        row.IsActive = true;
        row.SelectionEnabled = false;
        row.Status = stage;
    }

    protected override void OnProgress(OperationProgress progress)
    {
        var row = Files.FirstOrDefault(f => f.IsActive);
        if (row is not null && (progress.File == row.Path || progress.File == row.Path + ".bak.dovi_convert"))
        {
            row.Status = progress.Stage;
        }
    }

    private Task AnalyzeAsync(AnalysisMethod method) => RunAsync(async (token, progress) =>
    {
        InvalidatePlan();
        if (!await dependencies.EnsureAsync(progress, token))
        {
            Status = "Required tools are unavailable.";
            return;
        }

        var rows = Files.Where(r => r.IsSelected).ToArray();
        BeginBatch(rows);
        try
        {
            var results = await batch.ExecuteAsync(rows, row => row.Path, async (row, itemToken) =>
            {
                Activate(row, method == AnalysisMethod.SampledRpu ? "Analyzing" : "Inspecting");
                row.AnalysisError = null;
                row.Notice = "";
                row.Analysis = null;
                try
                {
                    row.Analysis = await Task.Run(() => inspection.InspectAsync(row.Path, method, null, itemToken), itemToken);
                    return new FileResult(row.Path, row.Analysis.Verdict == AnalysisVerdict.AnalysisFailed ? OperationStatus.Failed : OperationStatus.Completed, null, row.Analysis.Reason);
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<FileResult>(result =>
            {
                var row = rows.First(r => r.Path == result.Input);
                row.Status = result.Status.ToString();
                if (result.Status == OperationStatus.Failed)
                {
                    row.AnalysisError = result.Message;
                }
                else if (result.Status == OperationStatus.Cancelled)
                {
                    row.Notice = result.Message;
                }
            }), token);
            Status = Summary(results);
        }
        finally
        {
            EndBatch();
        }
    });

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
        foreach (var row in Files)
        {
            row.SelectionEnabled = false;
        }

        try
        {
            var request = new ConversionRequest(rows[0].Path, Target: Hdr10 ? ConversionTarget.Hdr10 : ConversionTarget.Profile81, OutputDirectory: OtherFolder ? Destination : null, IncludeSimple: true, ForceComplex: true, CreateBackup: CreateArchive, DeleteBackup: ReplaceOriginal, AdditionalInputs: rows.Skip(1).Select(r => r.Path).ToArray());
            var prepared = await Task.Run(() => planner.PlanAsync(request, progress, token), token);
            plans.AddRange(prepared.Plans);
            foreach (var plan in plans)
            {
                var row = rows.First(r => r.Path == plan.Analysis.Media.Source.Path);
                row.AnalysisError = null;
                row.Notice = "";
                row.Analysis = plan.Analysis;
                row.PlannedOutput = plan.Output;
                row.Status = "Ready";
            }

            foreach (var skipped in prepared.Skipped)
            {
                var row = rows.FirstOrDefault(r => r.Path == skipped.Input);
                if (row is not null)
                {
                    row.Status = skipped.Status.ToString();
                    row.Notice = skipped.Message;
                }
            }

            string warnings = string.Join("\n", plans.Where(p => p.Analysis.Verdict is AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel).Select(p => $"WARNING — {Path.GetFileName(p.Analysis.Media.Source.Path)}: Enhancement-layer picture data will be lost."));
            Review = (warnings.Length == 0 ? "" : warnings + "\n\n") + string.Join("\n\n", plans.Select(p => $"{p.Analysis.Media.Source.Path}\n→ {p.Output}\nTarget: {(p.Target == ConversionTarget.Profile81 ? "Profile 8.1" : "HDR10")}; original: {(p.DeleteBackup ? "replace after verification; original backup deleted" : "retained")}\n" + (p.Archive is null ? "" : $"EL archive: {p.Archive}\n") + p.Decision)) + "\n" + string.Join("\n", prepared.Skipped.Select(s => $"{s.Input}: {s.Status} — {s.Message}"));
            Status = $"{plans.Count} plans ready for approval.";
        }
        finally
        {
            foreach (var row in Files)
            {
                row.SelectionEnabled = true;
            }
        }
    });
    private Task ConvertAsync() => RunAsync(async (token, progress) =>
    {
        var approved = plans.ToArray();
        plans.Clear();
        foreach (var plan in approved)
        {
            OperationLog.Audit(logger, "ApproveConversion", Review, "ApprovedByButton", plan.Id);
        }

        OptionsOpen = false;
        var rows = Files.Where(r => approved.Any(p => p.Analysis.Media.Source.Path == r.Path)).ToArray();
        BeginBatch(rows);
        try
        {
            var result = await batch.ExecuteAsync(approved, p => p.Analysis.Media.Source.Path, async (plan, itemToken) =>
            {
                var row = rows.First(r => r.Path == plan.Analysis.Media.Source.Path);
                Activate(row, "Preparing");
                try
                {
                    return await Task.Run(() => conversion.ExecuteAsync(plan, progress, itemToken), itemToken);
                }
                finally
                {
                    row.IsActive = false;
                }
            }, control!, new InlineProgress<FileResult>(result =>
            {
                var row = rows.First(r => r.Path == result.Input);
                row.Result = result;
                row.Status = result.Status.ToString();
            }), token);
            Status = Summary(result);
        }
        finally
        {
            EndBatch();
        }
    });
    private static string Summary(BatchResult result) => string.Join(" · ", result.Files.GroupBy(r => r.Status).Select(g => $"{g.Count()} {g.Key}"));
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
