using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation;
using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.ViewModels;
public sealed class ArchiveViewModel : OperationViewModel
{
    private string input = "";
    private string archive = "";
    private string output = "";
    private bool allowLegacy;
    public ArchiveViewModel(BackupService backup, RestoreService restore, CleanupService cleanup, DependencySetup dependencies, IUserDialogs dialogs, ILogger<ArchiveViewModel> logger)
    {
        BrowseInputCommand = new(() =>
        {
            Input = dialogs.PickFiles().FirstOrDefault() ?? Input;
        });
        BrowseArchiveCommand = new(() =>
        {
            Archive = dialogs.PickFiles("EL archives|*.dovi").FirstOrDefault() ?? Archive;
        });
        BrowseOutputCommand = new(() =>
        {
            Output = dialogs.PickFolder() ?? Output;
        });
        BackupCommand = new(() => RunAsync(async (token, progress) =>
        {
            if (!await dependencies.EnsureAsync(progress, token))
            {
                Status = "Required tools are unavailable.";
                return;
            }

            var plan = await Task.Run(() => backup.PlanAsync(Input, Empty(Output), null, token), token);
            string review = $"Input: {plan.Media.Source.Path}\nArchive: {plan.Output}\nScratch: {plan.ScratchBytes / 1073741824d:0.0} GiB\nOriginal retained.";
            if (!dialogs.Review("Review enhancement-layer backup", review, "Approve and start"))
            {
                Status = "Backup not approved.";
                return;
            }

            OperationLog.Audit(logger, "ApproveBackup", review, "ApprovedByDialog", plan.Id);
            var result = await Task.Run(() => backup.ExecuteAsync(plan, progress, token), token);
            Status = $"{result.Status}\n{result.Output}\n{result.Message}";
        }), () => IsIdle && !string.IsNullOrWhiteSpace(Input));
        RestoreCommand = new(() => RunAsync(async (token, progress) =>
        {
            if (!await dependencies.EnsureAsync(progress, token))
            {
                Status = "Required tools are unavailable.";
                return;
            }

            var plan = await Task.Run(() => restore.PlanAsync(Input, Archive, Empty(Output), null, AllowLegacy, token), token);
            string review = $"Base file: {plan.Media.Source.Path}\nArchive: {plan.Archive.Path}\nOutput: {plan.Output}\nScratch: {plan.ScratchBytes / 1073741824d:0.0} GiB\n" + (plan.AllowLegacy ? "WARNING: Legacy archives lack verified source pairing.\n" : "Verified source pairing required.\n") + "Original retained.";
            if (!dialogs.Review("Review restoration", review, "Approve and start"))
            {
                Status = "Restore not approved.";
                return;
            }

            OperationLog.Audit(logger, "ApproveRestore", review, "ApprovedByDialog", plan.Id);
            var result = await Task.Run(() => restore.ExecuteAsync(plan, progress, token), token);
            Status = $"{result.Status}\n{result.Output}\n{result.Message}";
        }), () => IsIdle && !string.IsNullOrWhiteSpace(Input) && !string.IsNullOrWhiteSpace(Archive));
        CleanupCommand = new(() => RunAsync(async (token, _) =>
        {
            string? folder = dialogs.PickFolder();
            if (folder is null)
            {
                return;
            }

            var plan = await Task.Run(() => cleanup.Plan(folder, 100), token);
            if (plan.Files.Count == 0)
            {
                Status = "No retained backup files found.";
                return;
            }

            string review = "Permanently delete exactly these retained backup files:\n\n" + string.Join("\n", plan.Files.Select(f => $"{f.Path} ({f.Length:N0} bytes)"));
            string code = "APPROVE " + plan.Id.ToString("N")[..8].ToUpperInvariant();
            if (!dialogs.Review("Review cleanup", review, "Delete displayed backups", code))
            {
                Status = "Cleanup not approved.";
                return;
            }

            OperationLog.Audit(logger, "ApproveCleanup", review, "ApprovedByExactCode", plan.Id);
            var result = await Task.Run(() => cleanup.ExecuteAsync(plan, token), token);
            Status = string.Join("\n", result.Items.Select(f => $"{f.Item}: {f.Status} — {f.Message}"));
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
    public DelegateCommand BrowseInputCommand
    {
        get;
    }
    public DelegateCommand BrowseArchiveCommand
    {
        get;
    }
    public DelegateCommand BrowseOutputCommand
    {
        get;
    }
    public AsyncCommand BackupCommand
    {
        get;
    }
    public AsyncCommand RestoreCommand
    {
        get;
    }
    public AsyncCommand CleanupCommand
    {
        get;
    }

    protected override void CommandsChanged()
    {
        BackupCommand?.RaiseCanExecuteChanged();
        RestoreCommand?.RaiseCanExecuteChanged();
        CleanupCommand?.RaiseCanExecuteChanged();
    }
}
