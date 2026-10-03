namespace DoViFixer.Application.Updates;
public sealed record ReleaseInfo(string Version, string Url);

/// <summary>The running version, the latest published release (null when none is published yet), and whether that release is newer.</summary>
public sealed record UpdateCheckResult(string CurrentVersion, ReleaseInfo? Latest, bool IsUpdateAvailable);

public interface IUpdateSource
{
    /// <summary>Returns the latest published DoViFixer release, or null when no release has been published.</summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken);
}

public sealed class UpdateService(IUpdateSource source)
{
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        var latest = await source.GetLatestAsync(cancellationToken);
        bool newer = latest is not null && SemanticVersion.TryParse(latest.Version, out var released) && SemanticVersion.TryParse(currentVersion, out var current) && released.CompareTo(current) > 0;
        return new(currentVersion, latest, newer);
    }
}
