using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Abstractions;
public interface IAnalysisCache
{
    string RootDirectory { get; }
    Task<MediaAnalysis?> ReadAsync(FileIdentity source, AnalysisMethod method, CancellationToken cancellationToken);
    Task WriteAsync(MediaAnalysis analysis, CancellationToken cancellationToken);
    Task<string?> ReadBaseLayerHashAsync(FileIdentity source, CancellationToken cancellationToken);
    Task WriteBaseLayerHashAsync(FileIdentity source, string sha256, CancellationToken cancellationToken);
    Task<int> ClearAsync(CancellationToken cancellationToken);
    Task<long> GetSizeAsync(CancellationToken cancellationToken);
}
