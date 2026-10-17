using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class StreamedInspectionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FullInspectionAppliesPercentileInStreamingAndDiskPaths(bool failPipe)
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-percentile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new DoViFixer.Infrastructure.Configuration.SettingsStore(new(directory), NullLogger<DoViFixer.Infrastructure.Configuration.SettingsStore>.Instance);
            await settings.UpdateAsync(value => value with { InspectionPeakPercentile = 50 }, default);
            var runner = new InspectionRunner(directory)
            {
                FailPipe = failPipe, Packets = 3,
                ExportJson = "[{\"el_type\":\"FEL\",\"Level1\":{\"max_pq\":2000}},{\"el_type\":\"FEL\",\"Level1\":{\"max_pq\":2000}},{\"el_type\":\"FEL\",\"Level1\":{\"max_pq\":4000}}]"
            };
            var probe = new MediaProbe(Tools(), runner, new FileOperations(), NullLogger<MediaProbe>.Instance, settings);
            var evidence = await probe.AnalyzeAsync(Media(), AnalysisMethod.FullRpu, new Workspace(directory), default);
            Assert.IsNull(evidence.Error, evidence.Error);
            Assert.AreEqual(3, evidence.Frames);
            Assert.AreEqual(MediaClassifier.PqToNits(2000), evidence.PeakNits);
            Assert.AreEqual(50d, evidence.Options!.InspectionPeakPercentile);
            Assert.AreEqual(failPipe ? 1 : 0, runner.Extractions);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    [DataRow(false, false, 1)]
    [DataRow(true, false, 1)]
    [DataRow(false, true, 1)]
    [DataRow(false, false, 2)]
    public async Task FullInspectionStreamsOrRetriesDiskWhenNeeded(bool failPipe, bool invalidStream, int packets)
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new InspectionRunner(directory) { FailPipe = failPipe, InvalidStream = invalidStream, Packets = packets };
            var probe = new MediaProbe(Tools(), runner, new FileOperations(), NullLogger<MediaProbe>.Instance);
            var evidence = await probe.AnalyzeAsync(Media(), AnalysisMethod.FullRpu, new Workspace(directory), default);
            if (packets == 1)
            {
                Assert.IsNull(evidence.Error, evidence.Error);
                Assert.AreEqual(1, evidence.Frames);
            }
            else
            {
                StringAssert.Contains(evidence.Error!, "one access unit per uniquely timestamped video packet");
            }
            Assert.AreEqual(1, runner.Pipes);
            Assert.AreEqual(failPipe || invalidStream || packets == 2 ? 1 : 0, runner.Extractions);
            Assert.IsTrue(runner.Producer!.Arguments.Contains("0:v:0"));
            Assert.IsFalse(runner.Producer.Arguments.Contains("hevc_mp4toannexb"));
            Assert.AreEqual("-", runner.Consumer!.Arguments[1]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task CancellationDoesNotRetryDisk()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new InspectionRunner(directory) { CancelPipe = true };
            using var cancellation = new CancellationTokenSource();
            runner.Cancellation = cancellation;
            var probe = new MediaProbe(Tools(), runner, new FileOperations(), NullLogger<MediaProbe>.Instance);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => probe.AnalyzeAsync(Media(), AnalysisMethod.FullRpu, new Workspace(directory), cancellation.Token));
            Assert.AreEqual(0, runner.Extractions);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task InvalidExportRetriesDiskAndUsesFreshMetadata()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new InspectionRunner(directory) { CorruptFirstExport = true };
            var probe = new MediaProbe(Tools(), runner, new FileOperations(), NullLogger<MediaProbe>.Instance);
            var evidence = await probe.AnalyzeAsync(Media(), AnalysisMethod.FullRpu, new Workspace(directory), default);
            Assert.IsNull(evidence.Error, evidence.Error);
            Assert.AreEqual(1, evidence.Frames);
            Assert.AreEqual(1, runner.Extractions);
            Assert.AreEqual(2, runner.Exports);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task FailedDiskFallbackReportsFailure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new InspectionRunner(directory) { FailPipe = true, FailDisk = true };
            var probe = new MediaProbe(Tools(), runner, new FileOperations(), NullLogger<MediaProbe>.Instance);
            var evidence = await probe.AnalyzeAsync(Media(), AnalysisMethod.FullRpu, new Workspace(directory), default);
            StringAssert.Contains(evidence.Error!, "Disk extraction failed");
            Assert.AreEqual(1, runner.Extractions);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task NonzeroTrackIdStillSelectsTheOnlyVideoStream()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new InspectionRunner(directory);
            var probe = new MediaProbe(Tools(), runner, new FileOperations(), NullLogger<MediaProbe>.Instance);
            var media = Media();
            media = media with { VideoTrackId = 2, IdentificationJson = media.IdentificationJson.Replace("\"id\": 0", "\"id\": 2") };
            var evidence = await probe.AnalyzeAsync(media, AnalysisMethod.FullRpu, new Workspace(directory), default);
            Assert.IsNull(evidence.Error, evidence.Error);
            CollectionAssert.Contains(runner.Producer!.Arguments.ToArray(), "0:v:0");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task DeepInspectionUsesDiskExtraction()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new InspectionRunner(directory);
            var probe = new MediaProbe(Tools(), runner, new FileOperations(), NullLogger<MediaProbe>.Instance);
            await probe.AnalyzeAsync(Media(), AnalysisMethod.DeepInspection, new Workspace(directory), default);
            Assert.AreEqual(0, runner.Pipes);
            Assert.AreEqual(1, runner.Extractions);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static MediaInfo Media()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mkvmerge.json"));
        return new(new("movie.mkv", 1024, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 1, 24, 1, 0, null, [new(0, "video", "HEVC", "eng", "", true, false, null)], 0, 0, null, json);
    }

    private static ToolCatalog Tools()
    {
        var tools = new ToolCatalog();
        foreach (var tool in new[] { NativeTool.FFmpeg, NativeTool.FFprobe, NativeTool.DoviTool, NativeTool.MkvExtract })
        {
            tools.Refresh([new(tool, DependencyState.Ready, tool.ToString(), "test", "fixture")]);
        }

        return tools;
    }

    private sealed class Workspace(string directory) : ITemporaryWorkspace
    {
        public string DirectoryPath => directory;
        public string File(string name) => Path.Combine(directory, name);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InspectionRunner(string directory) : IProcessRunner
    {
        public string? ExportJson { get; init; }
        public bool FailPipe { get; init; }
        public bool InvalidStream { get; init; }
        public bool FailDisk { get; init; }
        public bool CorruptFirstExport { get; init; }
        public bool CancelPipe { get; init; }
        public int Packets { get; init; } = 1;
        public CancellationTokenSource? Cancellation { get; set; }
        public int Pipes { get; private set; }
        public int Extractions { get; private set; }
        public int Exports { get; private set; }
        public ProcessRequest? Producer { get; private set; }
        public ProcessRequest? Consumer { get; private set; }

        public async Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken token)
        {
            Pipes++;
            Producer = producer;
            Consumer = consumer;
            if (CancelPipe)
            {
                Cancellation!.Cancel();
                throw new OperationCanceledException(token);
            }

            if (FailPipe)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "inspection.rpu"), "partial", token);
                await File.WriteAllTextAsync(Path.Combine(directory, "rpu.json"), "partial", token);
                throw new IOException("Pipe failed");
            }

            byte[] input = InvalidStream ? [0, 0, 0, 4, 0x40, 0x01] : [0, 0, 0, 3, 0x40, 0x01, 0x0c];
            await producer.OutputRelay!(new MemoryStream(input), new MemoryStream(), token);
            await File.WriteAllBytesAsync(Path.Combine(directory, "inspection.rpu"), [1], token);
        }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
        {
            if (request.Executable == NativeTool.MkvExtract.ToString())
            {
                Extractions++;
                if (FailPipe)
                {
                    Assert.IsFalse(File.Exists(Path.Combine(directory, "inspection.rpu")));
                    Assert.IsFalse(File.Exists(Path.Combine(directory, "rpu.json")));
                }
                if (FailDisk)
                {
                    throw new IOException("Disk extraction failed");
                }

                await File.WriteAllBytesAsync(Path.Combine(directory, "inspection.hevc"), [1], token);
            }
            else if (request.Arguments.Contains("extract-rpu"))
            {
                await File.WriteAllBytesAsync(Path.Combine(directory, "inspection.rpu"), [1], token);
            }
            else if (request.Arguments.Contains("export"))
            {
                Exports++;
                if (CorruptFirstExport && Exports == 1)
                {
                    await File.WriteAllTextAsync(Path.Combine(directory, "rpu.json"), "{", token);
                }
                else
                {
                    if (ExportJson is not null)
                    {
                        await File.WriteAllTextAsync(Path.Combine(directory, "rpu.json"), ExportJson, token);
                    }
                    else
                    {
                        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mel-rpu.json"), Path.Combine(directory, "rpu.json"), true);
                    }
                }
            }

            return new(0, request.Executable == NativeTool.FFprobe.ToString() ? Packets.ToString() : "", "");
        }
    }
}
