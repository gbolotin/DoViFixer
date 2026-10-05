using System.Text;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using DoViFixer.Infrastructure.TemporaryStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;
[TestClass]
public sealed class ProgressTests
{
    [TestMethod]
    [DataRow(AnalysisMethod.SampledRpu)]
    [DataRow(AnalysisMethod.FullRpu)]
    [DataRow(AnalysisMethod.DeepInspection)]
    public async Task InspectionReportsSampleAttemptsOrExtractionProgressEvenWhenToolsFail(AnalysisMethod method)
    {
        var tools = new ToolCatalog();
        tools.Refresh([
            new(NativeTool.FFmpeg, DependencyState.Ready, "ffmpeg", "test", "fixture"),
            new(NativeTool.MkvExtract, DependencyState.Ready, "mkvextract", "test", "fixture")
        ]);
        var files = new FileOperations();
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(1024, null, default);
        var media = new MediaInfo(new("movie.mkv", 1024, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 2400, 100, 0, 1000, [], 0, 0, null, "{}");
        var probe = new MediaProbe(tools, new FailingRunner(), files, NullLogger<MediaProbe>.Instance);
        var sink = new RecordingProgress();
        var result = await probe.AnalyzeAsync(media, method, workspace, default, sink);
        Assert.IsTrue(sink.Values.All(value => value.Item == media.Source.Path));
        if (method == AnalysisMethod.SampledRpu)
        {
            CollectionAssert.AreEqual(Enumerable.Range(0, 11).Select(value => (double?)(value * 10)).ToArray(), sink.Values.Select(value => value.Percent).ToArray());
            Assert.AreEqual(0, result.SuccessfulSamples, "Progress measures attempts, not successful evidence.");
            Assert.IsNotNull(result.SampleDiagnostics);
        }
        else
        {
            CollectionAssert.AreEqual(new double?[] { 0, 45 }, sink.Values.Select(value => value.Percent).ToArray());
            Assert.IsNotNull(result.Error);
        }
    }

    [TestMethod]
    public async Task ParsesProgressAcrossReadBoundariesAndPreservesCapturedOutput()
    {
        string text = new string ('x', 8190) + "\r#GUI#progress 25%\r\n#GUI#progress 20%\n#GUI#progress 101%\n#GUI#progress 100%";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new StreamReader(stream);
        var sink = new RecordingProgress();
        var id = Guid.NewGuid();
        var progress = new MkvProgress(sink, id, "Extracting video", "movie.mkv");
        Assert.AreEqual(text, await ProcessRunner.DrainAsync(reader, progress.Report));
        CollectionAssert.AreEqual(new double? []
        {
            25, 99
        }, sink.Values.Select(v => v.Percent).ToArray());
        Assert.IsTrue(sink.Values.All(v => v.OperationId == id && v.Item == "movie.mkv"));
    }

    [TestMethod]
    [DataRow("frame=1234", true, 1234L)]
    [DataRow("frame=0", true, 0L)]
    [DataRow("frame=-1", false, 0L)]
    [DataRow("fps=23.97", false, 0L)]
    [DataRow("progress=end", false, 0L)]
    public void ParsesFFmpegProgressFrameCount(string line, bool parsed, long frame)
    {
        Assert.AreEqual(parsed, FFmpegProgress.TryParseFrame(line, out long value));
        Assert.AreEqual(frame, value);
    }

    [TestMethod]
    public void PhasedProgressWeighsPhasesAndLeavesCompletionToTheCaller()
    {
        var sink = new RecordingProgress();
        var id = Guid.NewGuid();
        var phases = new PhasedProgress(sink, id, "Backing up enhancement layer", "movie.mkv", 30, 30, 30, 10);
        phases.Report(0, 0);
        phases.Report(0, 0.5);
        phases.Report(0, 0.4);
        phases.Report(1, 0.5);
        phases.Report(2, 2);
        phases.Report(3, 1);
        CollectionAssert.AreEqual(new double?[] { 0, 15, 45, 90, 99 }, sink.Values.Select(v => v.Percent).ToArray());
        Assert.IsTrue(sink.Values.All(v => v.OperationId == id && v.Stage == "Backing up enhancement layer" && v.Item == "movie.mkv"));
    }

    [TestMethod]
    public async Task BackupExtractionReportsRealProgressThroughEveryTool()
    {
        var tools = new ToolCatalog();
        tools.Refresh([
            new(NativeTool.MkvExtract, DependencyState.Ready, "mkvextract", "test", "fixture"),
            new(NativeTool.DoviTool, DependencyState.Ready, "dovi_tool", "test", "fixture")
        ]);
        var files = new FileOperations();
        await using var workspace = await new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance).CreateAsync(1024 * 1024, null, default);
        var media = new MediaInfo(new("movie.mkv", 1024, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 2400, 100, 0, 1000, [], 0, 0, null, "{}");
        var processor = new VideoProcessor(tools, new BackupTools(), TimeProvider.System, NullLogger<VideoProcessor>.Instance);
        var sink = new RecordingProgress();
        var id = Guid.NewGuid();
        var manifest = await processor.ExtractBackupAsync(media, workspace, default, sink, id, "Extracting enhancement layer");
        double[] percents = sink.Values.Select(v => v.Percent!.Value).ToArray();
        Assert.AreEqual(0, percents[0]);
        Assert.AreEqual(99, percents[^1], "Only the service completes the stage, after the archive is written.");
        CollectionAssert.AreEqual(percents.Order().ToArray(), percents, "Progress never moves backwards.");
        Assert.AreEqual(percents.Length, percents.Distinct().Count());
        Assert.Contains(15d, percents, "mkvextract's own progress fills the first phase.");
        Assert.IsTrue(percents.Any(p => p is > 30 and < 60), "Demuxing progress is measured from the files dovi_tool writes.");
        Assert.IsTrue(sink.Values.All(v => v.OperationId == id && v.Stage == "Extracting enhancement layer" && v.Item == "movie.mkv"));
        Assert.AreEqual(BackupTools.LayerLength, manifest.EnhancementLayerLength);
    }

    private sealed class RecordingProgress : IProgress<OperationProgress>
    {
        public List<OperationProgress> Values
        {
            get;
        }
        = [];

        public void Report(OperationProgress value) => Values.Add(value);
    }

    private sealed class FailingRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            if (request.Executable == "mkvextract")
            {
                Assert.Contains("--gui-mode", request.Arguments);
                request.OutputLine?.Invoke("#GUI#progress 45%");
            }

            throw new IOException("Fixture tool failure");
        }

        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // Writes the files mkvextract and dovi_tool would; demuxing pauses halfway so its output can be measured while it runs.
    private sealed class BackupTools : IProcessRunner
    {
        public const int LayerLength = 4096;

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            var arguments = request.Arguments.ToList();
            if (request.Executable == "mkvextract")
            {
                request.OutputLine?.Invoke("#GUI#progress 50%");
                await File.WriteAllBytesAsync(arguments[^1][(arguments[^1].IndexOf(':') + 1)..], new byte[2 * LayerLength], cancellationToken);
                request.OutputLine?.Invoke("#GUI#progress 100%");
            }
            else if (arguments[0] == "demux")
            {
                string bl = arguments[arguments.IndexOf("-b") + 1];
                string el = arguments[arguments.IndexOf("-e") + 1];
                await using (var stream = new FileStream(bl, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    await stream.WriteAsync(new byte[LayerLength], cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(1.2), cancellationToken);
                }

                await File.WriteAllBytesAsync(el, new byte[LayerLength], cancellationToken);
            }
            else
            {
                File.Copy(arguments[1], arguments[^1]);
            }

            return new(0, "", "");
        }

        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
