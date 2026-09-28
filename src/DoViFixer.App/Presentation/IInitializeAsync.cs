namespace DoViFixer.App.Presentation;

public interface IInitializeAsync
{
    Task InitializeAsync(CancellationToken token = default);
}
