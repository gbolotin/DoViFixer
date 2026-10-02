using DoViFixer.App.Presentation.Application;
using DoViFixer.Application.Dependencies;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.App.Tests;

[TestClass]
public sealed class PresentationTests
{
    [TestMethod]
    public void EveryNativeToolHasANameAndPurposeInTheSummary()
    {
        foreach (var tool in Enum.GetValues<NativeTool>())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(NativeToolDescriptions.Purpose(tool)), $"{tool} needs a purpose.");
            StringAssert.Contains(NativeToolDescriptions.Summary, $"{NativeToolDescriptions.Name(tool)} — {NativeToolDescriptions.Purpose(tool)}");
        }
    }

    [TestMethod]
    public void NativeToolRowShowsStateWithVersionWhenKnown()
    {
        var ready = new NativeToolRow(new DependencyStatus(NativeTool.MkvMerge, DependencyState.Ready, @"C:\Tools\mkvmerge.exe", "90.0", "ok"));
        var missing = new NativeToolRow(new DependencyStatus(NativeTool.DoviTool, DependencyState.Missing, null, null, "missing"));

        Assert.AreEqual("mkvmerge (MKVToolNix)", ready.Name);
        Assert.AreEqual("Ready · 90.0", ready.StateText);
        Assert.IsTrue(ready.IsReady);
        Assert.AreEqual("Missing", missing.StateText);
        Assert.IsFalse(missing.IsReady);
    }
}
