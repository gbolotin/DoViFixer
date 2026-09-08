using DoViFixer.Application.Operations;
using DoViFixer.Console.Commands;
using DoViFixer.Console.Composition;
using DoViFixer.Console.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DoViFixer.Console;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CommandLine command;
        try
        {
            command = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            await System.Console.Error.WriteLineAsync(ex.Message);
            return 2;
        }
        if (command.Command == "help")
        {
            System.Console.WriteLine(ConsoleRenderer.Help);
            return 0;
        }
        try
        {
            using var host = ConsoleHost.Build();
            await host.StartAsync();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            try
            {
                return await host.Services.GetRequiredService<CommandDispatcher>().ExecuteAsync(command, lifetime.ApplicationStopping);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            await System.Console.Error.WriteLineAsync("CANCELLED. Original media retained. Run dependencies check if an installer was interrupted.");
            return 130;
        }
        catch (DependencyNotReadyException ex)
        {
            await System.Console.Error.WriteLineAsync(ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            await System.Console.Error.WriteLineAsync($"FAILED: {ex.Message}");
            return 1;
        }
    }
}
