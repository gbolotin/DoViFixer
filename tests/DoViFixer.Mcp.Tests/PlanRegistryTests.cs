using DoViFixer.Application.Cleanup;
using DoViFixer.Mcp.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol;

namespace DoViFixer.Mcp.Tests;
[TestClass]
public sealed class PlanRegistryTests
{
    [TestMethod]
    public void APlanRunsOnce()
    {
        var plans = new PlanRegistry();
        var plan = new CleanupPlan(Guid.NewGuid(), []);
        plans.Add(plan.Id, plan);
        Assert.AreSame(plan, plans.Take<CleanupPlan>([plan.Id]).Single());
        Assert.ThrowsExactly<McpException>(() => plans.Take<CleanupPlan>([plan.Id]));
    }

    [TestMethod]
    public void APlanOfAnotherKindIsRejectedAndKept()
    {
        var plans = new PlanRegistry();
        var plan = new CleanupPlan(Guid.NewGuid(), []);
        plans.Add(plan.Id, plan);
        Assert.ThrowsExactly<McpException>(() => plans.Take<string>([plan.Id]));
        Assert.AreSame(plan, plans.Take<CleanupPlan>([plan.Id]).Single());
    }

    [TestMethod]
    public void OneUnknownIdTakesNothing()
    {
        var plans = new PlanRegistry();
        var plan = new CleanupPlan(Guid.NewGuid(), []);
        plans.Add(plan.Id, plan);
        Assert.ThrowsExactly<McpException>(() => plans.Take<CleanupPlan>([plan.Id, Guid.NewGuid()]));
        Assert.ThrowsExactly<McpException>(() => plans.Take<CleanupPlan>([]));
        Assert.AreEqual(1, plans.Take<CleanupPlan>([plan.Id]).Count);
    }
}
