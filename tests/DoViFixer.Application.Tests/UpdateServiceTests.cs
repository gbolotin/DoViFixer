using DoViFixer.Application.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Application.Tests;
[TestClass]
public sealed class UpdateServiceTests
{
    [TestMethod]
    [DataRow("v0.2.0", "0.1.0-dev+abc1234", true)]
    [DataRow("v0.1.0", "0.1.0-dev+abc1234", true)]
    [DataRow("v0.1.0", "0.1.0-ci.42+abc1234", true)]
    [DataRow("v0.1.0", "0.1.0+abc1234", false)]
    [DataRow("v0.1.0", "0.2.0-dev+abc1234", false)]
    [DataRow("v1.0.0", "0.10.0", true)]
    [DataRow("not-a-version", "0.1.0", false)]
    public async Task ReportsAnUpdateOnlyForANewerRelease(string tag, string current, bool expected)
    {
        var result = await new UpdateService(new FixedSource(new(tag, "https://example.test/release"))).CheckAsync(current, CancellationToken.None);
        Assert.AreEqual(current, result.CurrentVersion);
        Assert.AreEqual(tag, result.Latest?.Version);
        Assert.AreEqual(expected, result.IsUpdateAvailable);
    }

    [TestMethod]
    public async Task NoPublishedReleaseIsNotAnUpdate()
    {
        var result = await new UpdateService(new FixedSource(null)).CheckAsync("0.1.0-dev", CancellationToken.None);
        Assert.IsNull(result.Latest);
        Assert.IsFalse(result.IsUpdateAvailable);
    }

    [TestMethod]
    [DataRow("1.0.0-alpha", "1.0.0-alpha.1")]
    [DataRow("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [DataRow("1.0.0-alpha.beta", "1.0.0-beta")]
    [DataRow("1.0.0-beta.2", "1.0.0-beta.11")]
    [DataRow("1.0.0-rc.1", "1.0.0")]
    [DataRow("1.0.0", "v1.0.1+0123456")]
    [DataRow("1.9.0", "1.10.0")]
    public void PrecedenceFollowsSemVer(string lower, string higher)
    {
        Assert.IsTrue(SemanticVersion.TryParse(lower, out var a));
        Assert.IsTrue(SemanticVersion.TryParse(higher, out var b));
        Assert.IsTrue(a.CompareTo(b) < 0);
        Assert.IsTrue(b.CompareTo(a) > 0);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("v")]
    [DataRow("1.0")]
    [DataRow("1.0.0.0")]
    [DataRow("1.0.0-")]
    [DataRow("latest")]
    public void RejectsTextThatIsNotAVersion(string text) => Assert.IsFalse(SemanticVersion.TryParse(text, out _));

    private sealed class FixedSource(ReleaseInfo? release) : IUpdateSource
    {
        public Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(release);
    }
}
