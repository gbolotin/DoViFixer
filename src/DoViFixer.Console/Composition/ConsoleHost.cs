using DoViFixer.Application;
using DoViFixer.Console.Commands;
using DoViFixer.Console.Interaction;
using DoViFixer.Console.Rendering;
using DoViFixer.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Console.Composition;

public static class ConsoleHost
{
    public static IHost Build()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        builder.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddDoViFixerApplication();
        builder.Services.AddDoViFixerInfrastructure(builder.Configuration);
        builder.Services.AddTransient<ConsoleRenderer>();
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
