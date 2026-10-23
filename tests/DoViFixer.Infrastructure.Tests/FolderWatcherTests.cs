using DoViFixer.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class FolderWatcherTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);

    [TestMethod]
    public async Task ReportsANewFileOnceItIsClosedAndNotOtherFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string existing = Path.Combine(directory, "Existing.mkv");
            await File.WriteAllTextAsync(existing, "fixture");
            using var watcher = new FolderWatcher(TimeProvider.System, NullLogger<FolderWatcher>.Instance);
            var arrived = new List<string>();
            var signal = new SemaphoreSlim(0);
            watcher.FileArrived += (_, path) =>
            {
                lock (arrived)
                {
                    arrived.Add(path);
                }

                signal.Release();
            };
            Assert.IsTrue(watcher.Watch(directory, includeSubfolders: false, Settle));

            string movie = Path.Combine(directory, "Movie.mkv");
            await using (var writing = new FileStream(movie, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await writing.WriteAsync(new byte[1024]);
                await writing.FlushAsync();
                Assert.IsFalse(await signal.WaitAsync(TimeSpan.FromSeconds(3)), "A file still open for writing has not finished arriving.");
            }

            Assert.IsTrue(await signal.WaitAsync(TimeSpan.FromSeconds(10)), "The closed file is reported.");
            await File.WriteAllTextAsync(Path.Combine(directory, "Notes.txt"), "fixture");
            Assert.IsFalse(await signal.WaitAsync(TimeSpan.FromSeconds(2)), "Only Matroska files are reported, and each once.");
            lock (arrived)
            {
                CollectionAssert.AreEqual(new[] { movie }, arrived, "Files already in the folder are not reported.");
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task APartialDownloadCountsOnceRenamedToMkvAndSubfoldersFollowTheSetting()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-tests-" + Guid.NewGuid().ToString("N"));
        string subfolder = Path.Combine(directory, "Film");
        Directory.CreateDirectory(subfolder);
        try
        {
            using var watcher = new FolderWatcher(TimeProvider.System, NullLogger<FolderWatcher>.Instance);
            var signal = new SemaphoreSlim(0);
            string? last = null;
            watcher.FileArrived += (_, path) =>
            {
                last = path;
                signal.Release();
            };
            Assert.IsTrue(watcher.Watch(directory, includeSubfolders: false, Settle));

            await File.WriteAllTextAsync(Path.Combine(subfolder, "Nested.mkv"), "fixture");
            Assert.IsFalse(await signal.WaitAsync(TimeSpan.FromSeconds(2)), "Subfolders are left out when not included.");

            string partial = Path.Combine(directory, "Movie.mkv.part");
            await File.WriteAllTextAsync(partial, "fixture");
            Assert.IsFalse(await signal.WaitAsync(TimeSpan.FromSeconds(2)), "A partial download is not a film yet.");
            File.Move(partial, Path.Combine(directory, "Movie.mkv"));
            Assert.IsTrue(await signal.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.AreEqual(Path.Combine(directory, "Movie.mkv"), last);

            Assert.IsTrue(watcher.Watch(directory, includeSubfolders: true, Settle));
            string nested = Path.Combine(subfolder, "Second.mkv");
            await File.WriteAllTextAsync(nested, "fixture");
            Assert.IsTrue(await signal.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.AreEqual(nested, last);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void AnUnavailableFolderIsNotWatchedUntilItExists()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-tests-" + Guid.NewGuid().ToString("N"));
        using var watcher = new FolderWatcher(TimeProvider.System, NullLogger<FolderWatcher>.Instance);
        try
        {
            Assert.IsFalse(watcher.Watch(directory, true, Settle));
            Directory.CreateDirectory(directory);
            Assert.IsTrue(watcher.Watch(directory, true, Settle), "Watching the same folder again retries it.");
            Assert.IsFalse(watcher.Watch(null, true, Settle));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
