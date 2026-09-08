using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;

namespace DoViFixer.Application.Settings;

public sealed class SettingsService(ISettingsStore store, IDependencyDetector detector, IToolCatalog catalog, IFileOperations files)
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
    }
    public async Task ResetToolAsync(NativeTool tool, CancellationToken cancellationToken)
    {
        await store.UpdateAsync(s => s with { ToolPaths = s.ToolPaths.Remove(tool) }, cancellationToken);
        catalog.Refresh(new[] { new DependencyStatus(tool, DependencyState.Missing, null, null, "Path reset; re-detection required.") });
    }
    public Task SetTemporaryDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        string full = Path.GetFullPath(path);
        files.EnsureAvailableSpace(full, 0);
        return store.UpdateAsync(s => s with { TemporaryDirectory = full }, cancellationToken);
    }
}
