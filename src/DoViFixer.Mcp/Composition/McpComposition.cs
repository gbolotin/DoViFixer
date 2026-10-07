using DoViFixer.Application;
using DoViFixer.Application.Updates;
using DoViFixer.Infrastructure;
using DoViFixer.Mcp.Jobs;
using DoViFixer.Mcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace DoViFixer.Mcp.Composition;
public static class McpComposition
{
    /// <summary>How long a tool call waits for its job before returning it as running.</summary>
    public static readonly TimeSpan InlineWait = TimeSpan.FromSeconds(20);

    public const string Instructions = """
        Converts Dolby Vision Profile 7 movies on this Windows PC to Profile 8.1 or HDR10, and archives or restores their enhancement layers.
        Paths are absolute Windows paths on this PC.
        Work that changes files is two steps: a plan_* tool analyzes and returns exact plans with ids, then a start_* tool runs the plans the user approved.
        Always show the user the plans (inputs, outputs, verdicts and warnings) and wait for their approval before a start_* tool or delete_backups.
        Never raise includeSimpleFel or forceComplexFel on your own: converting FEL files loses enhancement-layer picture data.
        Scans, inspections, plans and conversions run as jobs, one at a time. A tool returns the result if its job finishes within a few seconds; otherwise it returns the job, which you follow with get_job.
        If a tool reports missing dependencies, offer plan_dependency_install.
        """;

    /// <summary>Registers DoViFixer's services and the MCP server; the caller picks the transport.</summary>
    public static IMcpServerBuilder Register(IServiceCollection services, IConfiguration configuration, bool fileLogging = true)
    {
        services.AddLogging(logging => logging.ClearProviders());
        if (fileLogging)
        {
            // Never log to the console: standard output carries the MCP protocol.
            services.AddSerilog((_, logging) => ConfigureLogging(logging, configuration), preserveStaticLogger: true);
        }

        services.AddDoViFixerApplication();
        services.AddDoViFixerInfrastructure(configuration);
        // Jobs and approved plans outlive the tool calls that create them.
        services.AddSingleton(provider => new JobRegistry(provider.GetRequiredService<ILogger<JobRegistry>>(), provider.GetRequiredService<TimeProvider>(), InlineWait));
        services.AddSingleton<PlanRegistry>();
        return services.AddMcpServer(options =>
        {
            options.ServerInfo = new()
            {
                Name = "dovifixer",
                Title = ApplicationTitle.Name,
                Version = ApplicationVersion.Of(typeof(McpComposition).Assembly)
            };
            options.ServerInstructions = Instructions;
        }).WithTools<MediaTools>().WithTools<ConversionTools>().WithTools<ArchiveTools>().WithTools<SetupTools>().WithTools<JobTools>();
    }

    private static void ConfigureLogging(LoggerConfiguration logging, IConfiguration configuration)
    {
        string root = configuration["DoViFixer:DataDirectory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoViFixer");
        string directory = Path.Combine(root, "Logs");
        Directory.CreateDirectory(directory);
        var json = new JsonFormatter(renderMessage: true);
        logging.MinimumLevel.Debug().MinimumLevel.Override("Microsoft", LogEventLevel.Warning).MinimumLevel.Override("System", LogEventLevel.Warning).MinimumLevel.Override("ModelContextProtocol", LogEventLevel.Information).Enrich.FromLogContext().Enrich.WithProperty("SessionId", Guid.NewGuid()).Enrich.WithProperty("ProcessId", Environment.ProcessId).Enrich.WithProperty("Application", "DoViFixer.Mcp").Enrich.WithProperty("ApplicationVersion", ApplicationVersion.Of(typeof(McpComposition).Assembly)).WriteTo.File(json, Path.Combine(directory, "mcp-diagnostic-.jsonl"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, shared: true).WriteTo.Logger(s => s.Filter.ByIncludingOnly(e => IsKind(e, "History")).WriteTo.File(json, Path.Combine(directory, "mcp-history-.jsonl"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90, fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, shared: true)).WriteTo.Logger(s => s.Filter.ByIncludingOnly(e => IsKind(e, "Audit")).WriteTo.File(json, Path.Combine(directory, "mcp-audit-.jsonl"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90, fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, shared: true));
    }

    private static bool IsKind(LogEvent entry, string kind) => entry.Properties.TryGetValue("LogKind", out var value) && value is ScalarValue
    {
        Value: string text
    }
    && text == kind;
}
