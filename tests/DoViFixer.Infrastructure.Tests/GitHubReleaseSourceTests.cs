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

    [TestMethod]
    public async Task NoConnectionIsReportedAsUnreachable()
    {
        using var http = new HttpClient(new FailingHandler(_ => throw new HttpRequestException("No such host is known.")));
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new GitHubReleaseSource(http).GetLatestAsync(CancellationToken.None));
        StringAssert.Contains(error.Message, "Could not reach GitHub");
    }

    [TestMethod]
    public async Task ASilentNetworkTimesOutInsteadOfLookingCancelled()
    {
        using var http = new HttpClient(new FailingHandler(token => Task.Delay(System.Threading.Timeout.Infinite, token)));
        var source = new GitHubReleaseSource(http) { Timeout = TimeSpan.FromMilliseconds(50) };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => source.GetLatestAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task CallerCancellationStaysACancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var http = new HttpClient(new FailingHandler(token => Task.Delay(System.Threading.Timeout.Infinite, token)));
        await Assert.ThrowsAsync<OperationCanceledException>(() => new GitHubReleaseSource(http).GetLatestAsync(cancelled.Token));
    }

    private sealed class FailingHandler(Func<CancellationToken, Task> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await send(cancellationToken);
            throw new InvalidOperationException("The handler should not complete.");
        }
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
