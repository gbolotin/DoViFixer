using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Settings;

public sealed class SettingsService(ISettingsStore store, IDependencyDetector detector, IToolCatalog catalog, IFileOperations files, ILogger<SettingsService> logger)
{
    public Task<UserSettings> ReadAsync(CancellationToken cancellationToken) => store.ReadAsync(cancellationToken);
    public async Task SetToolAsync(NativeTool tool, string path, CancellationToken cancellationToken)
    {
        var status = await detector.ValidatePathAsync(tool, path, cancellationToken);
        if (status.State != DependencyState.Ready || status.Path is null)
        {
            throw new InvalidOperationException(status.Diagnostic);
        }
        await store.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.SetItem(tool, status.Path) }, cancellationToken);
        catalog.Refresh(new[] { status });
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["Tool"] = tool.ToString(), ["ToolVersion"] = status.Version ?? "unknown" });
        OperationLog.Audit(logger, "SetToolPath", status.Path, "Completed");
    }
    public async Task ResetToolAsync(NativeTool tool, CancellationToken cancellationToken)
    {
        await store.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.Remove(tool) }, cancellationToken);
        catalog.Refresh(new[] { new DependencyStatus(tool, DependencyState.Missing, null, null, "Path reset; re-detection required.") });
        OperationLog.Audit(logger, "ResetToolPath", tool.ToString(), "Completed");
    }
    public async Task SetTemporaryDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string full = Path.GetFullPath(path);
        files.EnsureWritableDirectory(full);
        files.EnsureAvailableSpace(full, 0);
        await store.UpdateAsync(s => s with { TemporaryDirectory = full }, cancellationToken);
        OperationLog.Audit(logger, "SetTemporaryDirectory", full, "Completed");
    }
}
