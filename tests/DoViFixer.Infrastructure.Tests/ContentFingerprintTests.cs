using DoViFixer.Domain.Media;
using DoViFixer.Infrastructure.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;

[TestClass]
public sealed class ContentFingerprintTests
{
    [TestMethod]
    public async Task LargeFileFingerprintUsesStartMiddleAndEndRatherThanEveryByte()
    {
        string path = Path.Combine(Path.GetTempPath(), "DoViFixer-fingerprint-" + Guid.NewGuid().ToString("N") + ".mkv");
        try
        {
            await using (var stream = File.Create(path))
            {
                stream.SetLength(40 * 1024 * 1024);
            }
            FileIdentity Identify() => new(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path));
            string original = await ContentFingerprint.ComputeAsync(Identify(), default);
            foreach (long offset in new long[] { 0, 20 * 1024 * 1024, 40 * 1024 * 1024 - 1 })
            {
                await using (var stream = File.OpenWrite(path))
                {
                    stream.Position = offset;
                    stream.WriteByte(1);
                }
                Assert.AreNotEqual(original, await ContentFingerprint.ComputeAsync(Identify(), default));
                await using var restore = File.OpenWrite(path);
                restore.Position = offset;
                restore.WriteByte(0);
            }
            await using (var stream = File.OpenWrite(path))
            {
                stream.Position = 12 * 1024 * 1024;
                stream.WriteByte(1);
            }
            Assert.AreEqual(original, await ContentFingerprint.ComputeAsync(Identify(), default), "Quick fingerprints intentionally do not cover unsampled bytes.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task FingerprintFollowsContentAcrossRenamesAndRejectsChangedIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DoViFixer-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Before.mkv");
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
            FileIdentity Identify(string file) => new(file, new FileInfo(file).Length, File.GetLastWriteTimeUtc(file));
            string original = await ContentFingerprint.ComputeAsync(Identify(path), default);
            string renamed = Path.Combine(directory, "After.mkv");
            File.Move(path, renamed);
            Assert.AreEqual(original, await ContentFingerprint.ComputeAsync(Identify(renamed), default));
            var oldIdentity = Identify(renamed);
            await File.WriteAllBytesAsync(renamed, [5, 6, 7, 8, 9]);
            await Assert.ThrowsExactlyAsync<IOException>(() => ContentFingerprint.ComputeAsync(oldIdentity, default));
            Assert.AreNotEqual(original, await ContentFingerprint.ComputeAsync(Identify(renamed), default));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => ContentFingerprint.ComputeAsync(Identify(renamed), cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
