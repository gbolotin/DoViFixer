using System.Text.Json;
using System.Text.Json.Serialization;
using DoViFixer.Application.Operations;

namespace DoViFixer.Console.Rendering;

public sealed class ConsoleRenderer : IProgress<OperationProgress>, IDisposable
{
    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000_000 => $"{bytes / 1_000_000_000_000m:N2} TB",
        >= 1_000_000_000 => $"{bytes / 1_000_000_000m:N2} GB",
        >= 1_000_000 => $"{bytes / 1_000_000m:N2} MB",
        1 => "1 byte",
        _ => $"{bytes:N0} bytes"
    };

    private readonly TextWriter progressOutput;
    private readonly bool interactive;
    private readonly Func<int> getWidth;

    public ConsoleRenderer() : this(System.Console.Error, !System.Console.IsErrorRedirected && Environment.GetEnvironmentVariable("TERM") != "dumb", () => System.Console.WindowWidth) { }

    internal ConsoleRenderer(TextWriter progressOutput, bool interactive, Func<int> getWidth)
    {
        this.progressOutput = progressOutput;
        this.interactive = interactive;
        this.getWidth = getWidth;
    }

    private readonly object progressLock = new();
    private OperationProgress? previousProgress;
    private int lineLength;
    private static readonly JsonSerializerOptions json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public void Write(string message)
    {
        lock (progressLock)
        {
            EndProgressLine();
            System.Console.WriteLine(message);
        }
    }
    public void Write(string message, ConsoleColor? color)
    {
        if (color is null || System.Console.IsOutputRedirected ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) ||
            Environment.GetEnvironmentVariable("TERM") == "dumb")
        {
            Write(message);
            return;
        }
        var previous = System.Console.ForegroundColor;
        try
        {
            System.Console.ForegroundColor = color.Value;
            Write(message);
        }
        finally
        {
            System.Console.ForegroundColor = previous;
        }
    }
    public void Json<T>(T value) => Write(JsonSerializer.Serialize(value, json));
    public void Report(OperationProgress value)
    {
        lock (progressLock)
        {
            bool sameStage = previousProgress is not null && previousProgress.OperationId == value.OperationId &&
                previousProgress.Stage == value.Stage && previousProgress.File == value.File;
            double? percent = value.Percent is double number && double.IsFinite(number) ? Math.Clamp(number, 0, 100) : null;
            string label = percent is double p ? $"{Math.Floor(p),3:0}%" : " ...";
            string message = $"{value.Stage} {label}{(value.File is null ? "" : $": {value.File}")}";
            if (!interactive)
            {
                // Keep logs readable: one stage start and one successful completion.
                if (!sameStage || (percent == 100 && previousProgress?.Percent != 100))
                {
                    progressOutput.WriteLine(message);
                }
            }
            else
            {
                if (!sameStage)
                {
                    EndProgressLine();
                }
                int width = Math.Max(1, getWidth() - 1);
                message = message.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                if (message.Length > width)
                {
                    message = message[..Math.Max(0, width - 1)] + "…";
                }
                progressOutput.Write("\r" + message + new string(' ', Math.Max(0, Math.Min(lineLength, width) - message.Length)));
                lineLength = message.Length;
                if (percent == 100)
                {
                    EndProgressLine();
                }
            }
            previousProgress = value;
        }
    }

    private void EndProgressLine()
    {
        if (lineLength > 0)
        {
            progressOutput.WriteLine();
            lineLength = 0;
        }
    }

    public void Dispose()
    {
        lock (progressLock)
        {
            EndProgressLine();
        }
    }
    public void Result(FileResult result) => Write($"{result.Status.ToString().ToUpperInvariant()}: {result.Input}\n  {result.Message}{(result.Output is null ? "" : $"\n  Output: {result.Output}")}");

}
