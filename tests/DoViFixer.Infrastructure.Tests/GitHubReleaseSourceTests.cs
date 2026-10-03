using System.Net;
using DoViFixer.Infrastructure.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.Infrastructure.Tests;
[TestClass]
public sealed class GitHubReleaseSourceTests
{
    [TestMethod]
    public async Task ReadsTheLatestDoViFixerRelease()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"tag_name":"v0.2.0","html_url":"https://github.com/gbolotin/DoViFixer/releases/tag/v0.2.0"}""");
        using var http = new HttpClient(handler);
        var release = await new GitHubReleaseSource(http).GetLatestAsync(CancellationToken.None);
        Assert.AreEqual("https://api.github.com/repos/gbolotin/DoViFixer/releases/latest", handler.RequestedUri?.ToString());
        Assert.AreEqual("v0.2.0", release?.Version);
        Assert.AreEqual("https://github.com/gbolotin/DoViFixer/releases/tag/v0.2.0", release?.Url);
    }

    [TestMethod]
    public async Task NoPublishedReleaseReturnsNull()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound, """{"message":"Not Found"}"""));
        Assert.IsNull(await new GitHubReleaseSource(http).GetLatestAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task OtherFailuresAreReported()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded"}"""));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new GitHubReleaseSource(http).GetLatestAsync(CancellationToken.None));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
