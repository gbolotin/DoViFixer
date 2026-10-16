using DoViFixer.Application.Diagnostics;
using DoViFixer.Console.Rendering;

namespace DoViFixer.Console.Commands;

public sealed class DiagnosticsCommand(DiagnosticsService diagnostics, ConsoleRenderer renderer) : IConsoleCommand
{
    public bool Handles(string command) => command == "diagnostics";

    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        renderer.Write(DiagnosticsService.ContentsNotice);
        var result = await diagnostics.ExportAsync(command.Arguments[1], cancellationToken);
        renderer.Write(result.Message);
        return CommandDispatcher.ExitCode(result.Status);
    }
}
