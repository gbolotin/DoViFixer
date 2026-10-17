namespace DoViFixer.Application.Settings;

/// <summary>CPU topology reported by the operating system, with a conservative fallback.</summary>
public sealed record HardwareProfile(int PhysicalCores, int LogicalProcessors, bool PhysicalCoresDetected)
{
    public int RecommendedConversionWorkers => Math.Clamp(Math.Max(1, PhysicalCores) / 4, 1, 8);
    public int RecommendedAnalysisWorkers => (int)Math.Clamp(Math.Max(1L, PhysicalCores) * 3 / 4, 1, 10);
    public int ConversionWorkers(int configured) => configured == 0 ? RecommendedConversionWorkers : Math.Clamp(configured, 1, 16);
    public int AnalysisWorkers(int configured) => configured == 0 ? RecommendedAnalysisWorkers : Math.Clamp(configured, 1, 10);
}

public enum ToolProcessPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal
}
