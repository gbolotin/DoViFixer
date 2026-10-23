using DoViFixer.Application.Updates;
using DoViFixer.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace DoViFixer.Infrastructure.Tests;

/// <summary>The right-click commands, written under a test key instead of the user's classes.</summary>
[TestClass]
public sealed class FileExplorerMenuTests
{
    private readonly string root = $@"Software\DoViFixer.Tests\{Guid.NewGuid():N}";

    [TestCleanup]
    public void Cleanup() => Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);

    [TestMethod]
    public void MkvFilesAndFoldersOfferOpenAndConvert()
    {
        var menu = new FileExplorerMenu(NullLogger<FileExplorerMenu>.Instance, root);

        menu.Update(true, @"C:\Apps\DoViFixer\DoViFixer.App.exe");

        foreach (string target in FileExplorerMenu.Targets)
        {
            using var open = Registry.CurrentUser.OpenSubKey($@"{root}\{target}\shell\{FileExplorerMenu.OpenVerb}")!;
            Assert.AreEqual($"Open in {ApplicationTitle.Name}", open.GetValue(""));
            Assert.AreEqual("Player", open.GetValue("MultiSelectModel"), "The commands stay available for more than 15 selected files.");
            Assert.AreEqual(@"""C:\Apps\DoViFixer\DoViFixer.App.exe"" ""%1""", Command(target, FileExplorerMenu.OpenVerb));
            using var convert = Registry.CurrentUser.OpenSubKey($@"{root}\{target}\shell\{FileExplorerMenu.ConvertVerb}")!;
            Assert.AreEqual($"Convert with {ApplicationTitle.Name}", convert.GetValue(""));
            Assert.AreEqual(@"""C:\Apps\DoViFixer\DoViFixer.App.exe"" --convert ""%1""", Command(target, FileExplorerMenu.ConvertVerb));
        }
    }

    [TestMethod]
    public void TheCommandsFollowTheAppAndHidingThemKeepsOtherCommands()
    {
        var menu = new FileExplorerMenu(NullLogger<FileExplorerMenu>.Instance, root);
        using (var other = Registry.CurrentUser.CreateSubKey($@"{root}\Directory\shell\Other.Command"))
        {
            other.SetValue("", "Another app");
        }

        menu.Update(true, @"C:\Apps\DoViFixer\DoViFixer.App.exe");
        menu.Update(true, @"D:\Moved\DoViFixer.App.exe");
        Assert.AreEqual(@"""D:\Moved\DoViFixer.App.exe"" ""%1""", Command("Directory", FileExplorerMenu.OpenVerb));

        menu.Update(false, @"D:\Moved\DoViFixer.App.exe");
        menu.Update(false, @"D:\Moved\DoViFixer.App.exe");
        foreach (string target in FileExplorerMenu.Targets)
        {
            Assert.IsNull(Registry.CurrentUser.OpenSubKey($@"{root}\{target}\shell\{FileExplorerMenu.OpenVerb}"));
            Assert.IsNull(Registry.CurrentUser.OpenSubKey($@"{root}\{target}\shell\{FileExplorerMenu.ConvertVerb}"));
        }

        using var kept = Registry.CurrentUser.OpenSubKey($@"{root}\Directory\shell\Other.Command");
        Assert.IsNotNull(kept, "Another app's command stays.");
    }

    private string? Command(string target, string verb)
    {
        using var command = Registry.CurrentUser.OpenSubKey($@"{root}\{target}\shell\{verb}\command");
        return command?.GetValue("") as string;
    }
}
