namespace DoViFixer.Application.FileExplorer;

/// <summary>
/// What a command File Explorer's right-click menu starts asks for: the files and folders to open in the app and,
/// for Convert, to convert as the Convert button does.
/// </summary>
public sealed record FileExplorerRequest(IReadOnlyList<string> Paths, bool Convert)
{
    /// <summary>The argument that asks to convert the files, not only to open them.</summary>
    public const string ConvertArgument = "--convert";

    /// <summary>
    /// Reads the app's command line: <see cref="ConvertArgument"/> and any number of files or folders, relative to
    /// <paramref name="currentDirectory"/>. Other arguments starting with "--" are ignored.
    /// </summary>
    public static FileExplorerRequest Parse(IEnumerable<string> arguments, string currentDirectory)
    {
        bool convert = false;
        List<string> paths = [];
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, ConvertArgument, StringComparison.OrdinalIgnoreCase))
            {
                convert = true;
            }
            else if (!string.IsNullOrWhiteSpace(argument) && !argument.StartsWith("--", StringComparison.Ordinal))
            {
                // The window that receives the request may run in another folder.
                paths.Add(Path.GetFullPath(argument.Trim('"'), currentDirectory));
            }
        }

        return new(paths, convert);
    }

    /// <summary>The command line that <see cref="Parse"/> reads back as this request, in any folder.</summary>
    public IReadOnlyList<string> ToArguments() => Convert ? [ConvertArgument, .. Paths] : [.. Paths];
}
