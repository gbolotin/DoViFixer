namespace DoViFixer.Application.Updates;

public sealed record ReleaseInfo(string Version, string Url, string Product);
public interface IUpdateSource
{
    Task<ReleaseInfo> GetLatestAsync(CancellationToken cancellationToken);
}
public sealed class UpdateService(IUpdateSource source)
{
    public Task<ReleaseInfo> CheckAsync(CancellationToken cancellationToken) => source.GetLatestAsync(cancellationToken);
}
