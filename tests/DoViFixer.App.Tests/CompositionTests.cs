using DoViFixer.App.Composition;
using DoViFixer.App.ViewModels;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Backup;
using DoViFixer.Application.Cleanup;
using DoViFixer.Application.Conversion;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Restore;
using DoViFixer.Application.Scanning;
using DoViFixer.Application.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Prism.DryIoc;
using Prism.Ioc;

namespace DoViFixer.App.Tests;
[TestClass]
public sealed class CompositionTests
{
    [TestMethod]
    public void SharedRegistrationsResolveInOnePrismRootWithoutSideEffects()
    {
        string root = Path.Combine(Path.GetTempPath(), "DoViFixer-composition-" + Guid.NewGuid());
        var container = new DryIocContainerExtension();
        using var rootContainer = ((IContainerProvider)container).GetContainer();
        AppComposition.Register(container, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DoViFixer:DataDirectory"] = root
        }).Build(), false);
        Type[] services = [typeof(ScanService), typeof(InspectionService), typeof(ConversionPlanner), typeof(ConversionService), typeof(BatchConversionService), typeof(BackupService), typeof(RestoreService), typeof(CleanupService), typeof(DependencyService), typeof(SettingsService), typeof(ShellViewModel)];
        foreach (var type in services)
        {
            Assert.IsNotNull(container.Resolve(type));
        }

        Assert.AreSame(container.Resolve<ISettingsStore>(), container.Resolve<ISettingsStore>());
        Assert.AreSame(container.Resolve<IToolCatalog>(), container.Resolve<IToolCatalog>());
        Assert.AreNotSame(container.Resolve<MediaViewModel>(), container.Resolve<MediaViewModel>());
        Assert.AreNotSame(container.Resolve<ConversionService>(), container.Resolve<ConversionService>());
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void ImportedFactoryUsesSameSingletonAndRootOwnsDisposal()
    {
        var container = new DryIocContainerExtension();
        var services = new ServiceCollection();
        services.AddSingleton<Owned>();
        services.AddTransient(p => new Consumer(p.GetRequiredService<Owned>()));
        container.Populate(services);
        var owned = container.Resolve<Owned>();
        Assert.AreSame(owned, container.Resolve<Consumer>().Owned);
        ((IContainerProvider)container).GetContainer().Dispose();
        Assert.IsTrue(owned.Disposed);
    }

    public sealed class Owned : IDisposable
    {
        public bool Disposed
        {
            get;
            private set;
        }

        public void Dispose() => Disposed = true;
    }

    public sealed record Consumer(Owned Owned);
}
