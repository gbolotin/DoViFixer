using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;
using WpfFoundation.Operations;

namespace DoViFixer.App.ViewModels;
public sealed partial class ArchiveViewModel(BackupService backup, RestoreService restore, CleanupService cleanup, DependencySetup dependencies, IDialogService dialogs, IFileDialogService files, ILogger<ArchiveViewModel> logger, IOperationFeedback? feedback = null)
    : OperationViewModel(feedback), INavigationPage
{
    public string NavigationName => "Backup & Restore";
    public string? NavigationIcon => "";

    private static string? Empty(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand), nameof(RestoreCommand))]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial string Archive { get; set; } = "";

    [ObservableProperty]
    public partial string Output { get; set; } = "";

    [ObservableProperty]
    public partial bool AllowLegacy { get; set; }

    [RelayCommand]
    private void BrowseInput() => Input = files.PickFiles("Matroska media|*.mkv", allowMultiple: false).FirstOrDefault() ?? Input;

    [RelayCommand]
    private void BrowseArchive() => Archive = files.PickFiles("EL archives|*.dovi", allowMultiple: false).FirstOrDefault() ?? Archive;

    [RelayCommand]
    private void BrowseOutput() => Output = files.PickFolder() ?? Output;

    private bool CanBackup() => IsIdle && !string.IsNullOrWhiteSpace(Input);

    [RelayCommand(CanExecute = nameof(CanBackup))]
    private Task Backup() => RunAsync("Backup", async (token, progress) =>
    {
        if (!await dependencies.EnsureAsync(progress, token))
        {
            SetStatus(ViewStatus.ToolsUnavailable);
            return;
        }

        var plan = await Task.Run(() => backup.PlanAsync(Input, Empty(Output), null, token), token);
        string review = $"Input: {plan.Media.Source.Path}\nArchive: {plan.Output}\nScratch: {plan.ScratchBytes / 1073741824d:0.0} GiB\nOriginal retained.";
        if (!await dialogs.ReviewAsync("Review enhancement-layer backup", review, "Approve and start"))
        {
            SetStatus(ViewStatus.BackupNotApproved);
            return;
        }

        OperationLog.Audit(logger, "ApproveBackup", review, "ApprovedByDialog", plan.Id);
        var result = await Task.Run(() => backup.ExecuteAsync(plan, progress, token), token);
        SetStatus(result.Status == OperationStatus.Failed ? ViewStatus.Error : ViewStatus.Result, $"{result.Status}\n{result.Output}\n{result.Message}");
    });

    private bool CanRestore() => IsIdle && !string.IsNullOrWhiteSpace(Input) && !string.IsNullOrWhiteSpace(Archive);

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task Restore() => RunAsync("Restoration", async (token, progress) =>
    {
        if (!await dependencies.EnsureAsync(progress, token))
        {
            SetStatus(ViewStatus.ToolsUnavailable);
            return;
        }

        var plan = await Task.Run(() => restore.PlanAsync(Input, Archive, Empty(Output), null, AllowLegacy, token), token);
        string review = $"Base file: {plan.Media.Source.Path}\nArchive: {plan.Archive.Path}\nOutput: {plan.Output}\nScratch: {plan.ScratchBytes / 1073741824d:0.0} GiB\n" + (plan.AllowLegacy ? "WARNING: Legacy archives lack verified source pairing.\n" : "Verified source pairing required.\n") + "Original retained.";
        if (!await dialogs.ReviewAsync("Review restoration", review, "Approve and start"))
        {
            SetStatus(ViewStatus.RestoreNotApproved);
            return;
        }

        OperationLog.Audit(logger, "ApproveRestore", review, "ApprovedByDialog", plan.Id);
        var result = await Task.Run(() => restore.ExecuteAsync(plan, progress, token), token);
        SetStatus(result.Status == OperationStatus.Failed ? ViewStatus.Error : ViewStatus.Result, $"{result.Status}\n{result.Output}\n{result.Message}");
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task Cleanup() => RunAsync("Cleanup", async (token, _) =>
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

        string review = "Permanently delete exactly these retained backup files:\n\n" + string.Join("\n", plan.Files.Select(f => $"{f.Path} ({f.Length:N0} bytes)"));
        if (!await dialogs.ReviewAsync("Review cleanup", review, "Delete"))
        {
            SetStatus(ViewStatus.CleanupNotApproved);
            return;
        }

        OperationLog.Audit(logger, "ApproveCleanup", review, "ApprovedByConfirmation", plan.Id);
        var result = await Task.Run(() => cleanup.ExecuteAsync(plan, token), token);
        if (result.Items.Any(item => item.Status == OperationStatus.Failed))
        {
            ReportItemFailed();
        }

        SetStatus(ViewStatus.Result, string.Join("\n", result.Items.Select(f => $"{f.Item}: {f.Status} — {f.Message}")));
    });

    protected override void CommandsChanged()
    {
        BackupCommand?.NotifyCanExecuteChanged();
        RestoreCommand?.NotifyCanExecuteChanged();
        CleanupCommand?.NotifyCanExecuteChanged();
    }
}
