using DoViFixer.App.Composition;
using DoViFixer.App.Presentation;
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

namespace DoViFixer.App.Tests;
[TestClass]
public sealed class CompositionTests
{
    [TestMethod]
    public void SharedRegistrationsResolveInOneRootWithoutSideEffects()
    {
        string root = Path.Combine(Path.GetTempPath(), "DoViFixer-composition-" + Guid.NewGuid());
        var registrations = new ServiceCollection();
        AppComposition.Register(registrations, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DoViFixer:DataDirectory"] = root
        }).Build(), false);
        using var container = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Type[] services = [typeof(ScanService), typeof(InspectionService), typeof(ConversionPlanner), typeof(ConversionService), typeof(BatchConversionService), typeof(BackupService), typeof(RestoreService), typeof(CleanupService), typeof(DependencyService), typeof(SettingsService), typeof(ShellViewModel), typeof(IThemeService)];
        foreach (var type in services)
        {
            Assert.IsNotNull(container.GetRequiredService(type));
        }

        Assert.AreSame(container.GetRequiredService<ISettingsStore>(), container.GetRequiredService<ISettingsStore>());
        Assert.AreSame(container.GetRequiredService<IToolCatalog>(), container.GetRequiredService<IToolCatalog>());
        Assert.AreSame(container.GetRequiredService<IThemeService>(), container.GetRequiredService<IThemeService>());
        Assert.AreNotSame(container.GetRequiredService<MediaViewModel>(), container.GetRequiredService<MediaViewModel>());
        Assert.AreNotSame(container.GetRequiredService<ConversionService>(), container.GetRequiredService<ConversionService>());
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void FactoryUsesSameSingletonAndRootOwnsDisposal()
    {
        var services = new ServiceCollection();
        AppComposition.Register(services, new ConfigurationBuilder().Build(), false);
        services.AddSingleton<Owned>();
        services.AddTransient(p => new Consumer(p.GetRequiredService<Owned>()));
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var owned = container.GetRequiredService<Owned>();
        Assert.AreSame(owned, container.GetRequiredService<Consumer>().Owned);
        container.Dispose();
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
