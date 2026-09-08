using DoViFixer.Application.Abstractions;
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

public sealed class SettingsCommand(SettingsService settings, IUserPathRegistration userPath, ConsoleRenderer renderer) : IConsoleCommand
{
    public bool Handles(string command) => command == "settings";
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        switch (command.Arguments[0])
        {
            case "add-to-path":
                string directory = AppContext.BaseDirectory;
                bool added = userPath.AddDirectory(directory, cancellationToken);
                renderer.Write(added ? $"Added to user PATH: {directory}" : $"Already in user PATH: {directory}");
                renderer.Write("Applied to this process. Reopen your terminal (and its host app if needed), then run DoViFixer.Console --help.");
                break;
            case "show":
                renderer.Json(await settings.ReadAsync(cancellationToken));
                break;
            case "temp":
                await settings.SetTemporaryDirectoryAsync(command.Arguments[1], cancellationToken);
                renderer.Write("Temporary directory saved.");
                break;
            case "tool":
                await settings.SetToolAsync(Enum.Parse<NativeTool>(command.Arguments[1], true), command.Arguments[2], cancellationToken);
                renderer.Write("Validated tool path saved and applied to the current process.");
                break;
            case "reset-tool":
                await settings.ResetToolAsync(Enum.Parse<NativeTool>(command.Arguments[1], true), cancellationToken);
                renderer.Write("Configured tool path reset. The next operation will detect installations again.");
                break;
        }
        return 0;
    }

}
