using System.Text.Json;
using DoViFixer.Application.Reports;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Diagnostics;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.Reports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class ReportTests
{
    private static ReportWriter Writer() => new(new OutputPublisher(NullLogger<OutputPublisher>.Instance, new WindowsFileLockInspector()));

    [TestMethod]
    public async Task CsvQuotesTextAndPreventsSpreadsheetFormulas()
    {
        string directory = Path.Combine(Path.GetTempPath(), "report-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string destination = Path.Combine(directory, "report.csv");
            var report = new ReportService(TimeProvider.System, Writer());
            var snapshot = report.CreateSnapshot([new ReportInput("=SUM(1,2)\"\nфильм.mkv", "Failed", Error: "+bad, error\nline")]);
            await report.ExportAsync(destination, snapshot, default);
            string csv = await File.ReadAllTextAsync(destination);
            StringAssert.Contains(csv, "\"'=SUM(1,2)\"\"\nфильм.mkv\"");
            StringAssert.Contains(csv, "\"'+bad, error\nline\"");
            await Assert.ThrowsExactlyAsync<DoViFixer.Application.Diagnostics.OperationFailureException>(() => report.ExportAsync(destination, snapshot, default));
            Assert.AreEqual(csv, await File.ReadAllTextAsync(destination));
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => report.ExportAsync(Path.Combine(directory, "unknown.bin"), snapshot, default));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task JsonKeepsMissingMeasurementsAndDistinctEvidenceMethods()
    {
        string directory = Path.Combine(Path.GetTempPath(), "report-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new ReportService(TimeProvider.System, Writer());
            ReportInput Input(string path, AnalysisMethod method) => new(path, "Scanned", new MediaAnalysis(
                new MediaInfo(new FileIdentity(path, 12, DateTime.UtcNow), DolbyVisionProfile.Profile7, "HEVC", 0, 3840, 2160, null, null, null, 0, null, [], 0, 0, null, ""),
                new RpuEvidence(method, EnhancementLayer.Mel, 4, null, 1, 1), AnalysisVerdict.Mel, "MEL"));
            var snapshot = service.CreateSnapshot([Input("sample.mkv", AnalysisMethod.SampledRpu), Input("full.mkv", AnalysisMethod.FullRpu), Input("deep.mkv", AnalysisMethod.DeepInspection), new ReportInput("unscanned.mkv", "NotScanned")]);
            string destination = Path.Combine(directory, "report.json");
            await service.ExportAsync(destination, snapshot, default);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(destination));
            Assert.AreEqual(1, json.RootElement.GetProperty("SchemaVersion").GetInt32());
            var entries = json.RootElement.GetProperty("Entries");
            Assert.AreEqual("SampledRpu", entries[0].GetProperty("Method").GetString());
            Assert.AreEqual("FullRpu", entries[1].GetProperty("Method").GetString());
            Assert.AreEqual("DeepInspection", entries[2].GetProperty("Method").GetString());
            Assert.AreEqual(JsonValueKind.Null, entries[0].GetProperty("RpuPeakNits").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, entries[3].GetProperty("Verdict").ValueKind);
            Assert.AreEqual(false, entries[2].GetProperty("EvidenceComplete").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CancelledExportLeavesNoDestinationOrStagingDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "report-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new ReportService(TimeProvider.System, Writer());
            var snapshot = service.CreateSnapshot([new ReportInput("movie.mkv", "Skipped")]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ExportAsync(Path.Combine(directory, "report.csv"), snapshot, cancellation.Token));
            Assert.AreEqual(0, Directory.GetFileSystemEntries(directory).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
