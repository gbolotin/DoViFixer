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
    public async Task FelPromptsRememberSeparateAnswersForEachCategory()
    {
        var questions = new List<string>();
        var approval = new DoViFixer.Console.Interaction.FelConversionApproval(new(), (question, yes, token, id) =>
        {
            Assert.IsFalse(yes);
            questions.Add(question);
            return Task.FromResult(question.Contains("Complex FEL", StringComparison.Ordinal));
        });
        var media = new DoViFixer.Domain.Media.MediaInfo(new("fixture.mkv", 1000, DateTime.UnixEpoch),
            DoViFixer.Domain.Media.DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 24, 1, 1000, null, [], 0, 0, "", "{}");
        var analysis = new DoViFixer.Domain.Analysis.MediaAnalysis(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, DoViFixer.Domain.Media.EnhancementLayer.Fel, 24, 1000, 1, 1),
            DoViFixer.Domain.Analysis.AnalysisVerdict.SimpleFel, "Metadata heuristic");
        Assert.IsFalse(await approval.ConfirmAsync(analysis, default));
        Assert.IsFalse(await approval.ConfirmAsync(analysis, default));
        analysis = analysis with { Verdict = DoViFixer.Domain.Analysis.AnalysisVerdict.ComplexFel };
        Assert.IsTrue(await approval.ConfirmAsync(analysis, default));
        Assert.IsTrue(await approval.ConfirmAsync(analysis, default));
        Assert.AreEqual(2, questions.Count);
        StringAssert.Contains(questions[1], "picture data will be lost");
    }

    [TestMethod]
    [DataRow(new[] { "convert", "movie.mkv", "--unknown" })]
    [DataRow(new[] { "scan", "movie.mkv", "--yes" })]
    [DataRow(new[] { "convert", "--output" })]
    [DataRow(new[] { "restore", "movie.mkv" })]
    [DataRow(new[] { "cleanup", "--yes" })]
    [DataRow(new[] { "scan", "-r", "-1" })]
    [DataRow(new[] { "dependencies", "check", "--repair" })]
    [DataRow(new[] { "settings", "add-to-path", "extra" })]
    [DataRow(new[] { "settings", "add-to-path", "--json" })]
    public void InvalidArgumentsFailWithoutStartingHost(string[] args) => Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(args));

    [TestMethod]
    public void ConversionApprovalDoesNotImplyInstallationConsent()
    {
        var command = CommandLine.Parse(new[] { "convert", "movie.mkv", "--yes", "--backup" });
        Assert.IsTrue(command.Has("yes"));
        Assert.IsFalse(command.Has("install-dependencies"));
    }

    [TestMethod]
    public void AddToPathParsesWithoutEnvironmentChanges()
    {
        string? saved = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        var command = CommandLine.Parse(new[] { "settings", "add-to-path" });
        Assert.AreEqual("settings", command.Command);
        Assert.AreEqual("add-to-path", command.Arguments.Single());
        Assert.AreEqual(saved, Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
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
