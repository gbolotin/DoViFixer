using DoViFixer.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Mcp.Composition;

/// <summary>
/// Setup's commands: <c>--register claude-desktop claude-code codex hermes</c> adds this server to the named assistants,
/// <c>--unregister</c> removes it from all of them. They write nothing to standard output and exit with 0 or 1.
/// </summary>
public static class AssistantSetup
{
    private const string RegisterArgument = "--register";
    private const string UnregisterArgument = "--unregister";

    public static bool IsRequest(string[] args) => args.Length > 0 &&
        (args[0].Equals(RegisterArgument, StringComparison.OrdinalIgnoreCase) || args[0].Equals(UnregisterArgument, StringComparison.OrdinalIgnoreCase));

    public static int Run(IServiceProvider services, string[] args)
    {
        var registration = services.GetRequiredService<IAssistantRegistration>();
        var logger = services.GetRequiredService<ILogger<IAssistantRegistration>>();
        int result = 0;
        bool register = args[0].Equals(RegisterArgument, StringComparison.OrdinalIgnoreCase);
        IEnumerable<AssistantClient> clients = args.Length > 1 ? args.Skip(1).Select(Parse) : Enum.GetValues<AssistantClient>();
        foreach (var client in clients)
        {
            try
            {
                if (register)
                {
                    registration.Register(client, Environment.ProcessPath!);
                }
                else
                {
                    registration.Unregister(client);
                }
            }
            catch (Exception ex)
            {
                // One damaged configuration does not stop the others.
                logger.LogError(ex, "Could not change the MCP server entry of {Client}", client);
                result = 1;
            }
        }

        return result;
    }

    /// <summary>Accepts the names setup passes, such as claude-desktop, as well as the enum names.</summary>
    internal static AssistantClient Parse(string name) => Enum.Parse<AssistantClient>(name.Replace("-", ""), ignoreCase: true);
}
