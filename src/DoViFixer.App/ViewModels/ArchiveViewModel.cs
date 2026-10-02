using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.ViewModels;
public sealed class ArchiveViewModel : OperationViewModel, INavigationPage
{
    public string NavigationName => "Backup & Restore";
    public string? NavigationIcon => "\uE8B7";

    private string input = "";
    private string archive = "";
    private string output = "";
    private bool allowLegacy;
    public ArchiveViewModel(BackupService backup, RestoreService restore, CleanupService cleanup, DependencySetup dependencies, IDialogService dialogs, IFileDialogService files, ILogger<ArchiveViewModel> logger)
    {
        BrowseInputCommand = new(() =>
        {
            Input = files.PickFiles("Matroska media|*.mkv", allowMultiple: false).FirstOrDefault() ?? Input;
        });
        BrowseArchiveCommand = new(() =>
        {
            Archive = files.PickFiles("EL archives|*.dovi", allowMultiple: false).FirstOrDefault() ?? Archive;
        });
        BrowseOutputCommand = new(() =>
        {
            Output = files.PickFolder() ?? Output;
        });
        BackupCommand = new(() => RunAsync(async (token, progress) =>
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
            SetStatus(ViewStatus.Result, $"{result.Status}\n{result.Output}\n{result.Message}");
        }), () => IsIdle && !string.IsNullOrWhiteSpace(Input));
        RestoreCommand = new(() => RunAsync(async (token, progress) =>
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
            SetStatus(ViewStatus.Result, $"{result.Status}\n{result.Output}\n{result.Message}");
        }), () => IsIdle && !string.IsNullOrWhiteSpace(Input) && !string.IsNullOrWhiteSpace(Archive));
        CleanupCommand = new(() => RunAsync(async (token, _) =>
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
            SetStatus(ViewStatus.Result, string.Join("\n", result.Items.Select(f => $"{f.Item}: {f.Status} — {f.Message}")));
        }), () => IsIdle);
    }

    private static string? Empty(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
    public string Input
    {
        get => input;
        set
        {
            SetProperty(ref input, value);
            CommandsChanged();
        }
    }

    public string Archive
    {
        get => archive;
        set
        {
            SetProperty(ref archive, value);
            CommandsChanged();
        }
    }

    public string Output
    {
        get => output;
        set => SetProperty(ref output, value);
    }
    public bool AllowLegacy
    {
        get => allowLegacy;
        set => SetProperty(ref allowLegacy, value);
    }
    public RelayCommand BrowseInputCommand
    {
        get;
    }
    public RelayCommand BrowseArchiveCommand
    {
        get;
    }
    public RelayCommand BrowseOutputCommand
    {
        get;
    }
    public AsyncRelayCommand BackupCommand
    {
        get;
    }
    public AsyncRelayCommand RestoreCommand
    {
        get;
    }
    public AsyncRelayCommand CleanupCommand
    {
        get;
    }

    protected override void CommandsChanged()
    {
        BackupCommand?.NotifyCanExecuteChanged();
        RestoreCommand?.NotifyCanExecuteChanged();
        CleanupCommand?.NotifyCanExecuteChanged();
    }
}
