using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Console.Interaction;

public sealed class ConsoleInteraction(ILogger<ConsoleInteraction> logger)
{
    public bool IsInteractive => !System.Console.IsInputRedirected;

    public void RecordPlan(Guid planId, string action, object details)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["LogKind"] = "Audit" });
        logger.LogInformation(new EventId(2001, "PlanPresented"), "Presented {Action} plan {PlanId}: {@Plan}", action, planId, details);
    }

    public async Task<bool> ConfirmAsync(string question, bool yes, CancellationToken cancellationToken, Guid? planId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (yes)
        {
            OperationLog.Audit(logger, "Approval", question, "ApprovedByOption", planId);
            return true;
        }
        if (!IsInteractive)
        {
            OperationLog.Audit(logger, "Approval", question, "DeclinedNonInteractive", planId);
            return false;
        }
        System.Console.Error.Write($"{question} [y/N] ");
        string? answer = await System.Console.In.ReadLineAsync(cancellationToken);
        bool approved = answer?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
        OperationLog.Audit(logger, "Approval", question, approved ? "ApprovedInteractive" : "DeclinedInteractive", planId);
        return approved;
    }

    public async Task<bool> ConfirmDeletionAsync(Guid plan, bool yes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (yes)
        {
            OperationLog.Audit(logger, "DeleteApproval", "Backup cleanup", "ApprovedByOption", plan);
            return true;
        }
        if (!IsInteractive)
        {
            OperationLog.Audit(logger, "DeleteApproval", "Backup cleanup", "DeclinedNonInteractive", plan);
            return false;
        }
        string code = "APPROVE " + plan.ToString("N")[..8].ToUpperInvariant();
        System.Console.Error.Write($"Permanently delete these exact backups? Type {code}: ");
        bool approved = (await System.Console.In.ReadLineAsync(cancellationToken))?.Trim() == code;
        OperationLog.Audit(logger, "DeleteApproval", "Backup cleanup", approved ? "ApprovedInteractive" : "DeclinedInteractive", plan);
        return approved;
    }
}
