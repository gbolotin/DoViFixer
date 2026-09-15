using DoViFixer.App.Dialogs;
using DoViFixer.App.Presentation;
using DoViFixer.App.ViewModels;
using DoViFixer.Application;
using DoViFixer.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prism.Ioc;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace DoViFixer.App.Composition;
public static class AppComposition
{
    public static void Register(IContainerExtension registry, IConfiguration configuration, bool fileLogging = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (fileLogging)
        {
            services.AddSerilog((_, logging) => ConfigureLogging(logging, configuration), preserveStaticLogger: true);
        }

        services.AddDoViFixerApplication();
        services.AddDoViFixerInfrastructure(configuration);
        string root = configuration["DoViFixer:DataDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoViFixer");
        services.AddTransient<IUserDialogs>(_ => new UserDialogs(Path.Combine(root, "Logs")));
        services.AddTransient<DependencySetup>();
        services.AddTransient<MediaViewModel>();
        services.AddTransient<ArchiveViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ShellViewModel>();
        // Prism imports the descriptors into its existing DryIoc root, including owned factories.
        registry.Populate(services);
    }

    private static void ConfigureLogging(LoggerConfiguration logging, IConfiguration configuration)
    {
        string root = configuration["DoViFixer:DataDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoViFixer");
        string directory = Path.Combine(root, "Logs");
        Directory.CreateDirectory(directory);
        var json = new JsonFormatter(renderMessage: true);
        logging.MinimumLevel.Debug().Enrich.FromLogContext().Enrich.WithProperty("SessionId", Guid.NewGuid()).Enrich.WithProperty("ProcessId", Environment.ProcessId).Enrich.WithProperty("Application", "DoViFixer.App").WriteTo.File(json, Path.Combine(directory, "app-diagnostic-.jsonl"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, shared: true).WriteTo.Logger(s => s.Filter.ByIncludingOnly(e => IsKind(e, "History")).WriteTo.File(json, Path.Combine(directory, "app-history-.jsonl"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90, fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, shared: true)).WriteTo.Logger(s => s.Filter.ByIncludingOnly(e => IsKind(e, "Audit")).WriteTo.File(json, Path.Combine(directory, "app-audit-.jsonl"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90, fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, shared: true));
    }

    private static bool IsKind(LogEvent entry, string kind) => entry.Properties.TryGetValue("LogKind", out var value) && value is ScalarValue
    {
        Value: string text
    }
    && text == kind;
}
