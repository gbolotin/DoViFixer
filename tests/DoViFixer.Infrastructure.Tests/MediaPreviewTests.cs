using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Infrastructure.Dependencies;
using DoViFixer.Infrastructure.Configuration;
using DoViFixer.Infrastructure.FileSystem;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using DoViFixer.Infrastructure.TemporaryStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class MediaPreviewTests
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-preview-tests", Guid.NewGuid().ToString("N"));
    private static readonly byte[] image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jf1kAAAAASUVORK5CYII=");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private async Task<string> CreateSourceAsync()
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "A movie.mkv");
        await File.WriteAllTextAsync(source, "fixture movie");
        return source;
    }

    [TestMethod]
    [DataRow("smpte2084", "300", "30.000", true)]
    [DataRow("arib-std-b67", "2", "0.200", true)]
    [DataRow("bt709", "N/A", "0.000", false)]
    public async Task ExtractsOneScaledFrameAndCleansWorkspace(string transfer, string duration, string position, bool hdr)
    {
        var tools = new ToolCatalog();
        var processes = new PreviewProcesses(transfer, duration);
        byte[] bytes = await CreatePreview(tools, processes).LoadAsync(await CreateSourceAsync(), default);
        CollectionAssert.AreEqual(image, bytes);
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(processes.OutputPath)));
        var arguments = processes.Request!.Arguments.ToList();
        Assert.AreEqual(position, arguments[arguments.IndexOf("-ss") + 1]);
        Assert.AreEqual("1", arguments[arguments.IndexOf("-frames:v") + 1]);
        string filter = arguments[arguments.IndexOf("-vf") + 1];
        Assert.IsTrue(filter.Contains("640:360"));
        Assert.AreEqual(hdr, filter.Contains("tonemap="));
    }

    [TestMethod]
    public async Task CacheSurvivesNewInstancesInvalidatesChangedMoviesAndClearsOnlyPictures()
    {
        string source = await CreateSourceAsync();
        var processes = new PreviewProcesses("bt709", "300");
        var tools = new ToolCatalog();
        Assert.AreEqual(0L, await CreatePreview(tools, processes).GetCacheSizeAsync(default));
        await CreatePreview(tools, processes).LoadAsync(source, default);
        var preview = CreatePreview(tools, processes);
        CollectionAssert.AreEqual(image, await preview.LoadAsync(source, default));
        Assert.AreEqual(1, processes.Extractions);
        Assert.AreEqual((long)image.Length, await preview.GetCacheSizeAsync(default));

        DateTime modified = File.GetLastWriteTimeUtc(source);
        await File.AppendAllTextAsync(source, "changed size");
        File.SetLastWriteTimeUtc(source, modified);
        await preview.LoadAsync(source, default);
        Assert.AreEqual(2, processes.Extractions);
        File.SetLastWriteTimeUtc(source, modified.AddSeconds(5));
        await preview.LoadAsync(source, default);
        Assert.AreEqual(3, processes.Extractions);

        string other = Path.Combine(directory, "Other.mkv");
        File.Copy(source, other);
        await preview.LoadAsync(other, default);
        Assert.AreEqual(4, processes.Extractions);
        string settings = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(settings, "{}");
        Assert.AreEqual(4L * image.Length, await preview.GetCacheSizeAsync(default));
        Assert.AreEqual(4, await preview.ClearCacheAsync(default));
        Assert.AreEqual(0L, await preview.GetCacheSizeAsync(default));
        Assert.AreEqual(0, await preview.ClearCacheAsync(default));
        Assert.IsTrue(File.Exists(settings));
        Assert.IsTrue(File.Exists(source));
        await preview.LoadAsync(source, default);
        Assert.AreEqual(5, processes.Extractions);
    }

    [TestMethod]
    public async Task BrokenCacheIsRegeneratedAndUnwritableCacheStillReturnsPreview()
    {
        string source = await CreateSourceAsync();
        var processes = new PreviewProcesses("bt709", "300");
        var preview = CreatePreview(new ToolCatalog(), processes);
        await preview.LoadAsync(source, default);
        string cached = Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(cached, "broken");
        CollectionAssert.AreEqual(image, await preview.LoadAsync(source, default));
        Assert.AreEqual(2, processes.Extractions);
        await preview.ClearCacheAsync(default);
        Directory.Delete(Path.GetDirectoryName(cached)!);
        await File.WriteAllTextAsync(Path.GetDirectoryName(cached)!, "blocks writes");
        CollectionAssert.AreEqual(image, await preview.LoadAsync(source, default));
        Assert.AreEqual(3, processes.Extractions);
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task ClearingWaitsForExtractionSoItCannotRepopulateTheCacheAfterwards()
    {
        string source = await CreateSourceAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processes = new PreviewProcesses("bt709", "300")
        {
            DuringExtraction = async token =>
            {
                started.SetResult();
                await finish.Task.WaitAsync(token);
            }
        };
        var preview = CreatePreview(new ToolCatalog(), processes);
        var loading = preview.LoadAsync(source, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clearing = preview.ClearCacheAsync(default);
        Assert.IsFalse(clearing.IsCompleted);
        finish.SetResult();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, await clearing.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.png", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    [TestCategory("NativeIntegration")]
    public async Task ExplicitSourceProducesPreviewWithInstalledTools()
    {
        string? source = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_PREVIEW_SOURCE");
        if (source is null)
        {
            Assert.Inconclusive("Set DOVIFIXER_TEST_PREVIEW_SOURCE and DOVIFIXER_TEST_FFMPEG/FFPROBE to opt in.");
        }
        var tools = new ToolCatalog();
        foreach (var tool in new[] { NativeTool.FFmpeg, NativeTool.FFprobe })
        {
            string? path = Environment.GetEnvironmentVariable("DOVIFIXER_TEST_" + tool.ToString().ToUpperInvariant());
            Assert.IsTrue(File.Exists(path));
            tools.Refresh([new(tool, DependencyState.Ready, path, "test", "explicit preview test")]);
        }
        byte[] bytes = await CreatePreview(tools, new ProcessRunner(NullLogger<ProcessRunner>.Instance), native: true).LoadAsync(source, default);
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        // A new service with no usable tools must still load the persisted frame.
        var cachedPreview = CreatePreview(new ToolCatalog(), new PreviewProcesses("unused", "0"), native: true);
        CollectionAssert.AreEqual(bytes, await cachedPreview.LoadAsync(source, default));
        Assert.AreEqual(1, await cachedPreview.ClearCacheAsync(default));
        if (Environment.GetEnvironmentVariable("DOVIFIXER_TEST_PREVIEW_OUTPUT") is { } output)
        {
            await File.WriteAllBytesAsync(output, bytes);
        }
    }

    private MediaPreview CreatePreview(ToolCatalog tools, IProcessRunner processes, bool native = false)
    {
        var dependencies = new DependencyService(new ReadyDetector(tools, native), null!, null!, tools, NullLogger<DependencyService>.Instance);
        var workspaces = new TemporaryWorkspaceFactory(new FileOperations(), NullLogger<TemporaryWorkspaceFactory>.Instance);
        return new MediaPreview(dependencies, tools, processes, workspaces, new StorageOptions(directory), NullLogger<MediaPreview>.Instance);
    }

    private sealed class ReadyDetector(ToolCatalog tools, bool native) : IDependencyDetector
    {
        public Task<DependencyReport> DetectAsync(IReadOnlyList<NativeTool> requested, CancellationToken cancellationToken, bool skipConfiguredPaths = false)
            => Task.FromResult(new DependencyReport(requested.Select(tool => new DependencyStatus(tool, DependencyState.Ready, native ? tools.GetPath(tool) : tool.ToString(), "test", "test")).ToArray()));
        public Task<DependencyStatus> ValidatePathAsync(NativeTool tool, string path, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PreviewProcesses(string transfer, string duration) : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }
        public string? OutputPath { get; private set; }
        public int Extractions { get; private set; }
        public Func<CancellationToken, Task>? DuringExtraction { get; init; }
        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            if (request.Executable == "FFprobe")
            {
                return new(0, $$$"""{"streams":[{"color_transfer":"{{{transfer}}}"}],"format":{"duration":"{{{duration}}}"}}""", "");
            }
            Request = request;
            Extractions++;
            if (DuringExtraction is not null)
            {
                await DuringExtraction(cancellationToken);
            }
            OutputPath = request.Arguments[^1];
            await File.WriteAllBytesAsync(OutputPath, image, cancellationToken);
            return new(0, "", "");
        }
        public Task PipeAsync(ProcessRequest producer, ProcessRequest consumer, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
