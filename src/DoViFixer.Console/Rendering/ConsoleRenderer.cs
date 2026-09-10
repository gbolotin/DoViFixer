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

    public const string Help = """
        DoViFixer 0.1.0 — native Windows Dolby Vision tools

        dependencies check [Tool ...] [--json]
        dependencies install [Tool ...] [--yes]
        scan [file-or-directory] [-r [depth]] [--inspect-simple] [--candidates-only] [--json]
          --inspect-simple deep-inspects every Simple FEL candidate after scanning (slow).
        inspect <file.mkv> [--deep] [--json]
          --deep decodes every base-layer frame and compares its luminance with RPU L1 (slow).
        convert [files-or-directories...] [-r [depth]] [--hdr10] [--include-simple]
                [--force] [--backup] [--delete] [--safe] [--output directory] [--temp directory]
                [--plan | --yes]
        backup <file.mkv> [--output directory] [--temp directory] [--plan | --yes]
        restore <base.mkv> [--source <archive.dovi>]
                [--output directory] [--temp directory] [--plan | --yes]
        cleanup [file-or-directory] [-r [depth]] [--delete-backups [--yes]]
        settings show [--json]
        settings tool <Tool> <absolute-executable-path>
        settings reset-tool <Tool>
        settings temp <directory>
        settings add-to-path
        update-check [--json]

        Tool names: FFmpeg, FFprobe, MkvMerge, MkvExtract, MediaInfo, DoviTool.
        settings temp creates missing directories and checks write access.
        settings add-to-path adds this app folder to user PATH; no administrator rights needed.
        Reopen your terminal after adding to PATH. Keep the app in that folder.
        -r defaults to depth 5; omitted paths use the current directory.
        Media commands support --install-dependencies as separate installation consent.
        Installation plans include missing, unusable and incompatible tools; validated paths are saved after approval.
        dependencies install still accepts --repair for compatibility; it is no longer required.
        --yes approves the displayed media plan, never software installation by itself.
        --plan performs analysis and shows exact outputs without conversion.
        Conversion keeps originals unchanged and writes " - DV P8.1.mkv" (or " - HDR10.mkv") beside them.
        --delete uses replacement: rename original to .bak.dovi_convert, reuse its filename, then delete the backup after verification.
        --force permits detected complex FEL only; failed/unknown analysis stays blocked.
        Interactive convert shows all plans, then asks once per MEL, Simple FEL, or Complex FEL category to execute.
        Declining skips that category; approved categories continue. --yes and --plan keep flag-based selection.
        Cleanup lists .dovi and .bak.dovi_convert files; deletion is permanent and opt-in.
        Conversion streams by default with disk fallback; --safe forces disk extraction.
        Restore finds the adjacent .dovi archive; EL-only upstream archives are accepted.
        Scan/inspect also accept --temp. Use -- before filenames beginning with a dash.
        update-check reports upstream dovi_convert releases; no DoViFixer release feed exists yet.
        Exit codes: 0 success, 1 failure/partial, 2 arguments, 3 dependencies,
                    4 declined/no selected work, 130 cancelled.
        """;
}
