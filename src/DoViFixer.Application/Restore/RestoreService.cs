using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Restore;
public sealed record RestorePlan(Guid Id, MediaInfo Media, FileIdentity Archive, string Output, string? TemporaryDirectory, long ScratchBytes, bool AllowLegacy);
public sealed class RestoreService(DependencyService dependencies, IMediaProbe probe, IFileOperations files, ITemporaryWorkspaceFactory workspaces, IVideoProcessor processor, IBackupArchiveStore archives, IMediaVerifier verifier, IOutputPublisher publisher, ISettingsStore settings, ILogger<RestoreService> logger, IFileDiscovery discovery, IAnalysisCache cache)
{
    // Starts archive pairing for one sequential batch from the current input-to-archive pairings.
    public RestoreArchivePairing BeginPairing(IEnumerable<(string Input, string Archive)> existingPairings) => new(this, existingPairings);

    // Pairing only reads cached hashes and archive manifests; it never extracts video. Without a cached
    // base-layer hash only a same-name archive with an agreeing frame count is offered. Restoration verifies
    // the SHA-256 pairing and caches the hash it learns, so a mismatched archive is not offered again.
    internal async Task<string?> FindArchiveAsync(MediaInfo media, IReadOnlySet<string> excludedArchives, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string input = media.Source.Path;
        string stem = Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input));
        const string convertedSuffix = " - DV P8.1";
        string[] preferred = stem.EndsWith(convertedSuffix, StringComparison.OrdinalIgnoreCase)
            ? [stem + ".dovi", stem[..^convertedSuffix.Length] + ".dovi"]
            : [stem + ".dovi"];
        var snapshot = await settings.ReadAsync(cancellationToken);
        string? baseHash = snapshot.UseCachedResults ? await cache.ReadBaseLayerHashAsync(media.Source, cancellationToken) : null;
        var candidates = discovery.Discover(Path.GetDirectoryName(input)!, 0, cleanup: true)
            .Where(path => path.EndsWith(".dovi", StringComparison.OrdinalIgnoreCase) && !excludedArchives.Contains(path))
            .Select(path => (Path: path, Rank: Array.FindIndex(preferred, candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))))
            .Where(candidate => baseHash is not null || candidate.Rank >= 0)
            .OrderBy(candidate => candidate.Rank >= 0 ? candidate.Rank : preferred.Length)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path);
        foreach (string candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveManifest? manifest;
            try
            {
                manifest = await archives.ReadManifestAsync(candidate, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Skipping unreadable restore archive {Archive}", candidate);
                continue;
            }

            if (manifest is not null && (baseHash is null ? FrameCountsAgree(media.FrameCount, manifest.FrameCount) : string.Equals(baseHash, manifest.BaseLayerSha256, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return null;
    }

    // Without a verified hash, reject an archive whose recorded frame count clearly differs. MediaInfo may estimate
    // counts from duration, so a small tolerance keeps the right archive from being hidden.
    private static bool FrameCountsAgree(long? mediaFrames, long? archiveFrames) =>
        mediaFrames is not { } media || archiveFrames is not { } archive || Math.Abs(media - archive) <= Math.Max(2, Math.Max(media, archive) / 1000);

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

        try
        {
            await processor.RestoreAsync(approvedPlan.Media, manifest, workspace, staged.Path, cancellationToken);
        }
        catch (BaseLayerMismatchException mismatch)
        {
            // Remember the input's real hash so archive pairing stops offering the mismatched archive.
            await cache.WriteBaseLayerHashAsync(approvedPlan.Media.Source, mismatch.ActualSha256, CancellationToken.None);
            throw;
        }

        if (manifest is not null)
        {
            // The processor verified the base layer against the manifest, so its hash is now known for pairing.
            await cache.WriteBaseLayerHashAsync(approvedPlan.Media.Source, manifest.BaseLayerSha256, CancellationToken.None);
        }

        var findings = await verifier.VerifyAsync(approvedPlan.Media, staged.Path, DolbyVisionProfile.Profile7, workspace, cancellationToken);
        if (findings.Count > 0)
        {
            throw new InvalidDataException("Restoration verification failed: " + string.Join("; ", findings.Select(f => f.Message)));
        }

        await staged.PublishAsync(cancellationToken);
        OperationLog.Audit(logger, "PublishRestore", approvedPlan.Output, "Completed", approvedPlan.Id);
        return new(approvedPlan.Media.Source.Path, OperationStatus.Completed, approvedPlan.Output, manifest is null ? "Restored media verified. Legacy archive source identity remains unverified." : "Pairing, payload and restored media verified. Original retained.");
    }
}

// Pairs Profile 8.1 files with restore archives so that each archive belongs to at most one file.
// Existing pairings stay claimed until their input is paired again, so inputs a batch never reaches keep theirs.
// Not thread-safe: use one instance per sequential batch.
public sealed class RestoreArchivePairing
{
    private readonly RestoreService restore;
    private readonly Dictionary<string, string> owners = new(StringComparer.OrdinalIgnoreCase);

    internal RestoreArchivePairing(RestoreService restore, IEnumerable<(string Input, string Archive)> existingPairings)
    {
        this.restore = restore;
        foreach (var (input, archive) in existingPairings)
        {
            owners[archive] = Path.GetFullPath(input);
        }
    }

    // Releases the input's previous archive, then pairs it with an archive no other input owns.
    public async Task<string?> PairAsync(MediaInfo media, CancellationToken cancellationToken)
    {
        string input = Path.GetFullPath(media.Source.Path);
        foreach (string released in owners.Where(owner => string.Equals(owner.Value, input, StringComparison.OrdinalIgnoreCase)).Select(owner => owner.Key).ToArray())
        {
            owners.Remove(released);
        }

        if (media.Profile != DolbyVisionProfile.Profile81)
        {
            return null;
        }

        string? archive = await restore.FindArchiveAsync(media, new HashSet<string>(owners.Keys, StringComparer.OrdinalIgnoreCase), cancellationToken);
        if (archive is not null)
        {
            owners[archive] = input;
        }

        return archive;
    }
}
