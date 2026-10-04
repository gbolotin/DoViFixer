using DoViFixer.Application.Abstractions;
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
    private const string SourcePath = @"C:\media\source.mkv";
    private const string OutputPath = @"C:\media\output.partial";
    private const string SourceMediaInfo = """
        {"media":{"track":[{"@type":"Video","Format":"HEVC","Width":"256","Height":"144","FrameCount":"260",
         "FrameRate":"23.976","Duration":"10.803","HDR_Format":"Dolby Vision","HDR_Format_Profile":"dvhe.07.06","MaxCLL":"1000"}]}}
        """;
    private static readonly MediaTrack[] tracks =
    [
        new(0, "video", "HEVC", "eng", "", true, false, "100"),
        new(1, "audio", "TrueHD Atmos", "eng", "", true, false, "101"),
        new(2, "audio", "AC-3", "fre", "", false, false, "102"),
        new(3, "subtitles", "HDMV PGS", "eng", "", false, false, "103")
    ];

    [TestMethod]
    public async Task TrackVerificationReadsEachFileInOnePassAndCleansUp()
    {
        await using var workspace = await CreateWorkspaceAsync();
        var mkvExtract = new FakeMkvExtract(workspace.DirectoryPath);
        var failures = new List<VerificationFinding>();
        await CreateVerifier(mkvExtract).VerifyTracksAsync(Media(SourcePath), Media(OutputPath), workspace, failures, default);
        Assert.AreEqual(0, failures.Count, string.Join("; ", failures.Select(f => f.Message)));
        CollectionAssert.AreEqual(new[]
        {
            SourcePath,
            OutputPath
        }, mkvExtract.Calls.Select(arguments => arguments[0]).ToArray(), "One mkvextract pass per file covers every track and mode.");
        Assert.IsFalse(mkvExtract.FilesBeforeEachCall[1].Any(name => name.EndsWith(".bin", StringComparison.Ordinal)), "Source payloads are hashed and deleted before the output pass.");
        Assert.AreEqual(0, Directory.GetFiles(workspace.DirectoryPath).Length);
    }

    [TestMethod]
    public async Task TrackVerificationReportsChangedPayloadsAndTimestampsPerTrack()
    {
        await using var workspace = await CreateWorkspaceAsync();
        var mkvExtract = new FakeMkvExtract(workspace.DirectoryPath);
        mkvExtract.Payloads[(OutputPath, 2)] = "changed audio";
        mkvExtract.Timestamps[(OutputPath, 0)] = "# timestamp format v2\n0\n45\n";
        mkvExtract.Timestamps[(OutputPath, 3)] = "# timestamp format v2\n500\n2500\n";
        var failures = new List<VerificationFinding>();
        await CreateVerifier(mkvExtract).VerifyTracksAsync(Media(SourcePath), Media(OutputPath), workspace, failures, default);
        // Only the video track is re-extracted by a safe-mode retry; other tracks come from the same remux.
        CollectionAssert.AreEqual(new VerificationFinding[]
        {
            new(VerificationArea.VideoStream, "Track 0 timestamps changed or verification is unavailable."),
            new(VerificationArea.Container, "Track 2 payload changed."),
            new(VerificationArea.Container, "Track 3 timestamps changed or verification is unavailable.")
        }, failures);
        Assert.AreEqual(0, Directory.GetFiles(workspace.DirectoryPath).Length);
    }

    [TestMethod]
    public async Task FailedTrackExtractionLeavesNoScratchFilesForARetry()
    {
        await using var workspace = await CreateWorkspaceAsync();
        var mkvExtract = new FakeMkvExtract(workspace.DirectoryPath)
        {
            FailingFile = OutputPath
        };
        await Assert.ThrowsExactlyAsync<IOException>(() => CreateVerifier(mkvExtract).VerifyTracksAsync(Media(SourcePath), Media(OutputPath), workspace, [], default));
        Assert.AreEqual(0, Directory.GetFiles(workspace.DirectoryPath).Length);
    }

    // The Profile 8.1 case has a metadata-free ending, so it also exercises the RpuCoverage comparison.
    [TestMethod]
    [DataRow(DolbyVisionProfile.Profile81)]
    [DataRow(DolbyVisionProfile.None)]
    public async Task VerificationExtractsEachVideoOnceAndHoldsAtMostTwoVideoStreams(DolbyVisionProfile expectedProfile)
    {
        var files = new FileOperations();
        await using var workspace = await CreateWorkspaceAsync();
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
        var verifier = new MediaVerifier(new MediaProbe(tools, runner, files, NullLogger<MediaProbe>.Instance), new VideoProcessor(tools, runner, TimeProvider.System, NullLogger<VideoProcessor>.Instance), tools, runner, NullLogger<MediaVerifier>.Instance);

        var failures = await verifier.VerifyAsync(source, output, expectedProfile, workspace, default);

        Assert.IsEmpty(failures, string.Join("; ", failures));
        CollectionAssert.AreEqual(new[] { source.Source.Path, output }, runner.VideoExtractions);
        Assert.IsTrue(runner.MostVideoStreams <= 2, $"{runner.MostVideoStreams} video streams existed at once.");
        Assert.IsEmpty(Directory.GetFiles(workspace.DirectoryPath, "*.hevc"));
    }

    private static MediaInfo Media(string path) => new(new(path, 1000, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 3840, 2160, null, null, null, 0, null, tracks, 0, 0, null, "{}");

    private static ValueTask<ITemporaryWorkspace> CreateWorkspaceAsync() => new TemporaryWorkspaceFactory(new FileOperations(), NullLogger<TemporaryWorkspaceFactory>.Instance).CreateAsync(1024 * 1024, null, default);

    private static MediaVerifier CreateVerifier(IProcessRunner processes)
    {
        var tools = new ToolCatalog();
        tools.Refresh([new DependencyStatus(NativeTool.MkvExtract, DependencyState.Ready, "mkvextract", "test", "test")]);
        var probe = new MediaProbe(tools, processes, new FileOperations(), NullLogger<MediaProbe>.Instance);
        var processor = new VideoProcessor(tools, processes, TimeProvider.System, NullLogger<VideoProcessor>.Instance);
        return new MediaVerifier(probe, processor, tools, processes, NullLogger<MediaVerifier>.Instance);
    }

    // Writes each requested track or timestamp file as mkvextract would, from content keyed by file and track ID.
    private sealed class FakeMkvExtract : IProcessRunner
    {
        private readonly string directory;

        public FakeMkvExtract(string directory)
        {
            this.directory = directory;
            foreach (string file in new[] { SourcePath, OutputPath })
            {
                foreach (var track in tracks.Where(t => t.Type != "video"))
                {
                    Payloads[(file, track.Id)] = $"{track.Codec} payload";
                }

                Timestamps[(file, 0)] = "# timestamp format v2\n0\n41.708333\n";
                Timestamps[(file, 1)] = "# timestamp format v2\n0\n0.833333\n";
                Timestamps[(file, 2)] = "# timestamp format v2\n0\n32\n";
                Timestamps[(file, 3)] = "# timestamp format v2\n500\n2000\n";
            }
        }

        public Dictionary<(string File, int Track), string> Payloads { get; } = [];
        public Dictionary<(string File, int Track), string> Timestamps { get; } = [];
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public List<string[]> FilesBeforeEachCall { get; } = [];
        public string? FailingFile { get; init; }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            FilesBeforeEachCall.Add(Directory.GetFiles(directory).Select(path => Path.GetFileName(path)).ToArray());
            Calls.Add(request.Arguments);
            string file = request.Arguments[0];
            string mode = "";
            foreach (string argument in request.Arguments.Skip(1))
            {
                if (argument is "tracks" or "timestamps_v2")
                {
                    mode = argument;
                    continue;
                }

                int separator = argument.IndexOf(':');
                var content = mode == "tracks" ? Payloads : Timestamps;
                await File.WriteAllTextAsync(argument[(separator + 1)..], content[(file, int.Parse(argument[..separator]))], cancellationToken);
            }

            // Like a real failure, the partial files written so far stay behind.
            if (file == FailingFile)
            {
                throw new IOException("mkvextract exited 2");
            }

            return new(0, "", "");
        }

        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => throw new NotSupportedException();
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
