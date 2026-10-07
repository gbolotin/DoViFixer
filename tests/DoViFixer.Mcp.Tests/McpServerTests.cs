using System.IO.Pipelines;
using System.Text.Json;
using DoViFixer.Mcp.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DoViFixer.Mcp.Tests;
/// <summary>Talks to the real server over in-memory streams, as an assistant would over standard input and output.</summary>
[TestClass]
public sealed class McpServerTests
{
    private string directory = "";

    [TestInitialize]
    public void CreateDirectory() => directory = Directory.CreateTempSubdirectory("dovifixer-mcp-").FullName;

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public async Task TheServerListsEveryOperationAndMarksWhatChangesFiles()
    {
        await using var server = await StartAsync();
        var tools = (await server.Client.ListToolsAsync()).ToDictionary(tool => tool.Name, tool => tool.ProtocolTool);
        CollectionAssert.AreEquivalent(new[] { "scan_media", "inspect_media", "plan_conversion", "start_conversion", "plan_backup", "start_backup", "plan_restore", "start_restore", "list_backups", "delete_backups", "check_dependencies", "plan_dependency_install", "start_dependency_install", "get_settings", "set_tool_path", "set_temporary_directory", "check_for_update", "get_job", "list_jobs", "cancel_job" }, tools.Keys.ToArray());
        Assert.IsTrue(tools["start_conversion"].Annotations!.DestructiveHint);
        Assert.IsTrue(tools["delete_backups"].Annotations!.DestructiveHint);
        Assert.IsTrue(tools["plan_conversion"].Annotations!.ReadOnlyHint);
        Assert.IsTrue(tools["scan_media"].Annotations!.ReadOnlyHint);
        StringAssert.Contains(server.Client.ServerInstructions, "approval");
        Assert.AreEqual("dovifixer", server.Client.ServerInfo.Name);
    }

    [TestMethod]
    public async Task BackupsAreDeletedOnlyThroughTheirListedPlan()
    {
        string archive = Path.Combine(directory, "Movie.dovi");
        await File.WriteAllTextAsync(archive, "archive");
        await using var server = await StartAsync();
        using var listed = JsonDocument.Parse(Text(await server.Client.CallToolAsync("list_backups", new Dictionary<string, object?> { ["path"] = directory })));
        string planId = listed.RootElement.GetProperty("planId").GetString()!;
        Assert.AreEqual(archive, listed.RootElement.GetProperty("files")[0].GetProperty("path").GetString());
        Assert.IsTrue(File.Exists(archive));
        var deleted = await server.Client.CallToolAsync("delete_backups", new Dictionary<string, object?> { ["planId"] = planId });
        Assert.IsFalse(deleted.IsError ?? false, Text(deleted));
        using (var result = JsonDocument.Parse(Text(deleted)))
        {
            Assert.AreEqual("Completed", result.RootElement.GetProperty("status").GetString(), Text(deleted));
        }

        Assert.IsFalse(File.Exists(archive));
        var again = await server.Client.CallToolAsync("delete_backups", new Dictionary<string, object?> { ["planId"] = planId });
        Assert.IsTrue(again.IsError);
        StringAssert.Contains(Text(again), "already started");
    }

    [TestMethod]
    public async Task InvalidArgumentsComeBackAsToolErrors()
    {
        await using var server = await StartAsync();
        var job = await server.Client.CallToolAsync("get_job", new Dictionary<string, object?> { ["jobId"] = Guid.NewGuid().ToString() });
        Assert.IsTrue(job.IsError);
        StringAssert.Contains(Text(job), "No job");
        var depth = await server.Client.CallToolAsync("list_backups", new Dictionary<string, object?> { ["path"] = directory, ["recursiveDepth"] = 101 });
        Assert.IsTrue(depth.IsError);
        StringAssert.Contains(Text(depth), "between 0 and 100");
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private async Task<Server> StartAsync()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = []
        });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        }));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DoViFixer:DataDirectory"] = Path.Combine(directory, "data") });
        McpComposition.Register(builder.Services, builder.Configuration, fileLogging: false).WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        var host = builder.Build();
        await host.StartAsync();
        var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
        return new(host, client);
    }

    private sealed record Server(IHost Host, McpClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Host.StopAsync();
            await ((IAsyncDisposable)Host).DisposeAsync();
        }
    }
}
