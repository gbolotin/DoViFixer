using System.Text.Json.Nodes;
using DoViFixer.Application.Abstractions;
using DoViFixer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class AssistantRegistrationTests
{
    private const string Server = @"C:\Program Files\DoViFixer\Mcp\DoViFixer.Mcp.exe";
    private const string QuotedServer = "\"C:\\\\Program Files\\\\DoViFixer\\\\Mcp\\\\DoViFixer.Mcp.exe\"";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-assistants-" + Guid.NewGuid().ToString("N"));
    private AssistantFolders Folders => new(Path.Combine(directory, "home"), Path.Combine(directory, "roaming"), Path.Combine(directory, "local"), null, null);
    private AssistantRegistration Registration() => new(Folders, NullLogger<AssistantRegistration>.Instance);
    private static string Normalized(string text) => text.Replace("\r\n", "\n");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void FindsOnlyTheAssistantsWhoseFoldersExist()
    {
        Assert.IsEmpty(Registration().FindInstalled());
        Directory.CreateDirectory(Path.Combine(Folders.LocalAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude"));
        Directory.CreateDirectory(Folders.Codex);

        CollectionAssert.AreEqual(new[] { AssistantClient.ClaudeDesktop, AssistantClient.Codex }, Registration().FindInstalled().ToArray());
    }

    [TestMethod]
    public async Task ClaudeDesktopKeepsOtherServersAndSettings()
    {
        string path = Path.Combine(Folders.ClaudeDesktop, "claude_desktop_config.json");
        Directory.CreateDirectory(Folders.ClaudeDesktop);
        await File.WriteAllTextAsync(path, """{ "globalShortcut": "Ctrl+Space", "mcpServers": { "other": { "command": "other.exe" } } }""");

        Registration().Register(AssistantClient.ClaudeDesktop, Server);
        var registered = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Registration().Unregister(AssistantClient.ClaudeDesktop);
        var unregistered = JsonNode.Parse(await File.ReadAllTextAsync(path))!;

        Assert.AreEqual(Server, (string?)registered["mcpServers"]!["dovifixer"]!["command"]);
        Assert.AreEqual("other.exe", (string?)registered["mcpServers"]!["other"]!["command"]);
        Assert.IsNull(unregistered["mcpServers"]!["dovifixer"]);
        Assert.AreEqual("other.exe", (string?)unregistered["mcpServers"]!["other"]!["command"]);
        Assert.AreEqual("Ctrl+Space", (string?)unregistered["globalShortcut"]);
    }

    [TestMethod]
    public async Task ClaudeDesktopFromTheStoreGetsTheEntryInItsPackage()
    {
        string package = Path.Combine(Folders.LocalAppData, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude");
        Directory.CreateDirectory(package);

        Registration().Register(AssistantClient.ClaudeDesktop, Server);

        var config = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(package, "claude_desktop_config.json")))!;
        Assert.AreEqual(Server, (string?)config["mcpServers"]!["dovifixer"]!["command"]);
        Assert.IsFalse(Directory.Exists(Folders.ClaudeDesktop));
    }

    [TestMethod]
    public async Task ClaudeCodeGetsAUserScopeStdioServer()
    {
        Directory.CreateDirectory(Folders.UserProfile);
        await File.WriteAllTextAsync(Folders.ClaudeCodeFile, """{ "numStartups": 3, "projects": { "C:/Work": { "mcpServers": {} } } }""");

        Registration().Register(AssistantClient.ClaudeCode, Server);

        var config = JsonNode.Parse(await File.ReadAllTextAsync(Folders.ClaudeCodeFile))!;
        Assert.AreEqual("stdio", (string?)config["mcpServers"]!["dovifixer"]!["type"]);
        Assert.AreEqual(Server, (string?)config["mcpServers"]!["dovifixer"]!["command"]);
        Assert.AreEqual(3, (int)config["numStartups"]!);
        Assert.IsNotNull(config["projects"]!["C:/Work"]);
    }

    [TestMethod]
    public void UnregisteringLeavesMissingAndUnrelatedFilesAlone()
    {
        Directory.CreateDirectory(Folders.UserProfile);
        File.WriteAllText(Folders.ClaudeCodeFile, """{"mcpServers":{"other":{}}}""");

        foreach (var client in Enum.GetValues<AssistantClient>())
        {
            Registration().Unregister(client);
        }

        Assert.AreEqual("""{"mcpServers":{"other":{}}}""", File.ReadAllText(Folders.ClaudeCodeFile));
        Assert.IsFalse(Directory.Exists(Folders.Codex));
        Assert.IsFalse(Directory.Exists(Folders.Hermes));
    }

    [TestMethod]
    public async Task CodexTableIsReplacedWithItsSubtables()
    {
        string path = Path.Combine(Folders.Codex, "config.toml");
        Directory.CreateDirectory(Folders.Codex);
        await File.WriteAllTextAsync(path, """
            model = "gpt-5"

            [mcp_servers.other]
            command = "other.exe"

            [mcp_servers.dovifixer]
            command = "old.exe"

            [mcp_servers.dovifixer.env]
            A = "1"

            [profiles.fast]
            model = "x"

            """);

        Registration().Register(AssistantClient.Codex, Server);
        string registered = Normalized(await File.ReadAllTextAsync(path));
        Registration().Unregister(AssistantClient.Codex);
        string unregistered = Normalized(await File.ReadAllTextAsync(path));

        string kept = "model = \"gpt-5\"\n\n[mcp_servers.other]\ncommand = \"other.exe\"\n\n[profiles.fast]\nmodel = \"x\"\n";
        Assert.AreEqual(kept + "\n[mcp_servers.dovifixer]\ncommand = " + QuotedServer + "\n", registered);
        Assert.AreEqual(kept, unregistered);
    }

    [TestMethod]
    public void CodexConfigIsCreatedWhenMissing()
    {
        Assert.AreEqual("[mcp_servers.dovifixer]\ncommand = " + QuotedServer + "\n", Normalized(AssistantRegistration.SetTomlServer("", Server)));
    }

    [TestMethod]
    public async Task HermesEntryGoesInsideItsServersSection()
    {
        string path = Path.Combine(Folders.Hermes, "config.yaml");
        Directory.CreateDirectory(Folders.Hermes);
        await File.WriteAllTextAsync(path, "model:\n  default: x\nmcp_servers:\n  other:\n    command: \"o\"\n  dovifixer:\n    command: \"old\"\n    args: []\n\nterminal:\n  backend: local\n");

        Registration().Register(AssistantClient.Hermes, Server);
        string registered = Normalized(await File.ReadAllTextAsync(path));
        Registration().Register(AssistantClient.Hermes, Server);
        string again = Normalized(await File.ReadAllTextAsync(path));
        Registration().Unregister(AssistantClient.Hermes);
        string unregistered = Normalized(await File.ReadAllTextAsync(path));

        Assert.AreEqual("model:\n  default: x\nmcp_servers:\n  other:\n    command: \"o\"\n  dovifixer:\n    command: " + QuotedServer +
            "\n    args: []\n\nterminal:\n  backend: local\n", registered);
        Assert.AreEqual(registered, again);
        Assert.AreEqual("model:\n  default: x\nmcp_servers:\n  other:\n    command: \"o\"\n\nterminal:\n  backend: local\n", unregistered);
    }

    [TestMethod]
    public void HermesSectionIsAddedAndLeftValidWhenEmptied()
    {
        string registered = AssistantRegistration.SetYamlServer("model:\n  default: x\n", Server);
        string unregistered = AssistantRegistration.SetYamlServer(registered, null);

        Assert.AreEqual("model:\n  default: x\nmcp_servers:\n  dovifixer:\n    command: " + QuotedServer + "\n    args: []\n", Normalized(registered));
        Assert.AreEqual("model:\n  default: x\nmcp_servers: {}\n", Normalized(unregistered));
    }
}
