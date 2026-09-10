using System.Text.Json;
using DoViFixer.Application.Operations;
using DoViFixer.Console.Composition;
using DoViFixer.Console.Interaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;

namespace DoViFixer.Console.Tests;

[TestClass]
public sealed class LoggingTests
{
    private string directory = null!;

    [TestMethod]
    public void ConsoleElapsedTimeIsReadableWhileFileLogsKeepExactMilliseconds()
    {
        var previousError = System.Console.Error;
        using var output = new StringWriter();
        System.Console.SetError(output);
        try
        {
            using var serilog = LoggingConfiguration.Configure(new LoggerConfiguration(), Configuration()).CreateLogger();
            using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false));
            var logger = factory.CreateLogger("Test");
            foreach (double elapsed in new[] { 192349.0141, 250.5, 1250.0, 3723000.0, 93784000.0 })
            {
                OperationLog.History(logger, "Failed", elapsed);
            }
        }
        finally
        {
            System.Console.SetError(previousError);
        }
        foreach (string expected in new[] { "3m 12s", "250.5 ms", "1.25 s", "1h 2m 3s", "1d 2h 3m 4s" })
        {
            StringAssert.Contains(output.ToString(), "Operation Failed after " + expected);
        }
        foreach (string kind in new[] { "history", "diagnostic" })
        {
            var entry = Read(kind)[0];
            Assert.AreEqual(192349.0141, entry.GetProperty("Properties").GetProperty("ElapsedMilliseconds").GetDouble());
            Assert.AreEqual("Operation {Outcome} after {ElapsedMilliseconds} ms", entry.GetProperty("MessageTemplate").GetString());
            StringAssert.Contains(entry.GetProperty("RenderedMessage").GetString()!, "192349.0141 ms");
            Assert.IsFalse(entry.GetProperty("Properties").TryGetProperty("ConsoleElapsed", out _));
        }
    }

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "DoViFixer-logging-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private IConfiguration Configuration(params (string Name, string Value)[] values) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DoViFixer:DataDirectory"] = directory }
            .Concat(values.Select(v => new KeyValuePair<string, string?>("DoViFixer:Logging:" + v.Name, v.Value))))
        .Build();

    private JsonElement[] Read(string kind) => Directory.EnumerateFiles(Path.Combine(directory, "Logs"), kind + "-*.jsonl")
        .SelectMany(File.ReadLines).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();

    private static string? Property(JsonElement entry, string name) =>
        entry.GetProperty("Properties").TryGetProperty(name, out var property) ? property.ToString() : null;

    [TestMethod]
    public async Task HistoryAndAuditPersistEvenWhenDiagnosticsAreFiltered()
    {
        var operationId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        using (var serilog = LoggingConfiguration.Configure(new LoggerConfiguration(), Configuration(("DiagnosticLevel", "Warning"))).CreateLogger())
        using (var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false)))
        {
            var logger = factory.CreateLogger("Test");
            await OperationLog.RunAsync(logger, "TestOperation", operationId, "movie.mkv", async () =>
            {
                logger.LogDebug("Debug message excluded");
                var interaction = new ConsoleInteraction(factory.CreateLogger<ConsoleInteraction>());
                interaction.RecordPlan(planId, "Backup", new { Input = "movie.mkv", Output = "movie.dovi" });
                Assert.IsTrue(await interaction.ConfirmAsync("Create archive?", true, default, planId));
                OperationLog.Audit(logger, "PublishBackup", "movie.dovi", "Completed", planId);
                return 0;
            }, default);
        }
        var history = Read("history");
        CollectionAssert.AreEquivalent(new[] { "Started", "Completed" }, history.Select(e => Property(e, "Outcome")).ToArray());
        Assert.IsTrue(history.All(e => Property(e, "OperationId") == operationId.ToString()));
        Assert.IsTrue(history.All(e => Property(e, "LogKind") == "History" && Property(e, "SessionId") is not null));
        var audit = Read("audit");
        Assert.AreEqual(3, audit.Length);
        Assert.IsTrue(audit.All(e => Property(e, "PlanId") == planId.ToString() && Property(e, "LogKind") == "Audit"));
        Assert.AreEqual("movie.dovi", audit[0].GetProperty("Properties").GetProperty("Plan").GetProperty("Output").GetString());
        Assert.AreEqual(0, Read("diagnostic").Length);
    }

    [TestMethod]
    public async Task ConcurrentOperationsKeepTheirIdsAndRetainFullExceptions()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using (var serilog = LoggingConfiguration.Configure(new LoggerConfiguration(), Configuration()).CreateLogger())
        using (var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false)))
        {
            var logger = factory.CreateLogger("Test");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<int> one = OperationLog.RunAsync(logger, "First", first, "first.mkv", async () =>
            {
                started.SetResult();
                await release.Task;
                logger.LogDebug("First diagnostic");
                return 1;
            }, default);
            await started.Task;
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => OperationLog.RunAsync<int>(logger, "Second", second, "second.mkv",
                () => throw new InvalidDataException("Verification fixture failed", new IOException("Fixture inner cause")), default));
            release.SetResult();
            await one;
            logger.LogDebug("Outside operation");
        }
        var history = Read("history");
        Assert.AreEqual(4, history.Length);
        Assert.IsTrue(history.Where(e => Property(e, "OperationId") == first.ToString()).All(e => Property(e, "Operation") == "First"));
        var failure = history.Single(e => Property(e, "Outcome") == "Failed");
        Assert.AreEqual(second.ToString(), Property(failure, "OperationId"));
        StringAssert.Contains(failure.GetProperty("Exception").GetString()!, "Fixture inner cause");
        StringAssert.Contains(failure.GetProperty("Exception").GetString()!, "OperationLog.RunAsync");
        var outside = Read("diagnostic").Single(e => e.GetProperty("RenderedMessage").GetString() == "Outside operation");
        Assert.IsNull(Property(outside, "OperationId"));
    }

    [TestMethod]
    public async Task CancellationHasOneTerminalHistoryEvent()
    {
        using (var serilog = LoggingConfiguration.Configure(new LoggerConfiguration(), Configuration()).CreateLogger())
        using (var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog, dispose: false)))
        using (var cancellation = new CancellationTokenSource())
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => OperationLog.RunAsync<int>(factory.CreateLogger("Test"), "Cancel", Guid.NewGuid(), null, () =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
                return Task.FromResult(0);
            }, cancellation.Token));
        }
        CollectionAssert.AreEqual(new[] { "Started", "Cancelled" }, Read("history").Select(e => Property(e, "Outcome")).ToArray());
    }

    [TestMethod]
    public void RollingFilesHonorRetentionAndKeepTheNewestEvent()
    {
        using (var logger = LoggingConfiguration.Configure(new LoggerConfiguration(), Configuration(("FileSizeLimitBytes", "512"), ("DiagnosticRetainedFiles", "2"))).CreateLogger())
        {
            for (int i = 0; i < 10; i++)
            {
                logger.Debug("Event {Index} {Payload}", i, new string('x', 600));
            }
        }
        Assert.AreEqual(2, Directory.GetFiles(Path.Combine(directory, "Logs"), "diagnostic-*.jsonl").Length);
        Assert.IsTrue(Read("diagnostic").Any(e => Property(e, "Index") == "9"));
    }

    [TestMethod]
    public async Task RealHostFlushesSettingsAuditAndLeavesJsonOutputValid()
    {
        string? previous = Environment.GetEnvironmentVariable("DoViFixer__DataDirectory");
        var previousOut = System.Console.Out;
        using var output = new StringWriter();
        Environment.SetEnvironmentVariable("DoViFixer__DataDirectory", directory);
        System.Console.SetOut(output);
        try
        {
            Assert.AreEqual(0, await DoViFixer.Console.Program.Main(["settings", "temp", Path.GetTempPath()]));
            output.GetStringBuilder().Clear();
            Assert.AreEqual(0, await DoViFixer.Console.Program.Main(["settings", "show", "--json"]));
            using var parsed = JsonDocument.Parse(output.ToString());
            Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()), parsed.RootElement.GetProperty("TemporaryDirectory").GetString());
            Assert.IsTrue(Read("audit").Any(e => Property(e, "Action") == "SetTemporaryDirectory" && Property(e, "Outcome") == "Completed"));
            var change = Read("audit").Single(e => Property(e, "Setting") == "TemporaryDirectory");
            Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()), Property(change, "NewValue"));
            Assert.IsTrue(change.GetProperty("Properties").GetProperty("PreviousValue").ValueKind == JsonValueKind.Null);
            Assert.AreEqual(4, Read("history").Length);
            Assert.AreEqual(2, Read("history").Select(e => Property(e, "SessionId")).Distinct().Count());
            Assert.IsTrue(Read("diagnostic").Any(e => e.GetProperty("RenderedMessage").GetString() == "Host stopped; flushing log files"));
        }
        finally
        {
            System.Console.SetOut(previousOut);
            Environment.SetEnvironmentVariable("DoViFixer__DataDirectory", previous);
        }
    }

    [TestMethod]
    public void InvalidConfigurationAndUnwritableDirectoryFailVisibly()
    {
        Assert.ThrowsExactly<ArgumentException>(() => LoggingConfiguration.Configure(new LoggerConfiguration(), Configuration(("DiagnosticRetainedFiles", "0"))));
        Assert.IsFalse(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        string blocked = Path.Combine(directory, "blocked");
        File.WriteAllText(blocked, "A file cannot be a log directory");
        Assert.ThrowsExactly<IOException>(() => LoggingConfiguration.Configure(new LoggerConfiguration(), Configuration(("Directory", blocked))));
    }
}
