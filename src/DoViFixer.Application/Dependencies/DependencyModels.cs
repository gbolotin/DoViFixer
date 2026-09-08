namespace DoViFixer.Application.Dependencies;

public enum NativeTool { FFmpeg, FFprobe, MkvMerge, MkvExtract, MediaInfo, DoviTool }
public enum DependencyState { Ready, Missing, Incompatible, Unusable }
public enum InstallationProvider { WinGet, VerifiedZip }
public sealed record DependencyStatus(NativeTool Tool, DependencyState State, string? Path, string? Version, string Diagnostic);
public sealed record DependencyReport(IReadOnlyList<DependencyStatus> Tools)
{
    public bool Ready => Tools.All(t => t.State == DependencyState.Ready);
}
public sealed record InstallationItem(string Id, string Version, InstallationProvider Provider,
    string Source, string Destination, string Scope, bool RequiresElevation,
    IReadOnlyList<NativeTool> Tools, string? Sha256 = null);
public sealed record InstallationPlan(Guid Id, IReadOnlyList<InstallationItem> Items, IReadOnlyList<string> Unavailable);
public sealed record InstallationOutcome(string Id, bool Succeeded, string Message);
public sealed record DependencyInstallationResult(IReadOnlyList<InstallationOutcome> Outcomes, DependencyReport Report);

public static class DependencyRequirements
{
    public static readonly IReadOnlyList<NativeTool> All = Array.AsReadOnly(Enum.GetValues<NativeTool>());
    public static readonly IReadOnlyList<NativeTool> Probe = Array.AsReadOnly(new[] { NativeTool.MkvMerge, NativeTool.MediaInfo });
    public static readonly IReadOnlyList<NativeTool> Analysis = All;
}
