using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoViFixer.Application.Updates;

namespace DoViFixer.Infrastructure.Updates;
/// <summary>Reads the latest published DoViFixer release from GitHub. Drafts and pre-releases are never "latest", so a repository without a published release answers 404.</summary>
internal sealed class GitHubReleaseSource(HttpClient http) : IUpdateSource
{
    internal const string LatestReleaseUrl = "https://api.github.com/repos/gbolotin/DoViFixer/releases/latest";

    /// <summary>Caps the check, because the shared HttpClient allows 30 minutes for tool downloads.</summary>
    internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            using var response = await SendAsync(linked.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var release = await response.Content.ReadFromJsonAsync<JsonElement>(linked.Token);
            string version = release.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Release has no version.");
            string url = release.GetProperty("html_url").GetString() ?? throw new InvalidDataException("Release has no URL.");
            return new(version, url);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Without this, the console would report a slow or silent network as a user cancellation.
            throw new TimeoutException($"GitHub did not answer within {Timeout.TotalSeconds:0} seconds. Check your internet connection and try again.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetAsync(LatestReleaseUrl, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Could not reach GitHub to check for updates: {ex.Message}", ex);
        }
    }
}
