using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DoViFixer.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Configuration;

/// <summary>The folders the assistants keep their configuration in; tests point them at a temporary folder.</summary>
internal sealed record AssistantFolders(string UserProfile, string RoamingAppData, string LocalAppData, string? CodexHome, string? HermesHome)
{
    public static AssistantFolders Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("CODEX_HOME"),
        Environment.GetEnvironmentVariable("HERMES_HOME"));

    public string ClaudeDesktop => Path.Combine(RoamingAppData, "Claude");
    public string ClaudeCodeFile => Path.Combine(UserProfile, ".claude.json");
    public string Codex => string.IsNullOrEmpty(CodexHome) ? Path.Combine(UserProfile, ".codex") : CodexHome;
    public string Hermes => string.IsNullOrEmpty(HermesHome) ? Path.Combine(UserProfile, ".hermes") : HermesHome;

    /// <summary>
    /// Claude Desktop from the Microsoft Store keeps its AppData inside its package unless the file already exists outside,
    /// so both copies get the entry.
    /// </summary>
    public IEnumerable<string> ClaudeDesktopFolders()
    {
        if (Directory.Exists(ClaudeDesktop))
        {
            yield return ClaudeDesktop;
        }

        string packages = Path.Combine(LocalAppData, "Packages");
        foreach (string package in Directory.Exists(packages) ? Directory.EnumerateDirectories(packages, "Claude_*") : [])
        {
            string folder = Path.Combine(package, "LocalCache", "Roaming", "Claude");
            if (Directory.Exists(folder))
            {
                yield return folder;
            }
        }
    }
}

/// <summary>
/// Edits each assistant's own file: JSON for Claude Desktop and Claude Code, TOML for Codex, YAML for Hermes. Only the
/// "dovifixer" entry changes; the text around it stays as the user wrote it.
/// </summary>
internal sealed class AssistantRegistration(AssistantFolders folders, ILogger<AssistantRegistration> logger) : IAssistantRegistration
{
    public const string ServerName = "dovifixer";
    // Relaxed escaping keeps non-ASCII text, such as names in paths, readable in files the user also edits.
    private static readonly JsonSerializerOptions json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonDocumentOptions reading = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public IReadOnlyList<AssistantClient> FindInstalled()
    {
        var found = new List<AssistantClient>();
        if (folders.ClaudeDesktopFolders().Any())
        {
            found.Add(AssistantClient.ClaudeDesktop);
        }

        if (File.Exists(folders.ClaudeCodeFile) || Directory.Exists(Path.Combine(folders.UserProfile, ".claude")))
        {
            found.Add(AssistantClient.ClaudeCode);
        }

        if (Directory.Exists(folders.Codex))
        {
            found.Add(AssistantClient.Codex);
        }

        if (Directory.Exists(folders.Hermes))
        {
            found.Add(AssistantClient.Hermes);
        }

        return found;
    }

    public void Register(AssistantClient client, string serverPath)
    {
        string command = Path.GetFullPath(serverPath);
        switch (client)
        {
            case AssistantClient.ClaudeDesktop:
                foreach (string folder in ClaudeDesktopFoldersOrDefault())
                {
                    EditJson(Path.Combine(folder, "claude_desktop_config.json"), servers => servers[ServerName] = new JsonObject { ["command"] = command });
                }
                break;
            case AssistantClient.ClaudeCode:
                EditJson(folders.ClaudeCodeFile, servers => servers[ServerName] = new JsonObject
                {
                    ["type"] = "stdio", ["command"] = command, ["args"] = new JsonArray(), ["env"] = new JsonObject()
                });
                break;
            case AssistantClient.Codex:
                EditText(Path.Combine(folders.Codex, "config.toml"), text => SetTomlServer(text, command));
                break;
            case AssistantClient.Hermes:
                EditText(Path.Combine(folders.Hermes, "config.yaml"), text => SetYamlServer(text, command));
                break;
        }

        logger.LogInformation("Added the MCP server {Command} to {Client}", command, client);
    }

    public void Unregister(AssistantClient client)
    {
        switch (client)
        {
            case AssistantClient.ClaudeDesktop:
                foreach (string folder in folders.ClaudeDesktopFolders())
                {
                    EditExistingJson(Path.Combine(folder, "claude_desktop_config.json"));
                }
                break;
            case AssistantClient.ClaudeCode:
                EditExistingJson(folders.ClaudeCodeFile);
                break;
            case AssistantClient.Codex:
                EditExistingText(Path.Combine(folders.Codex, "config.toml"), text => SetTomlServer(text, null));
                break;
            case AssistantClient.Hermes:
                EditExistingText(Path.Combine(folders.Hermes, "config.yaml"), text => SetYamlServer(text, null));
                break;
        }

        logger.LogInformation("Removed the MCP server from {Client}", client);
    }

