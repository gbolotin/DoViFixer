using System.ComponentModel;
using DoViFixer.Mcp.Jobs;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DoViFixer.Mcp.Tools;
[McpServerToolType]
public sealed class JobTools(JobRegistry jobs)
{
    [McpServerTool(Name = "get_job", Title = "Get job", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get a job's state, current stage and progress, and its result once it has finished. Jobs run one at a time; later ones wait as Queued.")]
    public string GetJob([Description("Job id returned by another tool.")] string jobId) => ToolViews.Json(jobs.Get(ToolArguments.JobId(jobId)) ?? throw new McpException($"No job {jobId} in this session."));

    [McpServerTool(Name = "list_jobs", Title = "List jobs", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List the jobs started since the server started, without their results.")]
    public string ListJobs() => ToolViews.Json(jobs.List());

    [McpServerTool(Name = "cancel_job", Title = "Cancel job", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Cancel a queued or running job. Originals are kept and temporary files are removed.")]
    public string CancelJob([Description("Job id to cancel.")] string jobId) => jobs.Cancel(ToolArguments.JobId(jobId)) ? "Cancellation requested. Call get_job to see when it has stopped." : "The job has already finished or does not exist.";
}
