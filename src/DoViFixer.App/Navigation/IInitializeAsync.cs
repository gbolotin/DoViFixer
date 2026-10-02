namespace DoViFixer.App.Navigation;

public interface IInitializeAsync
{
    Task InitializeAsync(CancellationToken token = default);
}
