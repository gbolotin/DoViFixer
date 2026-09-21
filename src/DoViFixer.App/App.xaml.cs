using System.Windows;
using DoViFixer.App.Composition;
using DoViFixer.App.Presentation;
using DoViFixer.App.Views;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.Configuration;
using Prism.DryIoc;
using Prism.Ioc;

namespace DoViFixer.App;
public partial class App : PrismApplication
{
    protected override Window CreateShell() => Container.Resolve<Shell>();
    protected override void RegisterTypes(IContainerRegistry containerRegistry) => AppComposition.Register((IContainerExtension)containerRegistry, new ConfigurationBuilder().AddEnvironmentVariables().Build());

    protected override async void OnInitialized()
    {
        base.OnInitialized();
        try
        {
            var settings = await Container.Resolve<SettingsService>().ReadAsync(CancellationToken.None);
            Container.Resolve<IThemeService>().ApplyTheme(settings.Theme);
        }
        catch
        {
            // If settings cannot be loaded, default theme remains active.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Container.GetContainer().Dispose();
        base.OnExit(e);
    }
}
