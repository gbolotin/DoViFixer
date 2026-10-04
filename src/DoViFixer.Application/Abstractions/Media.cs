using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Abstractions;
public interface IMediaPreview
{
    Task<byte[]> LoadAsync(string path, CancellationToken cancellationToken);
    Task<int> ClearCacheAsync(CancellationToken cancellationToken);
    Task<long> GetCacheSizeAsync(CancellationToken cancellationToken);
}

public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken);
    Task<RpuEvidence> AnalyzeAsync(MediaInfo media, AnalysisMethod method, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null);
}

public interface IVideoProcessor
{
    Task ConvertAsync(MediaInfo media, ConversionTarget target, ITemporaryWorkspace workspace, string stagedOutput, IProgress<OperationProgress>? progress, Guid operationId, CancellationToken cancellationToken, bool safe = false);
    /// <summary>Reports the stage's percentage through <paramref name="progress"/> under <paramref name="stage"/>; the caller completes the stage.</summary>
    Task<ArchiveManifest> ExtractBackupAsync(MediaInfo media, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null, Guid operationId = default, string stage = "Backing up enhancement layer");
    Task RestoreAsync(MediaInfo media, ArchiveManifest? manifest, ITemporaryWorkspace workspace, string stagedOutput, CancellationToken cancellationToken);
}

/// <summary>What a verification finding concerns. Safe mode changes only how the video stream is extracted; the remux, and so the container, is the same.</summary>
public enum VerificationArea
{
    VideoStream,
    Container
}

public sealed record VerificationFinding(VerificationArea Area, string Message);
public interface IMediaVerifier
{
    Task<IReadOnlyList<VerificationFinding>> VerifyAsync(MediaInfo source, string output, DolbyVisionProfile expectedProfile, ITemporaryWorkspace workspace, CancellationToken cancellationToken, IProgress<OperationProgress>? progress = null, Guid operationId = default);
}

// Thrown when an archive's base-layer SHA-256 does not match the input; carries the input's actual hash.
// InvalidDataException is sealed, so this derives from Exception; RestoreService catches it by name.
public sealed class BaseLayerMismatchException(string actualSha256) : Exception("Archive does not belong to this base-layer video (SHA-256 mismatch).")
{
    public string ActualSha256 { get; } = actualSha256;
}

public sealed record ArchiveManifest(int FormatVersion, string SourceName, string BaseLayerSha256, string EnhancementLayerSha256, long EnhancementLayerLength, long? FrameCount, DateTimeOffset CreatedAt);
public interface IBackupArchiveStore
{
    Task<ArchiveManifest?> ReadManifestAsync(string archive, CancellationToken cancellationToken);
    Task WriteAsync(string stagedArchive, ArchiveManifest manifest, ITemporaryWorkspace workspace, CancellationToken cancellationToken);
    Task<ArchiveManifest?> ReadAsync(string archive, ITemporaryWorkspace workspace, bool allowLegacy, CancellationToken cancellationToken);
}
