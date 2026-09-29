namespace DoViFixer.App.Presentation.Common;

public interface IInitializeAsync
{
    Task InitializeAsync(CancellationToken token = default);
}
