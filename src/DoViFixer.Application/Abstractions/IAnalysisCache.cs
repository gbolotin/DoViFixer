using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Abstractions;

public interface IAnalysisCache
{
    Task<MediaAnalysis?> ReadAsync(FileIdentity source, AnalysisMethod method, CancellationToken cancellationToken);
    Task WriteAsync(MediaAnalysis analysis, CancellationToken cancellationToken);
}
