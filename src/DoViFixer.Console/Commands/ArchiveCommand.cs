using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Scanning;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;
using DoViFixer.Console.Interaction;
using DoViFixer.Console.Rendering;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;

namespace DoViFixer.Console.Commands;

public sealed class ArchiveCommand(BackupService backup, RestoreService restore, ConsoleRenderer renderer, ConsoleInteraction interaction) : IConsoleCommand
{
    public bool Handles(string command) => command is "backup" or "restore";
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        string input = command.Arguments.FirstOrDefault() ?? Environment.CurrentDirectory;
        switch (command.Command)
        {
            case "backup":
                var backupPlan = await backup.PlanAsync(input, command.Value("output"), command.Value("temp"), cancellationToken);
                interaction.RecordPlan(backupPlan.Id, "Backup", new { backupPlan.Media.Source, backupPlan.Output, backupPlan.TemporaryDirectory, backupPlan.ScratchBytes });
                renderer.Write($"Backup: {backupPlan.Media.Source.Path}\nArchive: {backupPlan.Output}\nScratch estimate: {backupPlan.ScratchBytes:N0} bytes. Original retained.");
                if (command.Has("plan"))
                {
                    return 0;
                }
                if (!await interaction.ConfirmAsync("Create this archive?", command.Has("yes"), cancellationToken, backupPlan.Id))
                {
                    return 4;
                }
                renderer.Result(await backup.ExecuteAsync(backupPlan, renderer, cancellationToken));
                return 0;
            case "restore":
                var restorePlan = await restore.PlanAsync(input, command.Arguments[1], command.Value("output"), command.Value("temp"), command.Has("allow-legacy-archive"), cancellationToken);
                interaction.RecordPlan(restorePlan.Id, "Restore", new { restorePlan.Media.Source, restorePlan.Archive, restorePlan.Output, restorePlan.TemporaryDirectory, restorePlan.ScratchBytes, restorePlan.AllowLegacy });
                renderer.Write($"Base: {restorePlan.Media.Source.Path}\nArchive: {restorePlan.Archive.Path}\nOutput: {restorePlan.Output}\nScratch estimate: {restorePlan.ScratchBytes:N0} bytes. Original retained.");
                if (restorePlan.AllowLegacy)
                {
                    renderer.Write("Legacy archives are allowed: source pairing cannot be verified without a manifest.");
                }
                if (command.Has("plan"))
                {
                    return 0;
                }
                if (!await interaction.ConfirmAsync("Restore this file?", command.Has("yes"), cancellationToken, restorePlan.Id))
                {
                    return 4;
                }
                renderer.Result(await restore.ExecuteAsync(restorePlan, renderer, cancellationToken));
                return 0;
            default:
                throw new ArgumentException("Unsupported command.");
        }
    }
}
