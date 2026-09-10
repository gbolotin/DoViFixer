using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace DoViFixer.Console.Rendering;

internal sealed class ConsoleLogFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter formatter = new("[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}");

    public void Format(LogEvent logEvent, TextWriter output)
    {
        const string elapsedToken = "{ElapsedMilliseconds} ms";
        if (logEvent.MessageTemplate.Text.Contains(elapsedToken, StringComparison.Ordinal) &&
            logEvent.Properties.TryGetValue("ElapsedMilliseconds", out var value) &&
            value is ScalarValue { Value: double milliseconds } && double.IsFinite(milliseconds) &&
            milliseconds >= 0 && milliseconds < TimeSpan.MaxValue.TotalMilliseconds)
        {
            // Format a separate event for this sink; file sinks retain the original template and numeric value.
            var template = new MessageTemplateParser().Parse(logEvent.MessageTemplate.Text.Replace(elapsedToken, "{ConsoleElapsed:l}", StringComparison.Ordinal));
            var properties = logEvent.Properties.Where(p => p.Key != "ConsoleElapsed")
                .Select(p => new LogEventProperty(p.Key, p.Value))
                .Append(new LogEventProperty("ConsoleElapsed", new ScalarValue(FormatElapsed(milliseconds))));
            logEvent = new LogEvent(logEvent.Timestamp, logEvent.Level, logEvent.Exception, template, properties);
        }
        formatter.Format(logEvent, output);
    }

    private static string FormatElapsed(double milliseconds)
    {
        if (milliseconds < 1000)
        {
            return $"{milliseconds:0.##} ms";
        }
        if (milliseconds < 60_000)
        {
            return $"{milliseconds / 1000:0.##} s";
        }
        var elapsed = TimeSpan.FromMilliseconds(milliseconds);
        if (elapsed.Days > 0)
        {
            return $"{elapsed.Days}d {elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        }
        if (elapsed.Hours > 0)
        {
            return $"{elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s";
        }
        return $"{elapsed.Minutes}m {elapsed.Seconds}s";
    }
}
