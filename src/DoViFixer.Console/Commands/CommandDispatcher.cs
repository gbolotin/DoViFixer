using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Console.Commands;

public interface IConsoleCommand
{
    bool Handles(string command);
    Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken);
}

public sealed class CommandDispatcher(IEnumerable<IConsoleCommand> commands, DependencyCommand dependencies, ILogger<CommandDispatcher> logger)
{
    public async Task<int> ExecuteAsync(CommandLine command, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["CommandId"] = id, ["Command"] = command.Command });
        // Parsed, supported arguments only; never dump the process environment or arbitrary raw command lines.
        logger.LogDebug("Command arguments {Arguments}; options {Options}", command.Arguments, command.Options);
        return await OperationLog.RunAsync(logger, "Command", id, command.Arguments.FirstOrDefault(), async () =>
        {
            int exitCode = await ExecuteCoreAsync(command, cancellationToken);
            logger.LogInformation("Command returned {ExitCode}", exitCode);
            return exitCode;
        }, cancellationToken, code => code switch
        {
            0 => OperationStatus.Completed,
            4 => OperationStatus.Skipped,
            130 => OperationStatus.Cancelled,
            _ => OperationStatus.Failed
        });
    }

    private async Task<int> ExecuteCoreAsync(CommandLine command, CancellationToken cancellationToken)
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
