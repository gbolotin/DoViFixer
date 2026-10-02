using DoViFixer.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;
[TestClass]
public sealed class SourceFileMonitorTests
{
    [TestMethod]
    public async Task ReportsDeletedAndRestoredMediaInWatchedFolders()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string media = Path.Combine(directory, "Movie.mkv");
            await File.WriteAllTextAsync(media, "fixture");
            using var monitor = new SourceFileMonitor(NullLogger<SourceFileMonitor>.Instance);
            var changed = new SemaphoreSlim(0);
            monitor.Changed += (_, _) => changed.Release();
            monitor.Watch([media]);
            Assert.IsTrue(monitor.Exists(media));

            File.Delete(media);
            Assert.IsTrue(await changed.WaitAsync(TimeSpan.FromSeconds(10)), "Deleting a watched file raises Changed.");
            Assert.IsFalse(monitor.Exists(media));

            await File.WriteAllTextAsync(media, "fixture");
            Assert.IsTrue(await changed.WaitAsync(TimeSpan.FromSeconds(10)), "Restoring the file raises Changed.");
            Assert.IsTrue(monitor.Exists(media));

            monitor.Watch([]);
            while (changed.CurrentCount > 0)
            {
                await changed.WaitAsync();
            }

            File.Delete(media);
            Assert.IsFalse(await changed.WaitAsync(TimeSpan.FromMilliseconds(500)), "Folders no longer listed are not watched.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void UnavailableFoldersAreSkippedUntilTheNextWatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-tests-" + Guid.NewGuid().ToString("N"));
        using var monitor = new SourceFileMonitor(NullLogger<SourceFileMonitor>.Instance);
        string media = Path.Combine(directory, "Movie.mkv");

        monitor.Watch([media]);

        Assert.IsFalse(monitor.Exists(media));
    }
}
