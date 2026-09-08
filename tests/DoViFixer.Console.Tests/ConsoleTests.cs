using DoViFixer.Application;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Conversion;
using DoViFixer.Console.Commands;
using DoViFixer.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Console.Tests;

[TestClass]
public sealed class ConsoleTests
{
    [TestMethod]
    [DataRow(new[] { "convert", "movie.mkv", "--unknown" })]
    [DataRow(new[] { "scan", "movie.mkv", "--yes" })]
    [DataRow(new[] { "convert", "--output" })]
    [DataRow(new[] { "restore", "movie.mkv" })]
    [DataRow(new[] { "cleanup", "--yes" })]
    [DataRow(new[] { "scan", "-r", "-1" })]
    [DataRow(new[] { "dependencies", "check", "--repair" })]
    public void InvalidArgumentsFailWithoutStartingHost(string[] args) => Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(args));

    [TestMethod]
    public void ConversionApprovalDoesNotImplyInstallationConsent()
    {
        var command = CommandLine.Parse(new[] { "convert", "movie.mkv", "--yes", "--backup" });
        Assert.IsTrue(command.Has("yes"));
        Assert.IsFalse(command.Has("install-dependencies"));
    }

    [TestMethod]
    public void RecursionAndLiteralPathsParse()
    {
        var command = CommandLine.Parse(new[] { "scan", "-r", "3", "--", "-movie.mkv" });
        Assert.AreEqual(3, command.Depth);
        Assert.AreEqual("-movie.mkv", command.Arguments[0]);
        Assert.AreEqual(5, CommandLine.Parse(new[] { "scan", "-r" }).Depth);
    }

    [TestMethod]
    public async Task HelpAndArgumentErrorsDoNotCreateSettingsOrProbeTools()
    {
        string path = Path.Combine(Path.GetTempPath(), "DoViFixer-help-test-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("DoViFixer__DataDirectory");
        Environment.SetEnvironmentVariable("DoViFixer__DataDirectory", path);
        try
        {
            Assert.AreEqual(0, await DoViFixer.Console.Program.Main(new[] { "--help" }));
            Assert.AreEqual(2, await DoViFixer.Console.Program.Main(new[] { "invalid-command" }));
            Assert.IsFalse(Directory.Exists(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DoViFixer__DataDirectory", previous);
        }
    }

    [TestMethod]
    public void ContainerResolvesWorkflowGraphWithoutProbingAndDisposesSingletons()
    {
        string path = Path.Combine(Path.GetTempPath(), "DoViFixer-DI-test-" + Guid.NewGuid().ToString("N"));
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["DoViFixer:DataDirectory"] = path;
        builder.Logging.ClearProviders();
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        builder.Services.AddDoViFixerApplication().AddDoViFixerInfrastructure(builder.Configuration);
        builder.Services.AddSingleton<OwnedResource>();
        OwnedResource owned;
        using (var host = builder.Build())
        {
            Assert.AreNotSame(host.Services.GetRequiredService<ConversionService>(), host.Services.GetRequiredService<ConversionService>());
            Assert.AreSame(host.Services.GetRequiredService<ISettingsStore>(), host.Services.GetRequiredService<ISettingsStore>());
            Assert.AreSame(host.Services.GetRequiredService<IToolCatalog>(), host.Services.GetRequiredService<IToolCatalog>());
            owned = host.Services.GetRequiredService<OwnedResource>();
            Assert.IsFalse(Directory.Exists(path));
        }
        Assert.IsTrue(owned.Disposed);
    }

    private sealed class OwnedResource : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
