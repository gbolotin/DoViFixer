using DoViFixer.Application.Operations;
using DoViFixer.Console.Rendering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Console.Tests;

[TestClass]
public sealed class ProgressTests
{
    [TestMethod]
    public void InteractiveUpdatesReuseLineAndFinishBeforeNextStage()
    {
        using var output = new StringWriter();
        using var renderer = new ConsoleRenderer(output, true, () => 80);
        var progress = new OperationProgress(Guid.NewGuid(), "Extracting", "movie.mkv", 0);
        renderer.Report(progress);
        renderer.Report(progress with { Percent = 42 });
        Assert.AreEqual(0, output.ToString().Count(c => c == '\n'));
        StringAssert.Contains(output.ToString(), "\rExtracting  42%");
        renderer.Report(progress with { Percent = 100 });
        renderer.Report(progress with { Stage = "Verifying", Percent = null });
        renderer.Dispose();
        Assert.AreEqual(2, output.ToString().Count(c => c == '\n'));
    }

    [TestMethod]
    public void RedirectedOutputSuppressesIntermediatePercentages()
    {
        using var output = new StringWriter();
        using var renderer = new ConsoleRenderer(output, false, () => throw new AssertFailedException("Must not access terminal size."));
        var progress = new OperationProgress(Guid.NewGuid(), "Remuxing", "movie.mkv");
        renderer.Report(progress);
        for (int i = 0; i <= 100; i++)
        {
            renderer.Report(progress with { Percent = i });
        }
        Assert.AreEqual(2, output.ToString().Count(c => c == '\n'));
        StringAssert.Contains(output.ToString(), "Remuxing 100%");
    }
}
