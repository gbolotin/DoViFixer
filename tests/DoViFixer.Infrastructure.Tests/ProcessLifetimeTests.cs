using System.ComponentModel;
using System.Diagnostics;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
[TestCategory("ProcessIntegration")]
public sealed class ProcessLifetimeTests
{
    private readonly ProcessRunner runner = new(NullLogger<ProcessRunner>.Instance);
    private readonly List<Process> owned = [];
    private string directory = null!;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "DoViFixer-process-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        foreach (var process in owned)
        {
            await ProcessJob.StopAsync(process);
            process.Dispose();
        }

        Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    [DataRow("host-single")]
    [DataRow("host-pipeline")]
    public async Task AbruptHostTerminationKillsAssignedProcessesAndDescendants(string mode)
    {
        // The parent job also proves that the runner can create nested jobs.
        using var parentJob = new ProcessJob();
        var host = new Process { StartInfo = ProcessTestHost.StartInfo(mode, directory) };
        await parentJob.StartAsync(host);
        owned.Add(host);
        string[] names = mode == "host-single" ? ["single", "single-child"] : ["producer", "producer-child", "consumer", "consumer-child"];
        var children = new List<Process>();
        foreach (string name in names)
        {
            children.Add(await ReadProcessAsync(name));
        }

        // Do not kill the tree or close parentJob: only the host's job handles should kill descendants.
        host.Kill();
        await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        foreach (var child in children)
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [TestMethod]
    public async Task CancellationKillsChildrenAndDoesNotStopAnotherInvocation()
    {
        using var cancelled = new CancellationTokenSource();
        using var survivor = new CancellationTokenSource();
        Task first = runner.RunAsync(ProcessTestHost.Request("tree", directory, "first"), cancelled.Token);
        Task second = runner.RunAsync(ProcessTestHost.Request("tree", directory, "second"), survivor.Token);
        ProcessTestHost.Release(directory);
        var firstProcess = await ReadProcessAsync("first");
        var firstChild = await ReadProcessAsync("first-child");
        var secondProcess = await ReadProcessAsync("second");
        var secondChild = await ReadProcessAsync("second-child");
        try
        {
            cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(15)));
            await firstProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await firstChild.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await runner.RunAsync(ProcessTestHost.Request("echo", "completed separately", "0"), default);
            Assert.IsFalse(secondProcess.HasExited);
            Assert.IsFalse(secondChild.HasExited);
        }
        finally
        {
            cancelled.Cancel();
            survivor.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(15)));
        }
    }

    [TestMethod]
    public async Task PipelineCancellationKillsBothTrees()
    {
        using var cancellation = new CancellationTokenSource();
        Task operation = runner.PipeAsync(ProcessTestHost.Request("tree", directory, "producer"), ProcessTestHost.Request("tree", directory, "consumer"), cancellation.Token);
        ProcessTestHost.Release(directory);
        try
        {
            foreach (string name in new[] { "producer", "producer-child", "consumer", "consumer-child" })
            {
                await ReadProcessAsync(name);
            }
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(15)));
        }

        foreach (var process in owned)
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [TestMethod]
    public async Task PreCancelledRequestsDoNotStartProcesses()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = ProcessTestHost.Request("leaf", directory, "unexpected");
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(request, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.PipeAsync(request, request, cancellation.Token));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "unexpected.pid")));
    }

    [TestMethod]
    public async Task TimeoutIsDistinctFromCancellationAndKillsDescendants()
    {
        var request = ProcessTestHost.Request("tree", directory, "timeout") with { Timeout = TimeSpan.FromSeconds(5) };
        Task operation = runner.RunAsync(request, default);
        ProcessTestHost.Release(directory);
        var process = await ReadProcessAsync("timeout");
        var child = await ReadProcessAsync("timeout-child");
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => operation.WaitAsync(TimeSpan.FromSeconds(15)));
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public async Task NormalCompletionClosesJobAndTerminatesRemainingDescendants()
    {
        Task operation = runner.RunAsync(ProcessTestHost.Request("orphan", directory, "orphan"), default);
        ProcessTestHost.Release(directory);
        await operation.WaitAsync(TimeSpan.FromSeconds(15));
        int childId = int.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "orphan-child.pid")));
        try
        {
            using var child = Process.GetProcessById(childId);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // The process has already exited and its kernel object has been released.
        }
    }

    [TestMethod]
    public async Task AssignmentFailureStopsTheStartedProcess()
    {
        using var job = new ProcessJob();
        job.Dispose();
        using var process = new Process { StartInfo = ProcessTestHost.StartInfo("leaf", directory, "rejected") };
        var error = await Assert.ThrowsExactlyAsync<IOException>(() => job.StartAsync(process));
        Assert.IsInstanceOfType<ObjectDisposedException>(error.InnerException);
        Assert.IsTrue(process.HasExited);
    }

    [TestMethod]
    public async Task ProducerStartupFailureCleansUpConsumer()
    {
        // The consumer never gets released; even a producer startup failure must terminate it.
        var logger = new StartedProcessLogger(owned);
        var observedRunner = new ProcessRunner(logger);
        var missing = new ProcessRequest(Path.Combine(directory, "missing.exe"), []);
        await Assert.ThrowsExactlyAsync<Win32Exception>(() => observedRunner.PipeAsync(missing, ProcessTestHost.Request("tree", directory, "consumer"), default));
        Assert.HasCount(1, owned);
        Assert.IsTrue(owned[0].HasExited, "The consumer must exit before the startup failure is returned.");
        // A subsequent invocation remains usable after the failed pipeline.
        var result = await runner.RunAsync(ProcessTestHost.Request("echo", "after failure", "0"), default);
        StringAssert.Contains(result.Output, "after failure");
    }

    [TestMethod]
    public async Task ConsumerStartupFailureDoesNotStartProducer()
    {
        var missing = new ProcessRequest(Path.Combine(directory, "missing.exe"), []);
        await Assert.ThrowsExactlyAsync<Win32Exception>(() => runner.PipeAsync(ProcessTestHost.Request("tree", directory, "producer"), missing, default));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "producer.pid")));
    }

    [TestMethod]
    public async Task ExitCodesAndArgumentsRemainIntact()
    {
        const string argument = "Unicode שלום (a) \"quoted\" trailing\\";
        var result = await runner.RunAsync(ProcessTestHost.Request("echo", argument, "1") with { AllowWarnings = true }, default);
        Assert.AreEqual(argument + Environment.NewLine, result.Output);
        StringAssert.Contains(result.Error, "diagnostic");
        Assert.AreEqual(1, result.ExitCode);
        await Assert.ThrowsExactlyAsync<IOException>(() => runner.RunAsync(ProcessTestHost.Request("echo", "failure", "7"), default));
    }

    private async Task<Process> ReadProcessAsync(string name)
    {
        string path = Path.Combine(directory, name + ".pid");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path, timeout.Token), out int id))
            {
                var process = Process.GetProcessById(id);
                owned.Add(process);
                return process;
            }

            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class StartedProcessLogger(List<Process> processes) : ILogger<ProcessRunner>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                foreach (var property in properties)
                {
                    if (property is { Key: "NativeProcessId", Value: int id })
                    {
                        processes.Add(Process.GetProcessById(id));
                    }
                }
            }
        }
    }
}
