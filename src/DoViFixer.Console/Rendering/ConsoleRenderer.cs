using System.Text.Json;
using System.Text.Json.Serialization;
using DoViFixer.Application.Operations;

namespace DoViFixer.Console.Rendering;

public sealed class ConsoleRenderer : IProgress<OperationProgress>
{
    private static readonly JsonSerializerOptions json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public void Write(string message) => System.Console.WriteLine(message);
    public void Json<T>(T value) => Write(JsonSerializer.Serialize(value, json));
    public void Report(OperationProgress value) => System.Console.Error.WriteLine($"{value.Stage}{(value.File is null ? "" : $": {value.File}")}");
    public void Result(FileResult result) => Write($"{result.Status.ToString().ToUpperInvariant()}: {result.Input}\n  {result.Message}{(result.Output is null ? "" : $"\n  Output: {result.Output}")}");

    public const string Help = """
        DoViFixer 0.1.0 — native Windows Dolby Vision tools

        dependencies check [Tool ...] [--json]
        dependencies install [Tool ...] [--repair] [--yes]
        scan [file-or-directory] [-r [depth]] [--candidates-only] [--json]
        inspect <file.mkv> [--json]
        convert [file-or-directory] [-r [depth]] [--hdr10] [--include-simple]
                [--force] [--backup] [--output directory] [--temp directory]
                [--plan | --yes]
        backup <file.mkv> [--output directory] [--temp directory] [--plan | --yes]
        restore <base.mkv> <archive.dovi> [--allow-legacy-archive]
                [--output directory] [--temp directory] [--plan | --yes]
        cleanup [file-or-directory] [-r [depth]] [--delete-backups [--yes]]
        settings show [--json]
        settings tool <Tool> <absolute-executable-path>
        settings reset-tool <Tool>
        settings temp <existing-directory>
        update-check [--json]

        Tool names: FFmpeg, FFprobe, MkvMerge, MkvExtract, MediaInfo, DoviTool.
        -r defaults to depth 5; omitted paths use the current directory.
        Media commands support --install-dependencies as separate installation consent.
        --yes approves the displayed media plan, never software installation by itself.
        --plan performs analysis and shows exact outputs without conversion.
        Originals are always retained; outputs are never overwritten.
        --force permits detected complex FEL only; failed/unknown analysis stays blocked.
        Cleanup lists .dovi and .bak.dovi_convert files; deletion is permanent and opt-in.
        --safe is accepted for convert/inspect; disk extraction is always used.
        Scan/inspect also accept --temp. Use -- before filenames beginning with a dash.
        update-check reports upstream dovi_convert releases; no DoViFixer release feed exists yet.
        Exit codes: 0 success, 1 failure/partial, 2 arguments, 3 dependencies,
                    4 declined/no selected work, 130 cancelled.
        """;
}
