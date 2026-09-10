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

public sealed class CleanupCommand(CleanupService cleanup, ConsoleRenderer renderer, ConsoleInteraction interaction) : IConsoleCommand
{
    public bool Handles(string command) => command == "cleanup";
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        string input = command.Arguments.FirstOrDefault() ?? Environment.CurrentDirectory;
        switch (command.Command)
        {
            case "cleanup":
                var cleanupPlan = cleanup.Plan(input, command.Depth);
                foreach (var file in cleanupPlan.Files)
                {
                    interaction.RecordPlan(cleanupPlan.Id, "DeleteBackup", file);
                    renderer.Write($"{file.Path} ({ConsoleRenderer.FormatSize(file.Length)})");
                }
                renderer.Write($"{cleanupPlan.Files.Count} backup(s) selected.");
                if (!command.Has("delete-backups") || cleanupPlan.Files.Count == 0)
                {
                    return 0;
                }
                if (!await interaction.ConfirmDeletionAsync(cleanupPlan.Id, command.Has("yes"), cancellationToken))
                {
                    return 4;
                }
                var deleted = await cleanup.ExecuteAsync(cleanupPlan, cancellationToken);
                foreach (var file in deleted.Files)
                {
                    renderer.Result(file);
                }
                return CommandDispatcher.ExitCode(deleted.Status);
            default:
                throw new ArgumentException("Unsupported command.");
        }
    }
}
