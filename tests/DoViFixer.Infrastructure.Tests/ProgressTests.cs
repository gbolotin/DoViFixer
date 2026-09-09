using System.Text;
using DoViFixer.Application.Operations;
using DoViFixer.Infrastructure.MediaTools;
using DoViFixer.Infrastructure.MediaTools.Processes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class ProgressTests
{
    [TestMethod]
    public async Task ParsesProgressAcrossReadBoundariesAndPreservesCapturedOutput()
    {
        string text = new string('x', 8190) + "\r#GUI#progress 25%\r\n#GUI#progress 20%\n#GUI#progress 101%\n#GUI#progress 100%";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new StreamReader(stream);
        var sink = new RecordingProgress();
        var id = Guid.NewGuid();
        var progress = new MkvProgress(sink, id, "Extracting video", "movie.mkv");
        Assert.AreEqual(text, await ProcessRunner.DrainAsync(reader, progress.Report));
        CollectionAssert.AreEqual(new double?[] { 25, 99 }, sink.Values.Select(v => v.Percent).ToArray());
        Assert.IsTrue(sink.Values.All(v => v.OperationId == id && v.File == "movie.mkv"));
    }

    private sealed class RecordingProgress : IProgress<OperationProgress>
    {
        public List<OperationProgress> Values { get; } = [];
        public void Report(OperationProgress value) => Values.Add(value);
    }
}
