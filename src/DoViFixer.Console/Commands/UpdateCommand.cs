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
public sealed class UpdateCommand(UpdateService updates, ConsoleRenderer renderer) : IConsoleCommand
{
    public bool Handles(string command) => command == "update-check";
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        string input = command.Arguments.FirstOrDefault() ?? Environment.CurrentDirectory;
        switch (command.Command)
        {
            case "update-check":
                var result = await updates.CheckAsync(ApplicationVersion.Of(typeof(UpdateCommand).Assembly), cancellationToken);
            if (command.Has("json"))
            {
                renderer.Json(result);
            }
            else
            {
                renderer.Write(Format(result));
            }

            return 0;
            default:
                throw new ArgumentException("Unsupported command.");
        }
    }

    internal static string Format(UpdateCheckResult result) => $"{ApplicationTitle.Name} {result.CurrentVersion}\n" + result switch
    {
        { Latest: null } => $"No {ApplicationTitle.Name} release has been published yet.",
        { Latest: { } latest, IsUpdateAvailable: true } => $"Update available: {latest.Version}\n{latest.Url}",
        { Latest: { } latest } => $"Up to date. Latest release: {latest.Version}",
    };
}
