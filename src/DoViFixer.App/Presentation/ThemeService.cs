using System.Windows;
using DoViFixer.Application.Settings;

namespace DoViFixer.App.Presentation;

public interface IThemeService
{
    AppTheme CurrentTheme
    {
        get;
    }

    void ApplyTheme(AppTheme theme);
}

public sealed class ThemeService : IThemeService
{
    private AppTheme currentTheme = AppTheme.System;

    public AppTheme CurrentTheme => currentTheme;

    public void ApplyTheme(AppTheme theme)
    {
        currentTheme = theme;
        if (System.Windows.Application.Current is { } app)
        {
            if (app.Dispatcher.CheckAccess())
            {
                app.ThemeMode = Map(theme);
            }
            else
            {
                app.Dispatcher.Invoke(() => app.ThemeMode = Map(theme));
            }
        }
    }

    private static ThemeMode Map(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeMode.Light,
        AppTheme.Dark => ThemeMode.Dark,
        _ => ThemeMode.System
    };
}
