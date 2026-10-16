using System.Collections.Immutable;
using System.Text.Json;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Diagnostics;
using DoViFixer.Application.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;

[TestClass]
public sealed class DiagnosticsServiceTests
{
    [TestMethod]
    public async Task CapturesExplicitSettingsAndKnownVersionsWithoutProbing()
    {
        var runtime = new Runtime();
        var service = new DiagnosticsService(runtime, runtime, runtime, TimeProvider.System);
        var result = await service.ExportAsync("report.zip", default);
        Assert.AreEqual("report.zip", result.Path);
        Assert.AreEqual("C:\\output", runtime.Snapshot!.Settings!.OutputDirectory);
        Assert.AreEqual("ffmpeg.exe", runtime.Snapshot.Settings.ToolPaths[NativeTool.FFmpeg]);
        Assert.AreEqual("8.0", runtime.Snapshot.KnownTools.Single().Version);
        Assert.IsNull(runtime.Snapshot.SettingsError);
        Assert.ThrowsExactly<NotSupportedException>(() => runtime.GetPath(NativeTool.FFmpeg));
    }

    [TestMethod]
    public async Task BrokenSettingsAreReportedWithoutPreventingExport()
    {
        var runtime = new Runtime { ReadFailure = new JsonException("Broken settings") };
        await new DiagnosticsService(runtime, runtime, runtime, TimeProvider.System).ExportAsync("report.zip", default);
        Assert.IsNull(runtime.Snapshot!.Settings);
        Assert.AreEqual("Broken settings", runtime.Snapshot.SettingsError);
    }

    [TestMethod]
    public async Task InvalidExtensionAndCancellationNeverCallWriter()
    {
        var runtime = new Runtime();
        var service = new DiagnosticsService(runtime, runtime, runtime, TimeProvider.System);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.ExportAsync("report.txt", default));
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExportAsync("report.zip", new CancellationToken(true)));
        Assert.IsNull(runtime.Snapshot);
    }

    private sealed class Runtime : ISettingsStore, IToolCatalog, IDiagnosticBundleWriter
    {
        public Exception? ReadFailure { get; init; }
        public DiagnosticSnapshot? Snapshot { get; private set; }
        public Task<UserSettings> ReadAsync(CancellationToken cancellationToken) => ReadFailure is not null
            ? Task.FromException<UserSettings>(ReadFailure)
            : Task.FromResult(new UserSettings { OutputDirectory = @"C:\output", ToolPaths = ImmutableDictionary<NativeTool, string>.Empty.Add(NativeTool.FFmpeg, "ffmpeg.exe") });
        public Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken cancellationToken) => throw new NotSupportedException();
        public string GetPath(NativeTool tool) => throw new NotSupportedException();
        public void Refresh(IEnumerable<DependencyStatus> statuses) => throw new NotSupportedException();
        IReadOnlyList<DependencyStatus> IToolCatalog.Snapshot() => [new(NativeTool.FFmpeg, DependencyState.Ready, "ffmpeg.exe", "8.0", "ok")];
        public Task<DiagnosticExportResult> WriteAsync(string destination, DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
        {
            Snapshot = snapshot;
            return Task.FromResult(new DiagnosticExportResult(destination, 0, 0, 0));
        }
    }
}
