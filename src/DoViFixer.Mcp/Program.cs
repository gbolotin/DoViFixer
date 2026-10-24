using DoViFixer.Mcp.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DoViFixer.Mcp;
/// <summary>Model Context Protocol server over standard input and output, for AI assistants such as Claude Desktop.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // Setup's commands are not configuration.
            Args = AssistantSetup.IsRequest(args) ? [] : args
        });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        }));
        McpComposition.Register(builder.Services, builder.Configuration).WithStdioServerTransport();
        using var host = builder.Build();
        if (AssistantSetup.IsRequest(args))
        {
            return AssistantSetup.Run(host.Services, args);
        }

        await host.RunAsync();
        return 0;
    }
}
