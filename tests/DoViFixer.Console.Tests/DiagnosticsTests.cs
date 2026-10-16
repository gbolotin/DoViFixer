using System.IO.Compression;
using DoViFixer.Console.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Console.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DiagnosticsTests
{
    [TestMethod]
    public void ParserRequiresExportAndOneDestination()
    {
        Assert.AreEqual("report.zip", CommandLine.Parse(["diagnostics", "export", "report.zip"]).Arguments[1]);
        Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(["diagnostics", "export"]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(["diagnostics", "send", "report.zip"]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandLine.Parse(["diagnostics", "export", "report.zip", "--yes"]));
    }

    [TestMethod]
    public async Task ExportThroughRealHostReturnsSuccessPartialAndCollisionExitCodes()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-cli-diagnostics-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("DoViFixer__DataDirectory");
        string? previousLogs = Environment.GetEnvironmentVariable("DoViFixer__Logging__Directory");
        var output = new StringWriter();
        var previousOutput = System.Console.Out;
        try
        {
            Environment.SetEnvironmentVariable("DoViFixer__DataDirectory", directory);
            Environment.SetEnvironmentVariable("DoViFixer__Logging__Directory", Path.Combine(directory, "Logs"));
            Directory.CreateDirectory(Path.Combine(directory, "Logs"));
            foreach (string kind in new[] { "diagnostic", "history", "audit" })
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "Logs", kind + "-20261016.jsonl"), "{\"@m\":\"fixture\"}\n");
            }
            System.Console.SetOut(output);
            string destination = Path.Combine(directory, "support.zip");
            Assert.AreEqual(0, await Program.Main(["diagnostics", "export", destination]));
            using (var zip = ZipFile.OpenRead(destination))
            {
                Assert.IsNotNull(zip.GetEntry("snapshot.json"));
            }
            Assert.AreEqual(1, await Program.Main(["diagnostics", "export", destination]));
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{broken");
            Assert.AreEqual(1, await Program.Main(["diagnostics", "export", Path.Combine(directory, "partial.zip")]));
            Assert.IsTrue(File.Exists(Path.Combine(directory, "partial.zip")));
            StringAssert.Contains(output.ToString(), "nothing is uploaded");
            StringAssert.Contains(output.ToString(), "manifest.json");
        }
        finally
        {
            System.Console.SetOut(previousOutput);
            Environment.SetEnvironmentVariable("DoViFixer__DataDirectory", previous);
            Environment.SetEnvironmentVariable("DoViFixer__Logging__Directory", previousLogs);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
