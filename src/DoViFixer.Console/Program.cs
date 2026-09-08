using DoViFixer.Application.Operations;
using DoViFixer.Console.Commands;
using DoViFixer.Console.Composition;
using DoViFixer.Console.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

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
        Serilog.Debugging.SelfLog.Enable(TextWriter.Synchronized(System.Console.Error));
        try
        {
            using var host = ConsoleHost.Build();
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DoViFixer.Console");
            logger.LogInformation("Starting {Command}; runtime {Runtime}; OS {OperatingSystem}; architecture {Architecture}",
                command.Command, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture);
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            try
            {
                await host.StartAsync();
                return await host.Services.GetRequiredService<CommandDispatcher>().ExecuteAsync(command, lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
            {
                logger.LogInformation("Application cancelled");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Application failed while running {Command}", command.Command);
                throw;
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
                logger.LogDebug("Host stopped; flushing log files");
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
        finally
        {
            Serilog.Debugging.SelfLog.Disable();
        }
    }
}
