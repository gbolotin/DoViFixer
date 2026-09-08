namespace DoViFixer.Console.Interaction;

public sealed class ConsoleInteraction
{
    public bool IsInteractive => !System.Console.IsInputRedirected;

    public async Task<bool> ConfirmAsync(string question, bool yes, CancellationToken cancellationToken)
    {
        if (yes)
        {
            return true;
        }
        if (!IsInteractive)
        {
            return false;
        }
        System.Console.Error.Write($"{question} [y/N] ");
        string? answer = await System.Console.In.ReadLineAsync(cancellationToken);
        return answer?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true;
    }

    public async Task<bool> ConfirmDeletionAsync(Guid plan, bool yes, CancellationToken cancellationToken)
    {
        if (yes)
        {
            return true;
        }
        if (!IsInteractive)
        {
            return false;
        }
        string code = "APPROVE " + plan.ToString("N")[..8].ToUpperInvariant();
        System.Console.Error.Write($"Permanently delete these exact backups? Type {code}: ");
        return (await System.Console.In.ReadLineAsync(cancellationToken))?.Trim() == code;
    }
}
