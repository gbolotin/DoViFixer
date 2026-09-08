using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Infrastructure.Configuration;

internal sealed class UserPathRegistration(ILogger<UserPathRegistration> logger) : IUserPathRegistration
{
    public bool AddDirectory(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(directory);
        if (!Directory.Exists(full) || full.Contains(';'))
        {
            throw new ArgumentException("PATH requires an existing directory without semicolons.", nameof(directory));
        }
        string? saved = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        string updated = AppendDirectory(saved, full);
        bool changed = !string.Equals(saved, updated, StringComparison.Ordinal);
        if (changed)
        {
            Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
        }
        string process = AppendDirectory(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process), full);
        Environment.SetEnvironmentVariable("Path", process, EnvironmentVariableTarget.Process);
        OperationLog.Audit(logger, "AddUserPath", full, "Completed");
        return changed;
    }

    internal static string AppendDirectory(string? path, string directory)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        foreach (string entry in (path ?? "").Split(';'))
        {
            string expanded = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expanded))
            {
                continue;
            }
            try
            {
                if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded)), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return path!;
                }
            }
            catch (ArgumentException)
            {
                // Preserve unrelated malformed entries without interpreting them.
            }
        }
        return string.IsNullOrEmpty(path) ? directory : path + (path.EndsWith(';') ? "" : ";") + directory;
    }
}
