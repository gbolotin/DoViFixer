using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Restore;

public sealed record RestorePlan(Guid Id, MediaInfo Media, FileIdentity Archive, string Output,
    string? TemporaryDirectory, long ScratchBytes, bool AllowLegacy);
public sealed class RestoreService(DependencyService dependencies, IMediaProbe probe, IFileOperations files,
    ITemporaryWorkspaceFactory workspaces, IVideoProcessor processor, IBackupArchiveStore archives,
    IMediaVerifier verifier, IOutputPublisher publisher, ISettingsStore settings)
{
    public async Task<RestorePlan> PlanAsync(string input, string archive, string? outputDirectory, string? temporaryDirectory,
        bool allowLegacy, CancellationToken cancellationToken)
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
        return new(Guid.NewGuid(), media, archiveIdentity, files.PrepareOutputPath(input, outputDirectory, ".restored.mkv"),
            temporaryDirectory ?? snapshot.TemporaryDirectory,
            checked(ConversionPolicy.RequiredScratchBytes(media.Source.Length) + archiveIdentity.Length * 3), allowLegacy);
    }

    public async Task<FileResult> ExecuteAsync(RestorePlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
        await using var lease = await files.AcquireReadLeaseAsync(approvedPlan.Media.Source, cancellationToken);
        await using var archiveLease = await files.AcquireReadLeaseAsync(approvedPlan.Archive, cancellationToken);
        files.EnsureAvailableSpace(Path.GetDirectoryName(approvedPlan.Output)!, checked(approvedPlan.Media.Source.Length + approvedPlan.Archive.Length + (1L << 30)));
        await using var workspace = await workspaces.CreateAsync(approvedPlan.ScratchBytes, approvedPlan.TemporaryDirectory, cancellationToken);
        await using var staged = publisher.Stage(approvedPlan.Output);
        progress?.Report(new(approvedPlan.Id, "Verifying archive and reconstructing Profile 7", approvedPlan.Media.Source.Path));
        var manifest = await archives.ReadAsync(approvedPlan.Archive.Path, workspace, approvedPlan.AllowLegacy, cancellationToken);
        await processor.RestoreAsync(approvedPlan.Media, manifest, workspace, staged.Path, cancellationToken);
        var findings = await verifier.VerifyAsync(approvedPlan.Media, staged.Path, DolbyVisionProfile.Profile7, workspace, cancellationToken);
        if (findings.Count > 0)
        {
            throw new InvalidDataException("Restoration verification failed: " + string.Join("; ", findings));
        }
        await staged.PublishAsync(cancellationToken);
        return new(approvedPlan.Media.Source.Path, OperationStatus.Completed, approvedPlan.Output,
            manifest is null ? "Restored media verified. Legacy archive source identity remains unverified." : "Pairing, payload and restored media verified. Original retained.");
    }
}
