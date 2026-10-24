namespace DoViFixer.Application.Abstractions;

/// <summary>Adds DoViFixer's MCP server to, or removes it from, the configuration of AI assistants on this PC.</summary>
public interface IAssistantRegistration
{
    /// <summary>The assistants whose configuration folder exists for this user.</summary>
    IReadOnlyList<AssistantClient> FindInstalled();
    void Register(AssistantClient client, string serverPath);
    /// <summary>Removes DoViFixer's entry and leaves the rest of the configuration as it was.</summary>
    void Unregister(AssistantClient client);
}

public enum AssistantClient
{
    ClaudeDesktop,
    ClaudeCode,
    Codex,
    Hermes
}
