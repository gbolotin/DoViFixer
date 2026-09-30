using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DoViFixer.Application.Restore;
public sealed record RestorePlan(Guid Id, MediaInfo Media, FileIdentity Archive, string Output, string? TemporaryDirectory, long ScratchBytes, bool AllowLegacy);
public sealed class RestoreService(DependencyService dependencies, IMediaProbe probe, IFileOperations files, ITemporaryWorkspaceFactory workspaces, IVideoProcessor processor, IBackupArchiveStore archives, IMediaVerifier verifier, IOutputPublisher publisher, ISettingsStore settings, ILogger<RestoreService> logger, IFileDiscovery discovery, IAnalysisCache cache)
{
    public async Task<string?> FindArchiveAsync(MediaInfo media, IReadOnlySet<string> excludedArchives, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string input = media.Source.Path;
        string stem = Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input));
        const string convertedSuffix = " - DV P8.1";
        string[] preferred = stem.EndsWith(convertedSuffix, StringComparison.OrdinalIgnoreCase)
            ? [stem + ".dovi", stem[..^convertedSuffix.Length] + ".dovi"]
            : [stem + ".dovi"];
        var candidates = discovery.Discover(Path.GetDirectoryName(input)!, 0, cleanup: true)
            .Where(path => path.EndsWith(".dovi", StringComparison.OrdinalIgnoreCase) && !excludedArchives.Contains(path))
            .OrderBy(path =>
            {
                int index = Array.FindIndex(preferred, candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
                return index >= 0 ? index : preferred.Length;
            })
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
        string? baseHash = null;
        foreach (string candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveManifest? manifest;
            try
            {
                manifest = await archives.ReadManifestAsync(candidate, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                logger.LogDebug(ex, "Skipping unreadable restore archive {Archive}", candidate);
                continue;
            }

            if (manifest is null)
            {
                continue;
            }

            if (baseHash is null)
            {
                var snapshot = await settings.ReadAsync(cancellationToken);
                await using var lease = await files.AcquireReadLeaseAsync(media.Source, cancellationToken);
                baseHash = snapshot.UseCachedResults ? await cache.ReadBaseLayerHashAsync(media.Source, cancellationToken) : null;
                if (baseHash is null)
                {
                    await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
                    await using var workspace = await workspaces.CreateAsync(ConversionPolicy.RequiredScratchBytes(media.Source.Length), snapshot.TemporaryDirectory, cancellationToken);
                    progress?.Report(new(Guid.Empty, "Matching .dovi archives by SHA-256", input));
                    baseHash = await processor.GetBaseLayerSha256Async(media, workspace, cancellationToken);
                    await cache.WriteBaseLayerHashAsync(media.Source, baseHash, cancellationToken);
                }
            }

            if (string.Equals(baseHash, manifest.BaseLayerSha256, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<RestorePlan> PlanAsync(string input, string archive, string? outputDirectory, string? temporaryDirectory, bool allowLegacy, CancellationToken cancellationToken)
    {
        await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
        await using var lease = await files.AcquireReadLeaseAsync(files.Identify(input), cancellationToken);
        var media = await probe.ProbeAsync(input, cancellationToken);
        if (media.Profile is not (DolbyVisionProfile.Profile81 or DolbyVisionProfile.None) || !media.VideoCodec.Contains("HEVC", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Restore requires an HEVC Profile 8.1 or HDR10 base file.");
        }

        var archiveIdentity = files.Identify(archive);
        var snapshot = await settings.ReadAsync(cancellationToken);
        return new(Guid.NewGuid(), media, archiveIdentity, files.PrepareOutputPath(input, outputDirectory, ".restored.mkv"), temporaryDirectory ?? snapshot.TemporaryDirectory, checked(ConversionPolicy.RequiredScratchBytes(media.Source.Length) + archiveIdentity.Length * 3), allowLegacy);
    }

    public Task<OperationItemResult> ExecuteAsync(RestorePlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) => OperationLog.RunAsync(logger, "Restore", approvedPlan.Id, approvedPlan.Media.Source.Path, async () =>
    {
        var result = await ExecuteCoreAsync(approvedPlan, progress, cancellationToken);
        OperationLog.Result(logger, result);
        return result;
    }, cancellationToken, result => result.Status);
    private async Task<OperationItemResult> ExecuteCoreAsync(RestorePlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("Restoring archive {Archive} to {Output}; allow legacy {AllowLegacy}", approvedPlan.Archive.Path, approvedPlan.Output, approvedPlan.AllowLegacy);
        await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
        await using var lease = await files.AcquireReadLeaseAsync(approvedPlan.Media.Source, cancellationToken);
        await using var archiveLease = await files.AcquireReadLeaseAsync(approvedPlan.Archive, cancellationToken);
        files.EnsureAvailableSpace(Path.GetDirectoryName(approvedPlan.Output)!, checked(approvedPlan.Media.Source.Length + approvedPlan.Archive.Length + (1L << 30)));
        await using var workspace = await workspaces.CreateAsync(approvedPlan.ScratchBytes, approvedPlan.TemporaryDirectory, cancellationToken);
        await using var staged = publisher.Stage(approvedPlan.Output);
        progress?.Report(new(approvedPlan.Id, "Verifying archive and reconstructing Profile 7", approvedPlan.Media.Source.Path));
        var manifest = await archives.ReadAsync(approvedPlan.Archive.Path, workspace, approvedPlan.AllowLegacy, cancellationToken);
        if (manifest is null)
        {
            OperationLog.Audit(logger, "UseLegacyArchive", approvedPlan.Archive.Path, "SourcePairingUnverified", approvedPlan.Id);
        }

        await processor.RestoreAsync(approvedPlan.Media, manifest, workspace, staged.Path, cancellationToken);
        var findings = await verifier.VerifyAsync(approvedPlan.Media, staged.Path, DolbyVisionProfile.Profile7, workspace, cancellationToken);
        if (findings.Count > 0)
        {
            throw new InvalidDataException("Restoration verification failed: " + string.Join("; ", findings));
        }

        await staged.PublishAsync(cancellationToken);
        OperationLog.Audit(logger, "PublishRestore", approvedPlan.Output, "Completed", approvedPlan.Id);
        return new(approvedPlan.Media.Source.Path, OperationStatus.Completed, approvedPlan.Output, manifest is null ? "Restored media verified. Legacy archive source identity remains unverified." : "Pairing, payload and restored media verified. Original retained.");
    }
}
