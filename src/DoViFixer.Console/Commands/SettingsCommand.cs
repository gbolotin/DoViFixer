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
public sealed class SettingsCommand(SettingsService settings, PresetService presets, ConsoleInteraction interaction, IUserPathRegistration userPath, ConsoleRenderer renderer) : IConsoleCommand
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
            case "preset":
                return await PresetAsync(command, cancellationToken);
        }

        return 0;
    }

    private async Task<int> PresetAsync(CommandLine command, CancellationToken cancellationToken)
    {
        switch (command.Arguments[1])
        {
            case "list":
                var list = await presets.ListAsync(cancellationToken);
                var active = PresetService.FindActive(list, await settings.ReadAsync(cancellationToken));
                foreach (var preset in list)
                {
                    renderer.Write($"{(preset == active ? "* " : "  ")}{preset.Name}{(preset.IsBuiltIn ? " (built-in)" : "")}");
                    if (preset.Description.Length > 0)
                    {
                        renderer.Write($"    {preset.Description}");
                    }
                }

                renderer.Write(active is null ? "The current settings match no preset." : $"* The current settings match {active.Name}.");
                return 0;
            case "apply":
                var selected = await presets.FindAsync(command.Arguments[2], cancellationToken);
                var changes = await presets.PreviewAsync(selected, cancellationToken);
                if (changes.Count == 0)
                {
                    renderer.Write($"The settings already match the {selected.Name} preset.");
                    return 0;
                }

                renderer.Write($"Applying {selected.Name} changes these settings:");
                foreach (var change in changes)
                {
                    renderer.Write($"  {(change.IncreasesRisk ? "!" : " ")} {change.Setting}: {change.From} -> {change.To}");
                }

                if (changes.Any(change => change.IncreasesRisk))
                {
                    renderer.Write("! Can lose data or picture detail that the current settings keep.", ConsoleColor.Yellow);
                }

                renderer.Write("Folders, worker counts, tool paths and appearance stay as they are.");
                if (!await interaction.ConfirmAsync($"Apply the {selected.Name} preset?", command.Has("yes"), cancellationToken))
                {
                    renderer.Write("Preset not applied. Settings were not changed.");
                    return 4;
                }

                await presets.ApplyAsync(selected, cancellationToken);
                renderer.Write($"Applied the {selected.Name} preset.");
                return 0;
            case "save":
                var saved = await presets.SaveCurrentAsync(command.Arguments[2], command.Arguments.Count > 3 ? command.Arguments[3] : null, cancellationToken);
                renderer.Write($"Saved the current settings as the {saved.Name} preset.");
                return 0;
            default:
                if (!await presets.DeleteAsync(command.Arguments[2], cancellationToken))
                {
                    throw new ArgumentException($"There is no saved preset named '{command.Arguments[2]}'.");
                }

                renderer.Write($"Deleted the {command.Arguments[2]} preset. Settings were not changed.");
                return 0;
        }
    }
}
