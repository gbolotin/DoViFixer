using System.Net.Http.Json;
using System.Text.Json;
using DoViFixer.Application.Updates;

namespace DoViFixer.Infrastructure.Updates;

internal sealed class UpstreamReleaseSource(HttpClient http) : IUpdateSource
{
    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        var response = await http.GetFromJsonAsync<JsonElement>("https://api.github.com/repos/cryptochrome/dovi_convert/releases/latest", linked.Token);
        string version = response.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Release has no version.");
        string url = response.GetProperty("html_url").GetString() ?? throw new InvalidDataException("Release has no URL.");
        return new(version, url, "cryptochrome/dovi_convert (upstream reference; not a DoViFixer binary update)");
    }
}
