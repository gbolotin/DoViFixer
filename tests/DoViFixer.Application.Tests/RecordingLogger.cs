using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Tests;

internal sealed record LogEntry(LogLevel Level, Exception? Exception, IReadOnlyDictionary<string, object?> Properties);

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly LoggerExternalScopeProvider scopes = new();
    public List<LogEntry> Entries { get; } = [];
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => scopes.Push(state);
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>();
        scopes.ForEachScope((scope, values) => Copy(scope, values), properties);
        Copy(state, properties);
        Entries.Add(new(logLevel, exception, properties));
    }

    private static void Copy(object? state, Dictionary<string, object?> properties)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var value in values)
            {
                properties[value.Key] = value.Value;
            }
        }
    }
}
