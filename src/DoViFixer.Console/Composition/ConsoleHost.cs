using DoViFixer.Application;
using DoViFixer.Console.Commands;
using DoViFixer.Console.Interaction;
using DoViFixer.Console.Rendering;
using DoViFixer.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DoViFixer.Console.Composition;

public static class ConsoleHost
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog((_, logging) => LoggingConfiguration.Configure(logging, builder.Configuration),
            preserveStaticLogger: true);
        builder.Services.AddDoViFixerApplication();
        builder.Services.AddDoViFixerInfrastructure(builder.Configuration);
        // All commands share one terminal; the renderer serializes its progress state.
        builder.Services.AddSingleton<ConsoleRenderer>();
        builder.Services.AddTransient<ConsoleInteraction>();
        builder.Services.AddTransient<CommandDispatcher>();
        builder.Services.AddTransient<DependencyCommand>();
        builder.Services.AddTransient<IConsoleCommand, DependencyCommand>();
        builder.Services.AddTransient<IConsoleCommand, MediaReadCommand>();
        builder.Services.AddTransient<IConsoleCommand, ConversionCommand>();
        builder.Services.AddTransient<IConsoleCommand, ArchiveCommand>();
        builder.Services.AddTransient<IConsoleCommand, CleanupCommand>();
        builder.Services.AddTransient<IConsoleCommand, SettingsCommand>();
        builder.Services.AddTransient<IConsoleCommand, UpdateCommand>();
        return builder.Build();
    }
}
