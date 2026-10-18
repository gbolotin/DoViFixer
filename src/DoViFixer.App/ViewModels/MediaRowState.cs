namespace DoViFixer.App.ViewModels;

public enum MediaRowState
{
    NotScanned,
    Active,
    Scanned,
    PlanReady,
    Converted,
    Failed,
    Cancelled,
    Skipped,
    Restored
}

public enum MediaStatusTone
{
    None,
    Neutral,
    Safe,
    LowRisk,
    VerifyRecommended,
    HighRisk
}
