using System.Windows;
using DoViFixer.App.Composition;
using DoViFixer.App.Views;
using Microsoft.Extensions.Configuration;
using Prism.DryIoc;
using Prism.Ioc;

namespace DoViFixer.App;
public partial class App : PrismApplication
{
    protected override Window CreateShell() => Container.Resolve<Shell>();
    protected override void RegisterTypes(IContainerRegistry containerRegistry) => AppComposition.Register((IContainerExtension)containerRegistry, new ConfigurationBuilder().AddEnvironmentVariables().Build());
    protected override void OnExit(ExitEventArgs e)
    {
        Container.GetContainer().Dispose();
        base.OnExit(e);
    }
}
