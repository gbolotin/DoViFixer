using Microsoft.Extensions.Configuration;
using DoViFixer.Console.Rendering;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace DoViFixer.Console.Composition;

public static class LoggingConfiguration
{
    public static LoggerConfiguration Configure(LoggerConfiguration logging, IConfiguration configuration)
    {
        string dataDirectory = configuration["DoViFixer:DataDirectory"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DoViFixer");
        string directory = Path.GetFullPath(configuration["DoViFixer:Logging:Directory"] ?? Path.Combine(dataDirectory, "Logs"));
        var level = ReadLevel(configuration, "DiagnosticLevel", LogEventLevel.Debug);
        int diagnosticFiles = ReadPositiveInt(configuration, "DiagnosticRetainedFiles", 14);
        int historyFiles = ReadPositiveInt(configuration, "HistoryRetainedFiles", 90);
        int auditFiles = ReadPositiveInt(configuration, "AuditRetainedFiles", 90);
        int fileSize = ReadPositiveInt(configuration, "FileSizeLimitBytes", 10 * 1024 * 1024);
        // Fail visibly before starting work if the chosen log directory cannot be written.
        Directory.CreateDirectory(directory);
        string probe = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".probe");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
        }
        var json = new JsonFormatter(renderMessage: true);
        return logging
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("SessionId", Guid.NewGuid())
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .Enrich.WithProperty("UserName", Environment.UserName)
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .Enrich.WithProperty("ApplicationVersion", typeof(LoggingConfiguration).Assembly.GetName().Version?.ToString())
            .WriteTo.File(json, Path.Combine(directory, "diagnostic-.jsonl"), restrictedToMinimumLevel: level,
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: fileSize, rollOnFileSizeLimit: true,
                retainedFileCountLimit: diagnosticFiles, shared: true)
            .WriteTo.Logger(history => history.Filter.ByIncludingOnly(e => IsKind(e, "History"))
                .WriteTo.File(json, Path.Combine(directory, "history-.jsonl"), rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSize, rollOnFileSizeLimit: true, retainedFileCountLimit: historyFiles, shared: true))
            .WriteTo.Logger(audit => audit.Filter.ByIncludingOnly(e => IsKind(e, "Audit"))
                .WriteTo.File(json, Path.Combine(directory, "audit-.jsonl"), rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: fileSize, rollOnFileSizeLimit: true, retainedFileCountLimit: auditFiles, shared: true))
            .WriteTo.Console(new ConsoleLogFormatter(), restrictedToMinimumLevel: LogEventLevel.Warning,
                standardErrorFromLevel: LogEventLevel.Verbose);
    }

    private static bool IsKind(LogEvent entry, string kind) =>
        entry.Properties.TryGetValue("LogKind", out var value) && value is ScalarValue { Value: string text } && text == kind;

    private static int ReadPositiveInt(IConfiguration configuration, string name, int fallback)
    {
        string? text = configuration["DoViFixer:Logging:" + name];
        if (text is null)
        {
            return fallback;
        }
        return int.TryParse(text, out int value) && value > 0 ? value
            : throw new ArgumentException($"DoViFixer:Logging:{name} must be a positive integer.");
    }

    private static LogEventLevel ReadLevel(IConfiguration configuration, string name, LogEventLevel fallback)
    {
        string? text = configuration["DoViFixer:Logging:" + name];
        if (text is null)
        {
            return fallback;
        }
        return Enum.TryParse<LogEventLevel>(text, true, out var value) && Enum.IsDefined(value) ? value
            : throw new ArgumentException($"DoViFixer:Logging:{name} is not a valid Serilog level.");
    }
}
