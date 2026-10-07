using DoViFixer.Application.Dependencies;
using ModelContextProtocol;

namespace DoViFixer.Mcp.Tools;
/// <summary>Validates tool arguments with the same limits as the console commands; failures go back to the assistant as tool errors.</summary>
internal static class ToolArguments
{
    public static void Depth(int recursiveDepth)
    {
        if (recursiveDepth is < 0 or > 100)
        {
            throw new McpException("recursiveDepth must be between 0 and 100.");
        }
    }

    public static IReadOnlyList<Guid> PlanIds(IEnumerable<string> planIds) => [.. planIds.Select(id => Guid.TryParse(id, out var guid) ? guid : throw new McpException($"'{id}' is not a plan id."))];

    public static Guid JobId(string jobId) => Guid.TryParse(jobId, out var id) ? id : throw new McpException($"'{jobId}' is not a job id.");

    public static IReadOnlyList<NativeTool> Tools(string[]? tools) => tools is null or [] ? DependencyRequirements.All : [.. tools.Select(Tool)];

    public static NativeTool Tool(string tool) => Enum.TryParse<NativeTool>(tool, true, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw new McpException($"Unknown tool '{tool}'. Tools: {string.Join(", ", Enum.GetNames<NativeTool>())}.");
}
