using System.Windows;
using DoViFixer.App.Composition;
using DoViFixer.App.Presentation.Application;
using DoViFixer.App.ViewModels;
using DoViFixer.App.Views;
using DoViFixer.Application.Settings;
using DoViFixer.Application.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WpfFoundation.Notifications;

namespace DoViFixer.App;
public partial class App : System.Windows.Application
{
    private ServiceProvider? services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var registrations = new ServiceCollection();
        AppComposition.Register(registrations, new ConfigurationBuilder().AddEnvironmentVariables().Build());
        services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        RegisterNotifications(services);
        MainWindow = services.GetRequiredService<Shell>();
        var settingsService = services.GetRequiredService<SettingsService>();
        var themeService = services.GetRequiredService<IThemeService>();
        MainWindow.Show();
        try
        {
            var settings = await settingsService.ReadAsync(CancellationToken.None);
            if (services is not null)
            {
                themeService.ApplyTheme(settings.Theme.ToThemePreference());
            }
        }
        catch
        {
            // If settings cannot be loaded, keep the default theme.
        }

        if (MainWindow.DataContext is ShellViewModel shell)
        {
            await shell.InitializeAsync();
        }
    }

    // At every start and before the main window exists, so Windows shows the app's name and icon on its notifications,
    // routes clicks back to this window, and groups the window with the Start menu shortcut it creates.
    private static void RegisterNotifications(IServiceProvider provider)
    {
        try
        {
            provider.GetRequiredService<WindowsNotificationService>().Register("gbolotin.DoViFixer", ApplicationTitle.Name, Path.Combine(AppContext.BaseDirectory, "dovifixer.ico"));
        }
        catch (Exception ex)
        {
            // The app works without notifications; the taskbar still reports operations.
            provider.GetRequiredService<ILogger<App>>().LogWarning(ex, "Could not register for Windows notifications");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        services?.Dispose();
        services = null;
        base.OnExit(e);
    }
}
