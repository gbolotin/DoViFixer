using DoViFixer.Application.Settings;
using WpfFoundation.Theming;

namespace DoViFixer.App.Presentation.Application;

/// <summary>Maps the persisted theme setting to the WpfFoundation theme service.</summary>
public static class AppThemeMapping
{
    public static ThemePreference ToThemePreference(this AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemePreference.Light,
        AppTheme.Dark => ThemePreference.Dark,
        _ => ThemePreference.System
    };
}
