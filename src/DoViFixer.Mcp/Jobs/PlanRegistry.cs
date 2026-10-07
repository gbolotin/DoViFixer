using ModelContextProtocol;

namespace DoViFixer.Mcp.Jobs;
/// <summary>
/// Holds plans the assistant has shown the user until they are approved.
/// Execution takes the exact plan that was presented, never a re-analysis, and each plan runs once.
/// </summary>
public sealed class PlanRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, object> plans = [];

    public void Add(Guid id, object plan)
    {
        lock (gate)
        {
            plans[id] = plan;
        }
    }

    public IReadOnlyList<T> Take<T>(IReadOnlyCollection<Guid> ids)
        where T : class
    {
        if (ids.Count == 0)
        {
            throw new McpException("Pass at least one plan id.");
        }

        lock (gate)
        {
            var taken = new List<T>();
            foreach (var id in ids.Distinct())
            {
                if (!plans.TryGetValue(id, out var plan) || plan is not T typed)
                {
                    throw new McpException($"Plan {id} is unknown, of another kind, or already started. Create a new plan.");
                }

                taken.Add(typed);
            }

            foreach (var id in ids)
            {
                plans.Remove(id);
            }

            return taken;
        }
    }
}
