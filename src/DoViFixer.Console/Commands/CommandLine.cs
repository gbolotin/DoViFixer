using DoViFixer.Application.Dependencies;

namespace DoViFixer.Console.Commands;

public sealed record CommandLine(string Command, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string?> Options)
{
    public bool Has(string name) => Options.ContainsKey(name);
    public string? Value(string name) => Options.GetValueOrDefault(name);
    public int Depth => int.Parse(Value("recursive") ?? "0", System.Globalization.CultureInfo.InvariantCulture);

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            return new("help", [], new Dictionary<string, string?>());
        }
        string command = args[0].ToLowerInvariant();
        string[] allowed = command switch
        {
            "scan" => ["recursive", "temp", "candidates-only", "json", "install-dependencies"],
            "inspect" => ["temp", "json", "install-dependencies", "safe"],
            "convert" => ["recursive", "temp", "output", "hdr10", "include-simple", "force", "backup", "delete", "yes", "plan", "safe", "install-dependencies"],
            "backup" => ["temp", "output", "yes", "plan", "install-dependencies"],
            "restore" => ["temp", "output", "yes", "plan", "allow-legacy-archive", "source", "install-dependencies"],
            "cleanup" => ["recursive", "delete-backups", "yes"],
            "dependencies" => ["yes", "repair", "json"],
            "settings" => ["json"],
            "update-check" => ["json"],
            _ => throw new ArgumentException($"Unknown command '{command}'. Run --help.")
        };
        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        bool literal = false;
        for (int i = 1; i < args.Length; i++)
        {
            string argument = args[i];
            if (argument == "--" && !literal)
            {
                literal = true;
                continue;
            }
            if (literal || !argument.StartsWith('-'))
            {
                positionals.Add(argument);
                continue;
            }
            string name = argument switch { "-r" => "recursive", "-t" => "temp", "-o" => "output", "-y" => "yes", "-s" => "safe", "-f" => "force", "-b" => "backup", _ => argument.StartsWith("--", StringComparison.Ordinal) ? argument[2..] : argument };
            if (!allowed.Contains(name, StringComparer.Ordinal) || options.ContainsKey(name))
            {
                throw new ArgumentException($"Unknown, duplicate or inapplicable option '{argument}' for {command}.");
            }
            string? value = null;
            if (name is "temp" or "output" or "source")
            {
                if (++i >= args.Length || args[i].StartsWith('-'))
                {
                    throw new ArgumentException($"{argument} requires a path.");
                }
                value = args[i];
            }
            else if (name == "recursive")
            {
                value = "5";
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int depth))
                {
                    if (depth is < 0 or > 100)
                    {
                        throw new ArgumentException("Recursion depth must be between 0 and 100.");
                    }
                    value = args[++i];
                }
            }
            options.Add(name, value);
        }
        var parsed = new CommandLine(command, positionals.AsReadOnly(), options);
        parsed.Validate();
        return parsed;
    }

    private void Validate()
    {
        bool countValid = Command switch
        {
            "scan" or "cleanup" => Arguments.Count <= 1,
            "convert" => true,
            "inspect" or "backup" => Arguments.Count == 1,
            "restore" => Arguments.Count is 1 or 2 && !(Arguments.Count == 2 && Has("source")),
            "update-check" => Arguments.Count == 0,
            "dependencies" => Arguments.Count >= 1 && Arguments[0] is "check" or "install" &&
                Arguments.Skip(1).All(a => Enum.TryParse<NativeTool>(a, true, out var tool) && Enum.IsDefined(tool)),
            "settings" => Arguments.Count >= 1 && (Arguments[0] switch
            {
                "show" or "add-to-path" => Arguments.Count == 1,
                "temp" => Arguments.Count == 2,
                "tool" => Arguments.Count == 3 && Enum.TryParse<NativeTool>(Arguments[1], true, out var tool) && Enum.IsDefined(tool),
                "reset-tool" => Arguments.Count == 2 && Enum.TryParse<NativeTool>(Arguments[1], true, out var tool) && Enum.IsDefined(tool),
                _ => false
            }),
            _ => false
        };
        if (!countValid)
        {
            throw new ArgumentException($"Invalid arguments for {Command}. Run --help for command syntax.");
        }
        if (Command == "dependencies" && ((Arguments[0] == "check" && (Has("yes") || Has("repair"))) || (Arguments[0] == "install" && Has("json"))))
        {
            throw new ArgumentException("dependencies check supports --json; dependencies install supports --yes and --repair.");
        }
        if (Command == "cleanup" && Has("yes") && !Has("delete-backups"))
        {
            throw new ArgumentException("Unattended cleanup requires both --delete-backups and --yes.");
        }
        if ((Has("plan") && Has("yes")) || (Has("json") && Has("install-dependencies")) ||
            (Command == "settings" && Has("json") && Arguments[0] != "show"))
        {
            throw new ArgumentException("These options cannot be combined. Run --help for supported forms.");
        }
    }
}
