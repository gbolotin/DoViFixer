using System.Text;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Application.Dependencies;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.TemporaryStorage;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class DeepInspectionTests
{
    private static MemoryStream Rpu(string frames) => new(Encoding.UTF8.GetBytes("[" + frames + "]"));
    private const string Frame = "{\"el_type\":\"FEL\",\"Level1\":{\"max_pq\":3079}}";

    [TestMethod]
    public async Task ComparesMatchingFramesInsteadOfGlobalMaxima()
    {
        using var rpu = Rpu(Frame + "," + Frame);
        using var peaks = new StringReader("frame:0    pts:0\nlavfi.signalstats.YMAX=65535\nframe:1    pts:1\nlavfi.signalstats.YMAX=0\n");
        var evidence = await DeepInspectionParser.CompareAsync(rpu, peaks, default);
        Assert.AreEqual(2L, evidence.Brightness!.ComparedFrames);
        Assert.AreEqual(1L, evidence.Brightness.ExpandedFrames);
        Assert.AreEqual(1L, evidence.Brightness.MaximumDeltaFrame);
        Assert.AreEqual(10000, evidence.Brightness.BaseLayerPeakNits, 0.001);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("frame:1    pts:0\nlavfi.signalstats.YMAX=0\n")]
    [DataRow("frame:0    pts:0\nlavfi.signalstats.YMAX=65536\n")]
    [DataRow("frame:0    pts:0\nlavfi.signalstats.YMAX=NaN\n")]
    [DataRow("frame:0    pts:0\nlavfi.signalstats.YMAX=0\nframe:1    pts:1\nlavfi.signalstats.YMAX=0\n")]
    public async Task RejectsIncompleteOrMisalignedMeasurements(string text)
    {
        using var rpu = Rpu(Frame);
        using var peaks = new StringReader(text);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DeepInspectionParser.CompareAsync(rpu, peaks, default));
    }

    [TestMethod]
    public async Task MissingL1DoesNotReusePreviousFramePeak()
    {
        using var rpu = Rpu(Frame + ",{\"el_type\":\"FEL\"}");
        using var peaks = new StringReader("frame:0    pts:0\nlavfi.signalstats.YMAX=0\nframe:1    pts:1\nlavfi.signalstats.YMAX=0\n");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DeepInspectionParser.CompareAsync(rpu, peaks, default));
    }

    [TestMethod]
    public async Task CancellationIsPreserved()
    {
        using var rpu = Rpu(Frame);
        using var peaks = new StringReader("");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => DeepInspectionParser.CompareAsync(rpu, peaks, cancel.Token));
    }

    [TestMethod]
    [DataRow("tv", "limited")]
    [DataRow("pc", "full")]
    public void RequiresHdr10AndKnownRange(string range, string expected)
    {
        string json = "{\"streams\":[{\"color_transfer\":\"smpte2084\",\"color_primaries\":\"bt2020\",\"color_space\":\"bt2020nc\",\"color_range\":\"" + range + "\"}]}";
        Assert.AreEqual(expected, DeepInspectionParser.ReadHdr10Range(json));
        Assert.ThrowsExactly<InvalidDataException>(() => DeepInspectionParser.ReadHdr10Range(json.Replace("smpte2084", "bt709")));
        Assert.ThrowsExactly<InvalidDataException>(() => DeepInspectionParser.ReadHdr10Range(json.Replace(range, "unknown")));
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow("1", "0", "0", 2627d)]
    [DataRow("0", "1", "0", 6780d)]
    [DataRow("0", "0", "1", 593d)]
    public async Task NativeFilterMeasuresSaturatedFullRangeColors(string red, string green, string blue, double expectedNits)
    {
        await MeasureNativeAsync($"nullsrc=s=16x16:r=1:d=1,format=gbrpf32le,geq=r={red}:g={green}:b={blue}," +
            "zscale=pin=bt2020:tin=smpte2084:min=gbr:rin=full:p=bt2020:t=smpte2084:m=bt2020nc:r=full,format=yuv444p16le",
            "full", expectedNits);
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(64, 0d)]
    [DataRow(723, 1004d)]
    [DataRow(940, 10000d)]
    public async Task NativeFilterMeasuresKnownPqGrayFrames(int luma, double expectedNits)
    {
        await MeasureNativeAsync($"nullsrc=s=16x16:r=1:d=1,format=yuv420p10le,geq=lum={luma}:cb=512:cr=512", "limited", expectedNits);
    }

    private static async Task MeasureNativeAsync(string source, string range, double expectedNits)
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_FFMPEG");
        if (ffmpeg is null)
        {
            Assert.Inconclusive("Set DOVIFIXER_TEST_FFMPEG to run synthetic brightness calibration.");
        }
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-deep-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
            await runner.RunAsync(new(ffmpeg, new[] { "-nostdin", "-v", "error", "-f", "lavfi", "-i",
                source, "-vf", DeepInspectionParser.Filter(range), "-fps_mode", "passthrough", "-f", "null", "-" }, directory), default);
            using var rpu = Rpu(Frame);
            using var peaks = File.OpenText(Path.Combine(directory, "brightness.txt"));
            var result = await DeepInspectionParser.CompareAsync(rpu, peaks, default);
            Assert.AreEqual(expectedNits, result.Brightness!.BaseLayerPeakNits, 2d);
            Assert.AreEqual(1L, result.Brightness.ComparedFrames);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "brightness.txt"));
            Directory.Delete(directory);
        }
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public async Task NativeDeepInspectionMeasuresEveryFixtureFrame()
    {
        var tools = new ToolCatalog();
        foreach (var tool in new[] { NativeTool.FFmpeg, NativeTool.FFprobe, NativeTool.DoviTool, NativeTool.MkvMerge, NativeTool.MkvExtract })
        {
            string? path = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_" + tool.ToString().ToUpperInvariant());
            if (path is null)
            {
                Assert.Inconclusive("Set native test tool paths to run fixture deep inspection.");
            }
            tools.Refresh([new(tool, DependencyState.Ready, path, "test", "test")]);
        }
        var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);
        var files = new FileOperations();
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(64 * 1024 * 1024, null, default);
        string source = workspace.File("deep-fixture.mkv");
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "regular_start_code_4_muxed_el.hevc");
        await runner.RunAsync(new(tools.GetPath(NativeTool.MkvMerge), new[] { "-o", source, fixture }, AllowWarnings: true), default);
        var media = new MediaInfo(files.Identify(source), DolbyVisionProfile.Profile7, "HEVC", 0, 0, 0,
            null, null, null, 0, null, [], 0, 0, "", "{}");
        var probe = new MediaProbe(tools, runner, files, NullLogger<MediaProbe>.Instance);
        var evidence = await probe.AnalyzeAsync(media, AnalysisMethod.DeepInspection, workspace, default);
        Assert.IsNull(evidence.Error, evidence.Error);
        Assert.IsNotNull(evidence.Brightness);
        Assert.IsTrue(evidence.Brightness.ComparedFrames > 0);
        Assert.AreEqual(evidence.Frames, evidence.Brightness.ComparedFrames);
    }
}
