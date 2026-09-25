using System.Windows;
using DoViFixer.App.Composition;
using DoViFixer.App.Presentation;
using DoViFixer.App.Views;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        MainWindow = services.GetRequiredService<Shell>();
        var settingsService = services.GetRequiredService<SettingsService>();
        var themeService = services.GetRequiredService<IThemeService>();
        MainWindow.Show();
        try
        {
            var settings = await settingsService.ReadAsync(CancellationToken.None);
            if (services is not null)
            {
                themeService.ApplyTheme(settings.Theme);
            }
        }
        catch
        {
            // If settings cannot be loaded, default theme remains active.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        services?.Dispose();
        services = null;
        base.OnExit(e);
    }
}
