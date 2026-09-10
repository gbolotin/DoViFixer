namespace DoViFixer.Console.Rendering;

/// <summary>Command reference rendered without host startup or dependency discovery.</summary>
internal sealed class HelpRenderer(ConsoleRenderer renderer)
{
    private readonly int width = System.Console.IsOutputRedirected ? 100 : Math.Clamp(System.Console.WindowWidth - 1, 20, 120);

    public void Write()
    {
        renderer.Write("DoViFixer 0.1.0 - native Windows Dolby Vision tools", ConsoleColor.Cyan);
        Paragraph("Usage: DoViFixer.Console <command> [arguments] [options]");
        Paragraph("Help: -help, --help, -h, or help. [brackets] mean optional; <angles> mean required.");
        Paragraph("Groups: File Scanning & Analysis | File Conversion | Backup & Restore | Cleanup | Dependencies | Settings | Updates");

        Group("File Scanning & Analysis");
        Command("scan [file-or-directory] [-r [depth]] [--inspect-simple] [--candidates-only] [--json]",
            "Find media files, identify Dolby Vision profiles, and classify conversion candidates. With no path, scan the current directory. Subfolders are included only when recursion is requested.");
        Options(
            ("-r, --recursive [depth]", "Include subfolders. Default depth with -r: 5; explicit depth: 0 to 100."),
            ("--candidates-only", "Show conversion candidates only."),
            ("--inspect-simple", "Deep-inspect every Simple FEL candidate after scanning. Decodes all base-layer frames; slow."),
            ("--json", "Write structured JSON for scripts."));
        Examples(
            ("scan", "Scan the current folder"),
            ("scan --recursive", "Recursive scan, default depth 5"),
            ("scan -r", "Same, short form"),
            ("scan --recursive 2", "Recursive scan, depth 2"),
            ("scan \"D:\\Movies\" -r 3", "Scan a specific folder, depth 3"),
            ("scan -r --candidates-only", "Show only conversion candidates"));

        Command("inspect <file.mkv> [--deep] [--json]",
            "Analyze one file in detail. Deep inspection compares every decoded base-layer frame's luminance with RPU L1 metadata to look for possible FEL brightness expansion.");
        Options(("--deep", "Run full frame-by-frame inspection (slow)."));
        Examples(("inspect \"Movie.mkv\"", "Inspect one movie"),
            ("inspect \"Movie.mkv\" --deep", "Measure possible brightness expansion"));

        Group("File Conversion");
        Command("convert [files-or-directories...] [-r [depth]] [--hdr10] [--include-simple] [--force] [--backup] [--delete] [--safe] [--output directory] [--temp directory] [--plan | --yes]",
            "Convert eligible Profile 7 files to Dolby Vision Profile 8.1 (default) or HDR10. Accepts multiple files and folders; no paths means the current directory.");
        Options(
            ("--hdr10", "Produce HDR10 instead of Dolby Vision Profile 8.1."),
            ("--include-simple", "Include Simple FEL candidates in flag-based selection (--plan / --yes)."),
            ("-f, --force", "Permit detected Complex FEL; picture data will be lost. Failed or unknown analysis stays blocked."),
            ("-b, --backup", "Save a .dovi enhancement archive before conversion."),
            ("--delete", "Rename the original to .bak.dovi_convert, reuse its filename, then delete that backup after verification."),
            ("-s, --safe", "Force disk extraction. Default: streaming with disk fallback."),
            ("-o, --output directory", "Write output to the selected directory."));
        Paragraph("By default, originals are kept and output is named \" - DV P8.1.mkv\" or \" - HDR10.mkv\" beside the source. Interactive conversion shows all plans, then asks once per MEL, Simple FEL, or Complex FEL category. Declining skips that category; accepted categories continue.");
        Examples(("convert \"Movie.mkv\" --plan", "Preview the conversion plan"),
            ("convert \"D:\\Movies\" -r", "Review and approve a folder batch"),
            ("convert \"Movie.mkv\" --hdr10 --backup", "Create HDR10 and an enhancement archive"));

        Group("Backup & Restore");
        Command("backup <file.mkv> [--output directory] [--temp directory] [--plan | --yes]",
            "Save the enhancement layer and restoration metadata in a .dovi archive for later restoration.");
        Examples(("backup \"Movie.mkv\" --plan", "Preview archive creation"),
            ("backup \"Movie.mkv\"", "Create an archive after approval"));
        Command("restore <base.mkv> [--source <archive.dovi>] [--output directory] [--temp directory] [--plan | --yes]",
            "Restore Profile 7 from a compatible base file and enhancement archive. Finds the adjacent .dovi archive by default; EL-only upstream archives are also accepted.");
        Options(("--source archive.dovi", "Choose an archive explicitly instead of the adjacent archive."));
        Examples(("restore \"Movie - DV P8.1.mkv\" --plan", "Preview restoration"),
            ("restore \"Base.mkv\" --source \"Movie.dovi\"", "Restore using a chosen archive"));

        Group("Cleanup");
        Command("cleanup [file-or-directory] [-r [depth]] [--delete-backups [--yes]]",
            "List .dovi archives and .bak.dovi_convert backups. Defaults to the current directory and only lists files unless deletion is requested.");
        Options(("--delete-backups", "Request permanent deletion of listed backups, with confirmation."),
            ("--yes", "Approve deletion without prompting; requires --delete-backups."));
        Examples(("cleanup \"D:\\Movies\" -r", "List backups without deleting them"));

        Group("Dependencies");
        Command("dependencies check [Tool ...] [--json]",
            "Check native tool availability and compatibility. Omit tool names to check all tools.");
        Command("dependencies install [Tool ...] [--yes]",
            "Prepare an installation plan for missing, unusable, or incompatible tools, then ask for approval. Validated tool paths are saved after installation.");
        Options(("--yes", "Approve this software installation plan without prompting."),
            ("--repair", "Accepted for compatibility; repair is already included when needed."));
        Paragraph("Tool names: FFmpeg, FFprobe, MkvMerge, MkvExtract, MediaInfo, DoviTool.");
        Examples(("dependencies check", "Check all native tools"),
            ("dependencies check FFmpeg DoviTool --json", "Check selected tools as JSON"),
            ("dependencies install", "Review and approve required installations"));

        Group("Settings");
        Command("settings show [--json]", "Display saved settings and tool paths.");
        Command("settings tool <Tool> <absolute-executable-path>", "Select the executable for a native tool.");
        Command("settings reset-tool <Tool>", "Remove the saved executable override for a tool.");
        Command("settings temp <directory>", "Save the temporary directory; creates missing folders and checks write access.");
        Command("settings add-to-path", "Add this app folder to your user PATH. No administrator rights needed. Reopen your terminal afterwards and keep the app in that folder.");
        Examples(("settings show", "Show current configuration"),
            ("settings tool FFmpeg \"C:\\Tools\\ffmpeg.exe\"", "Set a tool path"),
            ("settings temp \"D:\\DoViScratch\"", "Set the working directory for temporary files"));

        Group("Updates");
        Command("update-check [--json]", "Check upstream dovi_convert releases. No DoViFixer release feed exists yet.");
        Examples(("update-check", "Show available upstream release information"));

        Group("Shared Options & Exit Codes");
        Options(("-t, --temp directory", "Temporary workspace for scan, inspect, convert, backup, and restore."),
            ("--plan", "Analyze and display exact planned outputs for convert, backup, or restore without executing the plan."),
            ("-y, --yes", "Approve a convert, backup, or restore plan. Does not approve software installation."),
            ("--install-dependencies", "Separate installation consent for scan, inspect, convert, backup, and restore. Cannot combine with --json."),
            ("--", "End option parsing before filenames that begin with a dash."));
        Paragraph("Quote paths containing spaces. Color is disabled for redirected output, NO_COLOR, or TERM=dumb.");
        Table("Code", "Meaning", [("0", "Success"), ("1", "Failure or partial completion"),
            ("2", "Invalid arguments"), ("3", "Dependencies unavailable"),
            ("4", "Declined or no selected work"), ("130", "Cancelled")]);
    }

