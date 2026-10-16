using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Diagnostics;
using DoViFixer.Application.Settings;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.Diagnostics;
using DoViFixer.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class DiagnosticsTests
{
    private string directory = null!;
    private static OutputPublisher Publisher(IFileLockInspector? locks = null) => new(NullLogger<OutputPublisher>.Instance, locks ?? new WindowsFileLockInspector());

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "DoViFixer-diagnostics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        // Windows can briefly retain file handles after the fixture process exits.
        for (int attempt = 0;; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                await Task.Delay(100);
            }
        }
    }

    [TestMethod]
    [DataRow(32, FailureCategory.FileInUse)]
    [DataRow(33, FailureCategory.FileInUse)]
    [DataRow(5, FailureCategory.AccessDenied)]
    [DataRow(112, FailureCategory.DiskFull)]
    [DataRow(39, FailureCategory.DiskFull)]
    [DataRow(2, FailureCategory.MissingFile)]
    [DataRow(183, FailureCategory.DestinationExists)]
    [DataRow(12345, FailureCategory.Unknown)]
    public void ClassifiesNativeCodesWithoutDiscardingEvidence(int code, FailureCategory category)
    {
        var exception = new Win32Exception(code, "Original error text");
        Guid invocation = Guid.NewGuid();
        var diagnosis = FailureClassifier.Describe(exception, "Publish", "movie.mkv", invocation);
        Assert.AreEqual(category, diagnosis.Category);
        Assert.AreEqual(invocation, diagnosis.ToolInvocationId);
        Assert.AreEqual("Original error text", diagnosis.TechnicalDetail);
        Assert.AreEqual("movie.mkv", diagnosis.Path);
        var wrapped = new OperationFailureException(diagnosis, exception);
        Assert.AreSame(exception, wrapped.InnerException);
        StringAssert.Contains(wrapped.Message, diagnosis.Guidance);
        Assert.AreEqual(category, FailureClassifier.Describe(new IOException("Localized message", unchecked((int)0x80070000) | code), "Publish").Category);
    }

    [TestMethod]
    public void ClassifiesTimeoutAndMissingToolButDoesNotTreatExitCodeAsWindowsError()
    {
        Assert.AreEqual(FailureCategory.Timeout, FailureClassifier.Describe(new TimeoutException(), "Inspect").Category);
        Assert.AreEqual(FailureCategory.MissingTool, FailureClassifier.Describe(new Win32Exception(2), "Start", startingTool: true).Category);
        Assert.AreEqual(FailureCategory.NativeToolFailed, FailureClassifier.ToolExit("tool", 5, "Unknown reason", Guid.NewGuid()).Category);
        Assert.AreEqual(FailureCategory.DiskFull, FailureClassifier.ToolExit("tool", 1, "No space left on device", Guid.NewGuid()).Category);
        Assert.AreEqual(FailureCategory.Unknown, FailureClassifier.Describe(new IOException("Permission?"), "Read").Category);
        StringAssert.Contains(new FailureDiagnosis(FailureCategory.FileInUse, "Publish", "file", null, "Locked")
            { FileLock = new([], "Unavailable") }.Message, "No lock holder could be identified");
    }

    [TestMethod]
    [TestCategory("ProcessIntegration")]
    public async Task LockedPublicationNamesTheHolderAndLeavesOriginalIntact()
    {
        string destination = Path.Combine(directory, "movie.mkv");
        string ready = Path.Combine(directory, "holder.ready");
        await File.WriteAllTextAsync(destination, "original");
        using var holder = Process.Start(ProcessTestHost.StartInfo("lock", destination, ready))!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(ready))
            {
                await Task.Delay(20, timeout.Token);
            }
            await using var staged = Publisher().Stage(destination, replaceExisting: true);
            await File.WriteAllTextAsync(staged.Path, "replacement");
            var exception = await Assert.ThrowsExactlyAsync<OperationFailureException>(() => staged.PublishAsync(timeout.Token));
            Assert.IsTrue(exception.Diagnosis.Category is FailureCategory.FileInUse or FailureCategory.AccessDenied);
            Assert.IsTrue(exception.Diagnosis.FileLock!.Holders.Any(h => h.ProcessId == holder.Id));
            StringAssert.Contains(exception.Message, $"PID {holder.Id}");
            Assert.IsFalse(holder.HasExited);
            Assert.AreEqual("original", await File.ReadAllTextAsync(destination));
        }
        finally
        {
            if (!holder.HasExited)
            {
                holder.Kill();
            }
            await holder.WaitForExitAsync();
        }
        Assert.IsEmpty(Directory.GetDirectories(directory, ".dovifixer-*"));
    }

    [TestMethod]
    public async Task ZipIncludesOnlyAllowedFilesAndReadsActiveLogsWithoutChangingSources()
    {
        const string content = "{\"@m\":\"Unicode שלום\"}\n";
        foreach (string name in new[] { "app-diagnostic-20261016.jsonl", "history-20261016.jsonl", "mcp-audit-20261016.jsonl" })
        {
            await File.WriteAllTextAsync(Path.Combine(directory, name), content);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "must not be copied raw");
        await File.WriteAllTextAsync(Path.Combine(directory, "diagnostic-secret.jsonl"), "not an application log");
        using var active = new FileStream(Path.Combine(directory, "app-diagnostic-20261016.jsonl"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var writer = new DiagnosticBundleWriter(new(directory), Publisher());
        string destination = Path.Combine(directory, "support.zip");
        var result = await writer.WriteAsync(destination, Snapshot(), default);
        Assert.AreEqual(3, result.IncludedLogs);
        Assert.AreEqual(0, result.UnavailableEntries);
        using var zip = ZipFile.OpenRead(destination);
        Assert.HasCount(5, zip.Entries);
        Assert.IsNull(zip.GetEntry("settings.json"));
        Assert.IsNull(zip.GetEntry("logs/diagnostic-secret.jsonl"));
        Assert.AreEqual(content, await ReadAsync(zip, "logs/app-diagnostic-20261016.jsonl"));
        active.Dispose();
        Assert.AreEqual(content, await File.ReadAllTextAsync(Path.Combine(directory, "app-diagnostic-20261016.jsonl")));
        Assert.AreEqual("must not be copied raw", await File.ReadAllTextAsync(Path.Combine(directory, "settings.json")));
        using var snapshot = JsonDocument.Parse(await ReadAsync(zip, "snapshot.json"));
        Assert.AreEqual(1, snapshot.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.IsFalse(snapshot.RootElement.TryGetProperty("Environment", out _));
    }

    [TestMethod]
    public async Task CollectionBoundsTailsAndListsUnreadableFiles()
    {
        string row = "{\"@m\":\"" + new string('x', 1000) + "\"}\n";
        string large = string.Concat(Enumerable.Repeat(row, 2500));
        for (int day = 10; day <= 14; day++)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, $"diagnostic-202610{day}.jsonl"), large);
        }
        string locked = Path.Combine(directory, "audit-20261016.jsonl");
        await File.WriteAllTextAsync(locked, row);
        using var exclusive = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        string destination = Path.Combine(directory, "bounded.zip");
        var result = await new DiagnosticBundleWriter(new(directory), Publisher()).WriteAsync(destination, Snapshot(), default);
        Assert.AreEqual(3, result.IncludedLogs);
        Assert.AreEqual(3, result.TruncatedLogs);
        Assert.AreEqual(2, result.UnavailableEntries); // Missing history plus unreadable audit.
        using var zip = ZipFile.OpenRead(destination);
        Assert.IsNull(zip.GetEntry("logs/diagnostic-20261011.jsonl"));
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("logs/")))
        {
            Assert.IsTrue(entry.Length <= DiagnosticBundleWriter.MaxLogBytes);
            using var reader = new StreamReader(entry.Open());
            while (await reader.ReadLineAsync() is { } line)
            {
                using var parsed = JsonDocument.Parse(line);
            }
        }
        StringAssert.Contains(await ReadAsync(zip, "manifest.json"), "audit-20261016.jsonl");
    }

    [TestMethod]
    public async Task CollisionAndCancellationDoNotLeavePartialsOrOverwriteFiles()
    {
        string destination = Path.Combine(directory, "existing.zip");
        await File.WriteAllTextAsync(destination, "existing");
        var writer = new DiagnosticBundleWriter(new(directory), Publisher());
        var error = await Assert.ThrowsExactlyAsync<OperationFailureException>(() => writer.WriteAsync(destination, Snapshot(), default));
        Assert.AreEqual(FailureCategory.DestinationExists, error.Diagnosis.Category);
        Assert.AreEqual("existing", await File.ReadAllTextAsync(destination));

        using var cancellation = new CancellationTokenSource();
        var cancelling = new DiagnosticBundleWriter(new(directory), new CancelOnStage(Publisher(), cancellation));
        string cancelledPath = Path.Combine(directory, "cancelled.zip");
        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelling.WriteAsync(cancelledPath, Snapshot(), cancellation.Token));
        Assert.IsFalse(File.Exists(cancelledPath));
        Assert.IsEmpty(Directory.GetDirectories(directory, ".dovifixer-*"));
    }

    [TestMethod]
    public void ToolSnapshotKeepsKnownVersionsWithoutStartingTools()
    {
        var catalog = new ToolCatalog();
        catalog.Refresh([new(NativeTool.FFmpeg, DependencyState.Ready, "ffmpeg.exe", "8.0", "ok")]);
        Assert.AreEqual("8.0", catalog.Snapshot().Single().Version);
        catalog.Refresh([new(NativeTool.FFmpeg, DependencyState.Missing, null, null, "missing")]);
        Assert.AreEqual(DependencyState.Missing, catalog.Snapshot().Single().State);
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.GetPath(NativeTool.FFmpeg));
    }

    private static DiagnosticSnapshot Snapshot() => new(DateTimeOffset.UtcNow, "Fixture", "1.0", null, [], null);
    private static async Task<string> ReadAsync(ZipArchive zip, string entry)
    {
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class CancelOnStage(IOutputPublisher publisher, CancellationTokenSource cancellation) : IOutputPublisher
    {
        public IStagedOutput Stage(string destination, bool replaceExisting = false)
        {
            var staged = publisher.Stage(destination, replaceExisting);
            cancellation.Cancel();
            return staged;
        }
    }
}
