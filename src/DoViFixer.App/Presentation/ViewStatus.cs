namespace DoViFixer.App.Presentation;

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
