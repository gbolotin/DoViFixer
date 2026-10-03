namespace DoViFixer.App.Presentation.Application;

public enum ViewStatus
{
    Ready,
    FileListCleared,
    Progress,
    Result,
    Error,
    Cancelled,
    ToolsUnavailable,
    BackupNotApproved,
    RestoreNotApproved,
    ConversionNotStarted,
    CleanupNotApproved,
    NoBackupsFound,
    SettingsLoaded,
    SettingsSaved,
    SettingsDiscarded,
    ToolsReady,
    ToolsNeedAttention,
    DependencySetupIncomplete,
    ToolPathSaved,
    ToolOverrideReset
}
