using System.Collections.Concurrent;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using DoViFixer.Infrastructure.TemporaryStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class ParallelScanTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SamplesRespectLimitUseUniqueFilesAndRetainPartialFailure(bool failSample)
    {
        await WithProbeAsync(async (probe, runner, workspace) =>
        {
            runner.FailSample = failSample;
            var progress = new RecordingProgress();
            var evidence = await probe.AnalyzeAsync(Media(), AnalysisMethod.SampledRpu, workspace, default, progress);
            Assert.AreEqual(3, runner.Peak);
            Assert.AreEqual(0, runner.Active);
            Assert.AreEqual(failSample ? 9 : 10, evidence.SuccessfulSamples);
            Assert.AreEqual(failSample ? 51 : 55, evidence.Frames);
            Assert.AreEqual(EnhancementLayer.Fel, evidence.Layer);
            Assert.AreEqual(10, runner.RawPaths.Count);
            Assert.AreEqual(failSample ? 9 : 10, runner.JsonPaths.Count);
            Assert.AreEqual(0, Directory.GetFiles(workspace.DirectoryPath).Length);
            Assert.AreEqual(failSample, evidence.SampleDiagnostics is not null);
            Assert.IsTrue(progress.Values.Zip(progress.Values.Skip(1)).All(pair => pair.First.Percent <= pair.Second.Percent));
            Assert.AreEqual(100d, progress.Values.Last().Percent);
        });
    }

    [TestMethod]
    public async Task CancellationAwaitsEverySampleAndRemovesTemporaryFiles()
    {
        await WithProbeAsync(async (probe, runner, workspace) =>
        {
            runner.WaitForCancellation = true;
            using var cancellation = new CancellationTokenSource();
            var analysis = probe.AnalyzeAsync(Media(), AnalysisMethod.SampledRpu, workspace, cancellation.Token);
            await runner.FirstWave.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => analysis);
            Assert.AreEqual(0, runner.Active);
            Assert.AreEqual(0, Directory.GetFiles(workspace.DirectoryPath).Length);
        });
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(7)]
    public async Task ConfiguredClipsUseEvenPositionsAndRequestedDuration(int count)
    {
        await WithProbeAsync(async (probe, runner, workspace) =>
        {
            runner.FailSample = true;
            var evidence = await probe.AnalyzeAsync(Media(), AnalysisMethod.SampledRpu, workspace, default);
            Assert.AreEqual(count, evidence.RequestedSamples);
            Assert.AreEqual(count == 1 ? 1 : count - 1, evidence.SuccessfulSamples);
            Assert.AreEqual(50, evidence.Options!.MinimumScanCoveragePercent);
            Assert.AreEqual(AnalysisVerdict.Mel, MediaClassifier.Classify(Media(), evidence).Verdict);
            var requests = runner.Requests.Where(request => request.Executable == "ffmpeg").ToArray();
            Assert.HasCount(count, requests);
            foreach (var request in requests)
            {
                var arguments = request.Arguments.ToList();
                int index = int.Parse(Path.GetFileNameWithoutExtension(arguments[^1]).Split('-')[1]);
                double start = double.Parse(arguments[arguments.IndexOf("-ss") + 1], System.Globalization.CultureInfo.InvariantCulture);
                Assert.AreEqual((index + 0.5) / count * 100, start, 0.001);
                Assert.AreEqual("2.5", arguments[arguments.IndexOf("-t") + 1]);
            }
        }, new UserSettings { AnalysisWorkers = 3, ScanSampleCount = count, ScanSampleSeconds = 2.5, MinimumScanCoveragePercent = 50 });
    }

    private static MediaInfo Media() => new(new("movie.mkv", 1024, DateTime.UnixEpoch), DolbyVisionProfile.Profile7, "HEVC", 0, 1920, 1080, 24, 2400, 100, 0, 1000, [], 0, 0, null, "{}");

    private static async Task WithProbeAsync(Func<MediaProbe, SampleRunner, ITemporaryWorkspace, Task> test, UserSettings? preferences = null)
    {
        var files = new FileOperations();
        var factory = new TemporaryWorkspaceFactory(files, NullLogger<TemporaryWorkspaceFactory>.Instance);
        await using var workspace = await factory.CreateAsync(1024, null, default);
        // Keep settings outside the sample workspace so cleanup assertions only count sample files.
        var settings = new FixedSettings(preferences ?? new UserSettings { AnalysisWorkers = 3 });
        var tools = new ToolCatalog();
        tools.Refresh([new(NativeTool.FFmpeg, DependencyState.Ready, "ffmpeg", "test", "fixture"), new(NativeTool.DoviTool, DependencyState.Ready, "dovi_tool", "test", "fixture")]);
        var runner = new SampleRunner { WaveSize = Math.Min(preferences?.ScanSampleCount ?? 10, 3) };
        var probe = new MediaProbe(tools, runner, files, NullLogger<MediaProbe>.Instance, settings);
        await test(probe, runner, workspace);
    }

    private sealed class FixedSettings(UserSettings value) : ISettingsStore
    {
        public Task<UserSettings> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(value);
        public Task UpdateAsync(Func<UserSettings, UserSettings> update, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingProgress : IProgress<OperationProgress>
    {
        public List<OperationProgress> Values { get; } = [];
        public void Report(OperationProgress value) => Values.Add(value);
    }

    private sealed class SampleRunner : IProcessRunner
    {
        public ConcurrentBag<ProcessRequest> Requests { get; } = [];
        public int WaveSize = 3;
        public int Active;
        public int Peak;
        public bool FailSample;
        public bool WaitForCancellation;
        public TaskCompletionSource FirstWave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, byte> RawPaths { get; } = new();
        public ConcurrentDictionary<string, byte> JsonPaths { get; } = new();
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Executable == "ffmpeg")
            {
                string raw = request.Arguments[^1];
                Assert.IsTrue(RawPaths.TryAdd(raw, 0));
                int active = Interlocked.Increment(ref Active);
                RecordPeak(active);
                if (active == WaveSize)
                {
                    FirstWave.TrySetResult();
                }
                try
                {
                    await File.WriteAllTextAsync(raw, "sample", cancellationToken);
                    await FirstWave.Task.WaitAsync(cancellationToken);
                    if (WaitForCancellation)
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                    }
                    if (FailSample && Path.GetFileName(raw) == "sample-3.hevc")
                    {
                        throw new IOException("Fixture sample failure");
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref Active);
                }
            }
            else if (request.Arguments[0] == "export")
            {
                string name = request.Arguments[^1]["all=".Length..];
                string json = Path.Combine(request.WorkingDirectory!, name);
                Assert.IsTrue(JsonPaths.TryAdd(json, 0));
                int index = int.Parse(Path.GetFileNameWithoutExtension(name).Split('-')[1]);
                string frame = index == 9 ? "{\"el_type\":\"FEL\"}" : "{\"el_type\":\"MEL\"}";
                await File.WriteAllTextAsync(json, "[" + string.Join(',', Enumerable.Repeat(frame, index + 1)) + "]", cancellationToken);
            }
            return new(0, "", "");
        }
        private void RecordPeak(int value)
        {
            int observed;
            do
            {
                observed = Peak;
                if (observed >= value)
                {
                    return;
                }
            } while (Interlocked.CompareExchange(ref Peak, value, observed) != observed);
        }
        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
