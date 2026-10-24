using DoViFixer.Application.Abstractions;
using DoViFixer.Mcp.Composition;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Mcp.Tests;

[TestClass]
public sealed class AssistantSetupTests
{
    [TestMethod]
    public void RecognizesOnlySetupCommands()
    {
        Assert.IsTrue(AssistantSetup.IsRequest(["--register", "codex"]));
        Assert.IsTrue(AssistantSetup.IsRequest(["--UNREGISTER"]));
        Assert.IsFalse(AssistantSetup.IsRequest([]));
        Assert.IsFalse(AssistantSetup.IsRequest(["--DoViFixer:DataDirectory=D:\\Test"]));
    }

    [TestMethod]
    public void AcceptsTheNamesSetupPasses()
    {
        Assert.AreEqual(AssistantClient.ClaudeDesktop, AssistantSetup.Parse("claude-desktop"));
        Assert.AreEqual(AssistantClient.ClaudeCode, AssistantSetup.Parse("claude-code"));
        Assert.AreEqual(AssistantClient.Codex, AssistantSetup.Parse("codex"));
        Assert.AreEqual(AssistantClient.Hermes, AssistantSetup.Parse("Hermes"));
    }
}
