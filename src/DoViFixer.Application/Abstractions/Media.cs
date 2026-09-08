using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Abstractions;

public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken);
    Task<RpuEvidence> AnalyzeAsync(MediaInfo media, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken);
}
public interface IVideoProcessor
{
    Task ConvertAsync(MediaInfo media, ConversionTarget target, ITemporaryWorkspace workspace, string stagedOutput,
        IProgress<OperationProgress>? progress, Guid operationId, CancellationToken cancellationToken);
    Task<ArchiveManifest> ExtractBackupAsync(MediaInfo media, ITemporaryWorkspace workspace, CancellationToken cancellationToken);
    Task RestoreAsync(MediaInfo media, ArchiveManifest? manifest, ITemporaryWorkspace workspace, string stagedOutput, CancellationToken cancellationToken);
}
public interface IMediaVerifier
{
    Task<IReadOnlyList<string>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile,
        ITemporaryWorkspace workspace, CancellationToken cancellationToken);
}
public sealed record ArchiveManifest(int FormatVersion, string SourceName, string BaseLayerSha256, string EnhancementLayerSha256,
    long EnhancementLayerLength, long? FrameCount, DateTimeOffset CreatedAt);
public interface IBackupArchiveStore
{
    Task WriteAsync(string stagedArchive, ArchiveManifest manifest, ITemporaryWorkspace workspace, CancellationToken cancellationToken);
    Task<ArchiveManifest?> ReadAsync(string archive, ITemporaryWorkspace workspace, bool allowLegacy, CancellationToken cancellationToken);
}
