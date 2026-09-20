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
}