    private void Group(string title)
    {
        renderer.Write("");
        renderer.Write(new string('=', width), ConsoleColor.DarkCyan);
        Paragraph(title, ConsoleColor.Cyan);
        renderer.Write(new string('=', width), ConsoleColor.DarkCyan);
    }

    private void Command(string syntax, string description)
    {
        renderer.Write("");
        Paragraph(syntax, ConsoleColor.Green);
        Paragraph(description);
    }

    private void Options(params (string, string)[] rows) => Table("Option", "Meaning", rows);

    private void Examples(params (string, string)[] rows) =>
        Table("Example (run in your terminal)", "What it does",
            rows.Select(row => ($"DoViFixer.Console {row.Item1}", row.Item2)).ToArray());

    private void Table(string leftTitle, string rightTitle, (string Left, string Right)[] rows)
    {
        renderer.Write("");
        int leftWidth = Math.Max(leftTitle.Length, rows.Max(row => row.Left.Length));
        // Stack long examples on smaller terminals rather than splitting copyable commands.
        if (width - leftWidth - 5 < 25)
        {
            Paragraph($"{leftTitle} / {rightTitle}", ConsoleColor.Yellow);
            foreach (var row in rows)
            {
                Paragraph(row.Left, ConsoleColor.Green);
                foreach (string line in Wrap(row.Right, width - 4))
                {
                    renderer.Write("    " + line);
                }
            }
            return;
        }
        renderer.Write($"  {leftTitle.PadRight(leftWidth)} | {rightTitle}", ConsoleColor.Yellow);
        renderer.Write($"  {new string('-', leftWidth)}-+-{new string('-', width - leftWidth - 5)}", ConsoleColor.DarkGray);
        foreach (var row in rows)
        {
            bool first = true;
            foreach (string line in Wrap(row.Right, width - leftWidth - 5))
            {
                renderer.Write($"  {(first ? row.Left : "").PadRight(leftWidth)} | {line}");
                first = false;
            }
        }
    }

    private void Paragraph(string text, ConsoleColor? color = null)
    {
        foreach (string line in Wrap(text, width - 2))
        {
            renderer.Write("  " + line, color);
        }
    }

    private static IEnumerable<string> Wrap(string text, int maxWidth)
    {
        while (text.Length > maxWidth)
        {
            int split = text.LastIndexOf(' ', maxWidth);
            if (split <= 0)
            {
                split = maxWidth;
            }
            yield return text[..split];
            text = text[split..].TrimStart();
        }
        yield return text;
    }
}
