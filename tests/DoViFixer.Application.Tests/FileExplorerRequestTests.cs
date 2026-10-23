using DoViFixer.Application.FileExplorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;

/// <summary>The command line File Explorer's right-click menu starts the app with.</summary>
[TestClass]
public sealed class FileExplorerRequestTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "Movies");

    [TestMethod]
    public void PathsAreReadRelativeToTheFolderTheAppStartedIn()
    {
        string absolute = Path.Combine(Path.GetTempPath(), "Other", "City.mkv");

        var request = FileExplorerRequest.Parse(["Mountain.mkv", absolute], Folder);

        Assert.IsFalse(request.Convert);
        CollectionAssert.AreEqual(new[] { Path.Combine(Folder, "Mountain.mkv"), absolute }, request.Paths.ToArray());
    }

    [TestMethod]
    public void ConvertAsksToConvertAndOtherOptionsAreIgnored()
    {
        var request = FileExplorerRequest.Parse(["--CONVERT", "--unknown", "", "Mountain.mkv"], Folder);

        Assert.IsTrue(request.Convert);
        CollectionAssert.AreEqual(new[] { Path.Combine(Folder, "Mountain.mkv") }, request.Paths.ToArray());
    }

    [TestMethod]
    public void TheArgumentsReadBackAsTheSameRequestInAnyFolder()
    {
        var request = FileExplorerRequest.Parse([FileExplorerRequest.ConvertArgument, "Mountain.mkv", "Season 1"], Folder);

        var forwarded = FileExplorerRequest.Parse(request.ToArguments(), Path.GetTempPath());

        Assert.IsTrue(forwarded.Convert);
        CollectionAssert.AreEqual(request.Paths.ToArray(), forwarded.Paths.ToArray());
        Assert.AreEqual(0, FileExplorerRequest.Parse([], Folder).ToArguments().Count, "A start without files only brings the window forward.");
    }
}
