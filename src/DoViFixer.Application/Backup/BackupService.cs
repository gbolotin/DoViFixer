using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Backup;

public sealed record BackupPlan(Guid Id, MediaInfo Media, string Output, string? TemporaryDirectory, long ScratchBytes);
public sealed class BackupService(DependencyService dependencies, IMediaProbe probe, IFileOperations files,
    ITemporaryWorkspaceFactory workspaces, IVideoProcessor processor, IBackupArchiveStore archives,
    IOutputPublisher publisher, ISettingsStore settings, ILogger<BackupService> logger)
{
    public async Task<BackupPlan> PlanAsync(string input, string? outputDirectory, string? temporaryDirectory, CancellationToken cancellationToken)
    {
        await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
        await using var lease = await files.AcquireReadLeaseAsync(files.Identify(input), cancellationToken);
        var media = await probe.ProbeAsync(input, cancellationToken);
        if (media.Profile != DolbyVisionProfile.Profile7)
        {
            throw new InvalidOperationException("Enhancement-layer backup requires Profile 7.");
        }
        var snapshot = await settings.ReadAsync(cancellationToken);
        return new(Guid.NewGuid(), media, files.PrepareOutputPath(input, outputDirectory, ".dovi"),
            temporaryDirectory ?? snapshot.TemporaryDirectory, ConversionPolicy.RequiredScratchBytes(media.Source.Length));
    }

    public Task<FileResult> ExecuteAsync(BackupPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "Backup", approvedPlan.Id, approvedPlan.Media.Source.Path,
            async () =>
            {
                var result = await ExecuteCoreAsync(approvedPlan, progress, cancellationToken);
                OperationLog.Result(logger, result);
                return result;
            }, cancellationToken, result => result.Status);

    private async Task<FileResult> ExecuteCoreAsync(BackupPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
        await using var lease = await files.AcquireReadLeaseAsync(approvedPlan.Media.Source, cancellationToken);
        files.EnsureAvailableSpace(Path.GetDirectoryName(approvedPlan.Output)!, approvedPlan.Media.Source.Length + (1L << 30));
        await using var workspace = await workspaces.CreateAsync(approvedPlan.ScratchBytes, approvedPlan.TemporaryDirectory, cancellationToken);
        await using var staged = publisher.Stage(approvedPlan.Output);
        progress?.Report(new(approvedPlan.Id, "Extracting enhancement layer", approvedPlan.Media.Source.Path));
        var manifest = await processor.ExtractBackupAsync(approvedPlan.Media, workspace, cancellationToken);
        await archives.WriteAsync(staged.Path, manifest, workspace, cancellationToken);
        await staged.PublishAsync(cancellationToken);
        OperationLog.Audit(logger, "PublishBackup", approvedPlan.Output, "Completed", approvedPlan.Id);
        return new(approvedPlan.Media.Source.Path, OperationStatus.Completed, approvedPlan.Output, "Archive payload verified; original retained.");
    }
}
