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
    public void DeepInspectionIsAnInspectOnlyOption()
    {
        var command = CommandLine.Parse(["inspect", "movie.mkv", "--deep", "--json"]);
        Assert.IsTrue(command.Has("deep"));
        Assert.IsTrue(command.Has("json"));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(["scan", "--deep"]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(["convert", "movie.mkv", "--deep"]));
    }

    [TestMethod]
    public async Task SingleFelPlanNeedsOnlyOneExecutionApproval()
    {
        var plan = CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.SimpleFel);
        var questions = new List<string>();
        var approval = new DoViFixer.Console.Interaction.FelConversionApproval(new(), (question, yes, token, id) =>
        {
            Assert.IsFalse(yes);
            Assert.AreEqual(plan.Id, id);
            questions.Add(question);
            return Task.FromResult(true);
        });
        var approved = await approval.ConfirmAsync([plan], false, default);
        CollectionAssert.AreEqual(new[] { plan }, approved.ToArray());
        Assert.AreEqual(1, questions.Count);
        StringAssert.Contains(questions[0], "Execute these 1 Simple FEL conversion(s) as planned?");
    }

    [TestMethod]
    public async Task DecliningSimpleFelPreservesApprovedMelAndComplexPlansInInputOrder()
    {
        var plans = new[]
        {
            CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.Mel),
            CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.SimpleFel),
            CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.ComplexFel),
            CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.Mel),
            CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.SimpleFel)
        };
        var questions = new List<string>();
        var approval = new DoViFixer.Console.Interaction.FelConversionApproval(new(), (question, yes, token, id) =>
        {
            questions.Add(question);
            return Task.FromResult(!question.Contains("Simple FEL", StringComparison.Ordinal));
        });
        var approved = await approval.ConfirmAsync(plans, false, default);
        CollectionAssert.AreEqual(new[] { plans[0], plans[2], plans[3] }, approved.ToArray());
        Assert.AreEqual(3, questions.Count);
        StringAssert.Contains(questions[2], "picture data will be lost");
    }

    [TestMethod]
    public async Task DeclinedPlansCannotExecute()
    {
        var approval = new DoViFixer.Console.Interaction.FelConversionApproval(new(), (_, _, _, _) => Task.FromResult(false));
        Assert.AreEqual(0, (await approval.ConfirmAsync([CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.SimpleFel)], false, default)).Count);
    }

    [TestMethod]
    public async Task UnattendedApprovalUsesOneOptionBasedConfirmation()
    {
        int calls = 0;
        var approval = new DoViFixer.Console.Interaction.FelConversionApproval(new(), (_, yes, _, _) =>
        {
            calls++;
            Assert.IsTrue(yes);
            return Task.FromResult(true);
        });
        var plans = new[] { CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.Mel), CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.ComplexFel) };
        CollectionAssert.AreEqual(plans, (await approval.ConfirmAsync(plans, true, default)).ToArray());
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task CancelledApprovalDoesNotPrompt()
    {
        var approval = new DoViFixer.Console.Interaction.FelConversionApproval(new(), (_, _, _, _) => throw new AssertFailedException("Unexpected prompt."));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            approval.ConfirmAsync([CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict.SimpleFel)], false, new CancellationToken(true)));
    }

    private static ConversionPlan CreatePlan(DoViFixer.Domain.Analysis.AnalysisVerdict verdict)
    {
        var media = new DoViFixer.Domain.Media.MediaInfo(new(Guid.NewGuid() + ".mkv", 1000, DateTime.UnixEpoch),
            DoViFixer.Domain.Media.DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 24, 1, 1000, null, [], 0, 0, "", "{}");
        var analysis = new DoViFixer.Domain.Analysis.MediaAnalysis(media,
            new(DoViFixer.Domain.Analysis.AnalysisMethod.FullRpu, DoViFixer.Domain.Media.EnhancementLayer.Fel, 24, 1000, 1, 1),
            verdict, "Metadata heuristic");
        return new(Guid.NewGuid(), analysis, DoViFixer.Domain.Conversion.ConversionTarget.Profile81,
            media.Source.Path, null, null, 1000, analysis.Reason);
    }
    [TestMethod]
    [DataRow(new[] { "convert", "movie.mkv", "--unknown" })]
    [DataRow(new[] { "scan", "movie.mkv", "--yes" })]
    [DataRow(new[] { "convert", "--output" })]
    [DataRow(new[] { "restore" })]
    [DataRow(new[] { "cleanup", "--yes" })]
    [DataRow(new[] { "scan", "-r", "-1" })]
    [DataRow(new[] { "dependencies", "check", "--repair" })]
    [DataRow(new[] { "settings", "add-to-path", "extra" })]
    [DataRow(new[] { "settings", "add-to-path", "--json" })]
    public void InvalidArgumentsFailWithoutStartingHost(string[] args) => Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(args));

    [TestMethod]
    public void UpstreamConversionAndRestoreArgumentsParse()
    {
        var convert = CommandLine.Parse(["convert", "one.mkv", "two.mkv", "folder", "--safe", "--delete", "--backup"]);
        Assert.AreEqual(3, convert.Arguments.Count);
        Assert.IsTrue(convert.Has("safe") && convert.Has("delete") && convert.Has("backup"));
        Assert.AreEqual(1, CommandLine.Parse(["restore", "movie.mkv"]).Arguments.Count);
        Assert.AreEqual("backup.dovi", CommandLine.Parse(["restore", "movie.mkv", "--source", "backup.dovi"]).Value("source"));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(["restore", "movie.mkv", "old.dovi", "--source", "other.dovi"]));
    }

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
