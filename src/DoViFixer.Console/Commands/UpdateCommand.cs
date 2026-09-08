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
                var release = await updates.CheckAsync(cancellationToken);
                if (command.Has("json"))
                {
                    renderer.Json(release);
                }
                else
                {
                    renderer.Write($"{release.Product}: {release.Version}\n{release.Url}\nReviewed baseline: 8.2.0.");
                }
                return 0;
            default:
                throw new ArgumentException("Unsupported command.");
        }
    }
}
