namespace DoViFixer.ShellExtension;

/// <summary>The command lines that start the app for the items selected in File Explorer.</summary>
internal static class CommandLines
{
    /// <summary>The argument that asks the app to convert the files, as FileExplorerRequest in DoViFixer.Application reads it.</summary>
    public const string ConvertArgument = "--convert";

    // Windows limits a command line to 32,767 characters; the rest leaves room for the executable path.
    internal const int MaximumLength = 30000;

    /// <summary>
    /// Splits <paramref name="paths"/> into command lines short enough for Windows. The app gathers the files of
    /// starts that follow each other closely into one batch.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> For(IReadOnlyList<string> paths, bool convert)
    {
        List<IReadOnlyList<string>> lines = [];
        List<string> current = [];
        int length = 0;
        foreach (string path in paths)
        {
            // Each argument is quoted and separated by a space.
            int added = path.Length + 3;
            if (current.Count > 0 && length + added > MaximumLength)
            {
                lines.Add(Finish(current, convert));
                current = [];
                length = 0;
            }

            current.Add(path);
            length += added;
        }

        if (current.Count > 0)
        {
            lines.Add(Finish(current, convert));
        }

        return lines;
    }

    private static List<string> Finish(List<string> paths, bool convert) => convert ? [ConvertArgument, .. paths] : paths;
}
