using DoViFixer.Application.Operations;

namespace DoViFixer.Console.Commands;

public interface IConsoleCommand
{
    bool Handles(string command);
    Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken);
}

public sealed class CommandDispatcher(IEnumerable<IConsoleCommand> commands, DependencyCommand dependencies)
{
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        if (command.Command is "scan" or "inspect" or "convert" or "backup" or "restore")
        {
            if (!await dependencies.PreflightAsync(command, cancellationToken))
            {
                return 3;
            }
        }
        var handler = commands.Single(c => c.Handles(command.Command));
        return await handler.ExecuteAsync(command, cancellationToken);
    }

    internal static int ExitCode(OperationStatus status) => status switch
    {
        OperationStatus.Completed => 0,
        OperationStatus.Cancelled => 130,
        OperationStatus.Skipped => 4,
        _ => 1
    };
}
