using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;

namespace DoViFixer.Application.Abstractions;

public interface IDependencyDetector
{
    Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> tools, CancellationToken cancellationToken, bool skipConfiguredPaths = false);
    Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken);
}
public interface IDependencyInstaller
{
    Task<InstallationPlan> PrepareAsync(DependencyReport report, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstallationOutcome>> InstallAsync(InstallationPlan approvedPlan,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}
public interface ISettingsStore
{
    Task<UserSettings> ReadAsync(CancellationToken cancellationToken);
    Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken cancellationToken);
}
public interface IToolCatalog
{
    string GetPath(NativeTool tool);
    void Refresh(IEnumerable<DependencyStatus> statuses);
}