    private IEnumerable<string> ClaudeDesktopFoldersOrDefault()
    {
        var found = folders.ClaudeDesktopFolders().ToList();
        return found.Count > 0 ? found : [folders.ClaudeDesktop];
    }

    private static void EditJson(string path, Action<JsonObject> edit)
    {
        EditText(path, text =>
        {
            var root = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text, documentOptions: reading) as JsonObject ??
                throw new InvalidDataException($"{path} does not hold a JSON object.");
            if (root["mcpServers"] is not JsonObject servers)
            {
                servers = new JsonObject();
                root["mcpServers"] = servers;
            }

            edit(servers);
            return root.ToJsonString(json) + Environment.NewLine;
        });
    }

    private static void EditExistingJson(string path)
    {
        if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path), documentOptions: reading) is JsonObject { } root &&
            root["mcpServers"] is JsonObject servers && servers.ContainsKey(ServerName))
        {
            EditJson(path, existing => existing.Remove(ServerName));
        }
    }

    private static void EditExistingText(string path, Func<string, string> edit)
    {
        if (File.Exists(path))
        {
            EditText(path, edit);
        }
    }

    /// <summary>Writes through a temporary file, so the assistant never reads a half-written configuration.</summary>
    private static void EditText(string path, Func<string, string> edit)
    {
        string text = File.Exists(path) ? File.ReadAllText(path) : "";
        string updated = edit(text);
        if (updated == text)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, updated, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // TOML and YAML double-quoted strings share these escapes.
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string[] Lines(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static string Join(IEnumerable<string> lines)
    {
        string text = string.Join(Environment.NewLine, lines).TrimEnd();
        return text.Length == 0 ? "" : text + Environment.NewLine;
    }

    /// <summary>Replaces the [mcp_servers.dovifixer] table and its subtables, or removes them when command is null.</summary>
    internal static string SetTomlServer(string text, string? command)
    {
        string table = "mcp_servers." + ServerName;
        var kept = new List<string>();
        bool inServer = false;
        foreach (string line in Lines(text))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                string name = trimmed.Trim('[', ']').Trim();
                inServer = name == table || name.StartsWith(table + ".", StringComparison.Ordinal);
            }

            if (!inServer)
            {
                kept.Add(line);
            }
        }

        if (command is not null)
        {
            if (kept.Count > 0 && kept[^1].Trim().Length > 0)
            {
                kept.Add("");
            }

            kept.Add($"[{table}]");
            kept.Add($"command = {Quote(command)}");
        }

        return Join(kept);
    }

    /// <summary>
    /// Replaces the dovifixer entry under the top-level mcp_servers key, or removes it when command is null. Other servers
    /// and keys keep their lines.
    /// </summary>
    internal static string SetYamlServer(string text, string? command)
    {
        var lines = Lines(text).ToList();
        int section = lines.FindIndex(line => line.TrimEnd() is "mcp_servers:" or "mcp_servers: {}" or "mcp_servers: null" or "mcp_servers: ~");
        if (section < 0)
        {
            if (command is null)
            {
                return text;
            }

            lines.Add("mcp_servers:");
            section = lines.Count - 1;
        }
        else
        {
            lines[section] = "mcp_servers:";
        }

        // The section runs to the next line that is not indented; its entries use the indent of its first entry.
        int end = section + 1;
        while (end < lines.Count && (lines[end].Trim().Length == 0 || lines[end].StartsWith(' ') || lines[end].TrimStart().StartsWith('#')))
        {
            end++;
        }

        string indent = "  ";
        for (int index = section + 1; index < end; index++)
        {
            if (lines[index].Trim().Length > 0 && !lines[index].TrimStart().StartsWith('#'))
            {
                indent = lines[index][..(lines[index].Length - lines[index].TrimStart().Length)];
                break;
            }
        }

        int start = lines.FindIndex(section + 1, end - section - 1, line => line == indent + ServerName + ":");
        if (start >= 0)
        {
            int stop = start + 1;
            while (stop < end && (lines[stop].Trim().Length == 0 || lines[stop].StartsWith(indent + " ")))
            {
                stop++;
            }

            // Blank lines after the entry separate it from what follows; they stay.
            while (stop > start + 1 && lines[stop - 1].Trim().Length == 0)
            {
                stop--;
            }

            lines.RemoveRange(start, stop - start);
            end -= stop - start;
        }

        if (command is not null)
        {
            // Insert after the last entry line, before trailing blank lines.
            int at = end;
            while (at > section + 1 && lines[at - 1].Trim().Length == 0)
            {
                at--;
            }

            lines.InsertRange(at, [indent + ServerName + ":", indent + indent + "command: " + Quote(command), indent + indent + "args: []"]);
        }
        else if (!lines.Skip(section + 1).Take(end - section - 1).Any(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#')))
        {
            // The section is empty now; an empty mapping keeps the YAML valid.
            lines[section] = "mcp_servers: {}";
        }

        return Join(lines);
    }
}
