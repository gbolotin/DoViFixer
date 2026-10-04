using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;
[TestClass]
public sealed class HevcAnnexBTests
{
    private static readonly byte[][] nalUnits = [[0x40, 0x01, 0x0c], [0x42, 0x01, 0x01, 0x00], [0x26, 0x01, 0xaf, 0x00, 0x00, 0x03], [0x7e, 0x01, 0x00, 0x00, 0x01]];

    [TestMethod]
    [DataRow(4)]
    [DataRow(2)]
    [DataRow(1)]
    public async Task CopyWritesEveryNalUnitUnchangedAfterAStartCode(int lengthSize)
    {
        using var output = new MemoryStream();
        // One byte per read puts every length prefix and NAL unit across read boundaries.
        await HevcAnnexB.CopyAsync(new TrickleStream(LengthPrefixed(lengthSize, nalUnits)), output, lengthSize, default);
        CollectionAssert.AreEqual(nalUnits.SelectMany(n => new byte[] { 0, 0, 0, 1 }.Concat(n)).ToArray(), output.ToArray());
    }

    [TestMethod]
    public async Task CopyRejectsAStreamThatEndsInsideANalUnitOrHasAnInvalidLength()
    {
        byte[] stream = LengthPrefixed(4, nalUnits);
        foreach (byte[] input in new[] { stream[..^1], stream[..^7], new byte[] { 0, 0, 0, 0 } })
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => HevcAnnexB.CopyAsync(new MemoryStream(input), new MemoryStream(), 4, default));
        }
    }

    [TestMethod]
    public void LengthSizeComesFromTheDecoderConfigurationRecord()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mkvmerge.json"));
        Assert.AreEqual(4, HevcAnnexB.LengthSize(json, 0));
        Assert.ThrowsExactly<InvalidDataException>(() => HevcAnnexB.LengthSize("""{"tracks":[{"id":0,"properties":{}}]}""", 0));
    }

    [TestMethod]
    public async Task StreamingConversionPassesMatroskaFramesThroughWithoutFfmpegsAnnexBFilter()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var tools = new ToolCatalog();
            foreach (var tool in new[] { NativeTool.FFmpeg, NativeTool.DoviTool, NativeTool.MkvExtract, NativeTool.MkvMerge })
            {
                tools.Refresh([new(tool, DependencyState.Ready, tool.ToString(), "test", "fixture")]);
            }

            string json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mkvmerge.json"));
            var media = new MediaInfo(new("movie.mkv", 1024, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 2400, 100, 0, 1000, [new(0, "video", "HEVC", "eng", "", true, false, null)], 0, 0, null, json);
            var runner = new StreamingRunner(LengthPrefixed(4, nalUnits));
            var processor = new VideoProcessor(tools, runner, TimeProvider.System, NullLogger<VideoProcessor>.Instance);
            await processor.ConvertAsync(media, ConversionTarget.Profile81, new Workspace(directory), Path.Combine(directory, "output.mkv"), null, Guid.NewGuid(), default);
            Assert.IsNotNull(runner.Producer);
            Assert.AreEqual("FFmpeg", runner.Producer.Executable);
            Assert.DoesNotContain("hevc_mp4toannexb", runner.Producer.Arguments);
            CollectionAssert.IsSubsetOf(new[] { "-c:v", "copy", "-f", "data" }, runner.Producer.Arguments.ToArray());
            CollectionAssert.AreEqual(nalUnits.SelectMany(n => new byte[] { 0, 0, 0, 1 }.Concat(n)).ToArray(), runner.Relayed);
            Assert.IsFalse(runner.Requests.Any(r => r.Arguments.Contains("tracks") && r.Arguments.Contains("--gui-mode")), "Streaming succeeded, so the video is not extracted to disk.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static byte[] LengthPrefixed(int lengthSize, IEnumerable<byte[]> units) => units.SelectMany(n => Enumerable.Range(0, lengthSize).Select(i => (byte)(n.Length >> (8 * (lengthSize - 1 - i)))).Concat(n)).ToArray();

    private sealed class Workspace(string directory) : ITemporaryWorkspace
    {
        public string DirectoryPath => directory;
        public string File(string name) => Path.Combine(directory, name);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TrickleStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }

    private sealed class StreamingRunner(byte[] producerOutput) : IProcessRunner
    {
        public ProcessRequest? Producer
        {
            get;
            private set;
        }

        public byte[] Relayed
        {
            get;
            private set;
        }

        = [];

        public List<ProcessRequest> Requests
        {
            get;
        }

        = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessResult(0, "", ""));
        }

        public async Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken)
        {
            Producer = producer;
            Assert.IsNotNull(producer.OutputRelay);
            using var output = new MemoryStream();
            await producer.OutputRelay(new MemoryStream(producerOutput), output, cancellationToken);
            Relayed = output.ToArray();
        }
    }
}
