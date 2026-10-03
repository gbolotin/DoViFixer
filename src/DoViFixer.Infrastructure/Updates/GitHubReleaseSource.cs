using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoViFixer.Application.Updates;

namespace DoViFixer.Infrastructure.Updates;
/// <summary>Reads the latest published DoViFixer release from GitHub. Drafts and pre-releases are never "latest", so a repository without a published release answers 404.</summary>
internal sealed class GitHubReleaseSource(HttpClient http) : IUpdateSource
{
    internal const string LatestReleaseUrl = "https://api.github.com/repos/gbolotin/DoViFixer/releases/latest";

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var response = await http.GetAsync(LatestReleaseUrl, linked.Token);
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
}
