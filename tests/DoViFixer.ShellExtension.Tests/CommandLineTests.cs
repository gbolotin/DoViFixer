using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.ShellExtension.Tests;

/// <summary>The command lines a File Explorer command starts the app with.</summary>
[TestClass]
public sealed class CommandLineTests
{
    [TestMethod]
    public void OpenPassesTheSelectedItemsAndConvertAddsItsArgument()
    {
        string[] paths = [@"C:\Media\Mountain.mkv", @"C:\Media\Season 1"];

        var open = CommandLines.For(paths, convert: false);
        var convert = CommandLines.For(paths, convert: true);

        CollectionAssert.AreEqual(paths, open.Single().ToArray());
        CollectionAssert.AreEqual(new[] { "--convert", @"C:\Media\Mountain.mkv", @"C:\Media\Season 1" }, convert.Single().ToArray());
        Assert.AreEqual(0, CommandLines.For([], convert: true).Count, "Nothing selected starts nothing.");
    }

    [TestMethod]
    public void ManyItemsAreSplitIntoCommandLinesWindowsAccepts()
    {
        string[] paths = [.. Enumerable.Range(0, 1000).Select(index => $@"C:\Media\A long folder name for movies\Movie number {index:0000}.mkv")];

        var lines = CommandLines.For(paths, convert: true);

        Assert.IsTrue(lines.Count > 1);
        Assert.IsTrue(lines.All(line => line[0] == "--convert" && line.Sum(argument => argument.Length + 3) <= CommandLines.MaximumLength + 12));
        CollectionAssert.AreEqual(paths, lines.SelectMany(line => line.Skip(1)).ToArray(), "Every item is passed once, in order.");
    }
}
