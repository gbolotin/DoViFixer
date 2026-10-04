using DoViFixer.Application.Dependencies;
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
public sealed class MediaVerifierTests
{
    private const string SourceMediaInfo = """
        {"media":{"track":[{"@type":"Video","Format":"HEVC","Width":"256","Height":"144","FrameCount":"260",
         "FrameRate":"23.976","Duration":"10.803","HDR_Format":"Dolby Vision","HDR_Format_Profile":"dvhe.07.06","MaxCLL":"1000"}]}}
        """;

    // The Profile 8.1 case has a metadata-free ending, so it also exercises the RpuCoverage comparison.
    [TestMethod]
    [DataRow(DolbyVisionProfile.Profile81)]
    [DataRow(DolbyVisionProfile.None)]
    public async Task VerificationExtractsEachVideoOnceAndHoldsAtMostTwoVideoStreams(DolbyVisionProfile expectedProfile)
    {
        var files = new FileOperations();
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(1024 * 1024, null, default);
        string mkv = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mkvmerge.json"));
        string output = workspace.File("output.mkv");
        await File.WriteAllTextAsync(output, "converted");
        string outputMediaInfo = expectedProfile == DolbyVisionProfile.Profile81
            ? SourceMediaInfo.Replace("dvhe.07.06", "dvhe.08.06").Replace("\"MaxCLL\"", "\"HDR_Format_Compatibility\":\"HDR10\",\"MaxCLL\"")
            : SourceMediaInfo.Replace("Dolby Vision", "SMPTE ST 2086").Replace("dvhe.07.06", "");
        var source = MediaMetadataParser.Parse(new(workspace.File("source.mkv"), 1000, DateTime.UnixEpoch), mkv, SourceMediaInfo);
        var runner = new FakeVerificationTools(workspace.DirectoryPath, mkv, SourceMediaInfo, output, outputMediaInfo, rpuFound: expectedProfile != DolbyVisionProfile.None);
        var tools = new ToolCatalog();
        tools.Refresh(Enum.GetValues<NativeTool>().Select(t => new DependencyStatus(t, DependencyState.Ready, t.ToString(), "test", "fixture")));
        var verifier = new MediaVerifier(new MediaProbe(tools, runner, files, NullLogger<MediaProbe>.Instance), new VideoProcessor(tools, runner, TimeProvider.System, NullLogger<VideoProcessor>.Instance), tools, runner);

        var failures = await verifier.VerifyAsync(source, output, expectedProfile, workspace, default);

        Assert.IsEmpty(failures, string.Join("; ", failures));
        CollectionAssert.AreEqual(new[] { source.Source.Path, output }, runner.VideoExtractions);
        Assert.IsTrue(runner.MostVideoStreams <= 2, $"{runner.MostVideoStreams} video streams existed at once.");
        Assert.IsEmpty(Directory.GetFiles(workspace.DirectoryPath, "*.hevc"));
    }

    // Stands in for the native tools: every video stream holds 259 frames with RPU metadata followed by
    // one frame without it, and removing the RPU always yields the same base layer.
    private sealed class FakeVerificationTools(string workspace, string mkvmergeJson, string sourceMediaInfo, string output, string outputMediaInfo, bool rpuFound) : IProcessRunner
    {
        private const int Frames = 260;

        public List<string> VideoExtractions { get; } = [];

        public int MostVideoStreams { get; private set; }

        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            var arguments = request.Arguments;
            var result = new ProcessResult(0, "", "");
            switch (request.Executable, arguments[0])
            {
                case (nameof(NativeTool.MkvMerge), _):
                    result = result with { Output = mkvmergeJson };
                    break;
                case (nameof(NativeTool.MediaInfo), _):
                    result = result with { Output = arguments[^1] == output ? outputMediaInfo : sourceMediaInfo };
                    break;
                case (nameof(NativeTool.FFprobe), _) when arguments.Contains("-count_packets"):
                    result = result with { Output = Frames.ToString() };
                    break;
                case (nameof(NativeTool.FFprobe), _):
                    for (int pts = 0; pts < Frames; pts++)
                    {
                        request.OutputLine!(pts.ToString());
                    }

                    break;
                case (nameof(NativeTool.MkvExtract), "--gui-mode"):
                    VideoExtractions.Add(arguments[1]);
                    await File.WriteAllBytesAsync(Destination(arguments[3]), VideoStream(), cancellationToken);
                    break;
                case (nameof(NativeTool.MkvExtract), _) when arguments[1] == "timestamps_v2":
                    await File.WriteAllTextAsync(Destination(arguments[2]), "# timestamp format v2\n0\n42\n", cancellationToken);
                    break;
                case (nameof(NativeTool.MkvExtract), _):
                    // Chapters and tags are absent, so mkvextract writes nothing.
                    break;
                case (nameof(NativeTool.DoviTool), "extract-rpu") when rpuFound:
                    await File.WriteAllTextAsync(arguments[3], "rpu", cancellationToken);
                    break;
                case (nameof(NativeTool.DoviTool), "extract-rpu"):
                    result = new(1, "", "Error: No RPU was found in input file");
                    break;
                case (nameof(NativeTool.DoviTool), "export"):
                    string frames = string.Join(",", Enumerable.Repeat("""{"dovi_profile":8}""", Frames - 1));
                    await File.WriteAllTextAsync(Path.Combine(request.WorkingDirectory!, arguments[4]["all=".Length..]), $"[{frames}]", cancellationToken);
                    break;
                case (nameof(NativeTool.DoviTool), "remove"):
                    await File.WriteAllTextAsync(arguments[3], "base layer", cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected {request.Executable} {string.Join(' ', arguments)}");
            }

            MostVideoStreams = Math.Max(MostVideoStreams, Directory.GetFiles(workspace, "*.hevc").Length);
            return result;
        }

        private static string Destination(string trackAndPath) => trackAndPath[(trackAndPath.IndexOf(':') + 1)..];

        private static byte[] VideoStream()
        {
            byte[] delimiter = [0, 0, 0, 1, 35 << 1, 1];
            byte[] slice = [0, 0, 0, 1, 1 << 1, 1, 128];
            byte[] rpu = [0, 0, 0, 1, 62 << 1, 1];
            byte[] frameWithRpu = [.. delimiter, .. slice, .. rpu];
            return [.. Enumerable.Repeat(frameWithRpu, Frames - 1).SelectMany(frame => frame), .. delimiter, .. slice];
        }
    }
}
