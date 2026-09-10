using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Conversion;

public sealed record ConversionRequest(string Input, int RecursiveDepth = 0, ConversionTarget Target = ConversionTarget.Profile81,
    string? OutputDirectory = null, string? TemporaryDirectory = null, bool IncludeSimple = false,
    bool ForceComplex = false, bool CreateBackup = false, bool Safe = false, bool DeleteBackup = false,
    IReadOnlyList<string>? AdditionalInputs = null);
public sealed record ConversionPlan(Guid Id, MediaAnalysis Analysis, ConversionTarget Target, string Output,
    string? Archive, string? TemporaryDirectory, long ScratchBytes, string Decision, bool Safe = false, bool DeleteBackup = false);
public sealed record ConversionPlanningResult(IReadOnlyList<ConversionPlan> Plans, IReadOnlyList<FileResult> Skipped);

public sealed class ConversionPlanner(IFileDiscovery discovery, IFileOperations files,
    InspectionService inspection, ISettingsStore settings, ILogger<ConversionPlanner> logger)
{
    public Task<ConversionPlanningResult> PlanAsync(ConversionRequest request,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken,
        Func<MediaAnalysis, CancellationToken, Task<bool>>? approveFel = null) =>
        OperationLog.RunAsync(logger, "PlanConversion", Guid.NewGuid(), request.Input,
            () => PlanCoreAsync(request, progress, cancellationToken, approveFel), cancellationToken,
            result => result.Skipped.Any(f => f.Status == OperationStatus.Failed)
                ? (result.Plans.Count > 0 ? OperationStatus.Partial : OperationStatus.Failed)
                : (result.Plans.Count > 0 ? OperationStatus.Completed : OperationStatus.Skipped));

    private async Task<ConversionPlanningResult> PlanCoreAsync(ConversionRequest request,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken,
        Func<MediaAnalysis, CancellationToken, Task<bool>>? approveFel)
    {
        var plans = new List<ConversionPlan>();
        var skipped = new List<FileResult>();
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = await settings.ReadAsync(cancellationToken);
        var inputs = new[] { request.Input }.Concat(request.AdditionalInputs ?? []);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                paths.UnionWith(discovery.Discover(input, request.RecursiveDepth));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                skipped.Add(new(input, OperationStatus.Failed, null, ex.Message));
                OperationLog.Result(logger, skipped[^1]);
            }
        }
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                progress?.Report(new(Guid.Empty, "Planning", path));
                var analysis = await inspection.InspectAsync(path, AnalysisMethod.FullRpu, request.TemporaryDirectory, cancellationToken);
                var decision = ConversionPolicy.Evaluate(analysis, request.IncludeSimple, request.ForceComplex);
                if (!decision.Allowed && approveFel is not null &&
                    analysis.Verdict is AnalysisVerdict.SimpleFel or AnalysisVerdict.ComplexFel &&
                    ConversionPolicy.Evaluate(analysis, true, true).Allowed &&
                    await approveFel(analysis, cancellationToken))
                {
                    decision = ConversionPolicy.Evaluate(analysis, true, true);
                }
                if (!decision.Allowed)
                {
                    skipped.Add(new(path, OperationStatus.Skipped, null, decision.Reason));
                    OperationLog.Result(logger, skipped[^1]);
                    continue;
                }
                string suffix = request.DeleteBackup ? Path.GetExtension(path)
                    : request.Target == ConversionTarget.Profile81 ? " - DV P8.1.mkv" : " - HDR10.mkv";
                string output = files.PrepareOutputPath(path, request.OutputDirectory, suffix, allowInput: request.DeleteBackup);
                string? originalBackup = request.DeleteBackup
                    ? files.PrepareOutputPath(path, null, Path.GetExtension(path) + ".bak.dovi_convert") : null;
                string? archive = request.CreateBackup ? files.PrepareOutputPath(path, request.OutputDirectory, ".dovi") : null;
                if (!outputs.Add(output) || (originalBackup is not null && !outputs.Add(originalBackup)) || (archive is not null && !outputs.Add(archive)))
                {
                    throw new IOException("Multiple inputs resolve to the same output. Use separate output directories.");
                }
                long scratch = ConversionPolicy.RequiredScratchBytes(analysis.Media.Source.Length);
                plans.Add(new(Guid.NewGuid(), analysis, request.Target, output, archive,
                    request.TemporaryDirectory ?? snapshot.TemporaryDirectory, scratch, decision.Reason, request.Safe, request.DeleteBackup));
                logger.LogInformation("Prepared plan {PlanId}: {Input} to {Output}; target {Target}; archive {Archive}; scratch {ScratchBytes}; force {ForceComplex}; include simple {IncludeSimple}; {Decision}",
                    plans[^1].Id, path, output, request.Target, archive, scratch, request.ForceComplex, request.IncludeSimple, decision.Reason);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not DependencyNotReadyException)
            {
                logger.LogError(ex, "Conversion planning failed for {Input}", path);
                skipped.Add(new(path, OperationStatus.Failed, null, ex.Message));
                OperationLog.Result(logger, skipped[^1]);
            }
        }
        return new(plans.AsReadOnly(), skipped.AsReadOnly());
    }
}

public sealed class ConversionService(DependencyService dependencies, IFileOperations files, ITemporaryWorkspaceFactory workspaces,
    IVideoProcessor processor, IMediaVerifier verifier, IOutputPublisher publisher, IBackupArchiveStore archives, ILogger<ConversionService> logger)
{
    public Task<FileResult> ExecuteAsync(ConversionPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "Convert", approvedPlan.Id, approvedPlan.Analysis.Media.Source.Path,
            async () =>
            {
                var result = await ExecuteCoreAsync(approvedPlan, progress, cancellationToken);
                OperationLog.Result(logger, result);
                return result;
            }, cancellationToken, result => result.Status);

    private async Task<FileResult> ExecuteCoreAsync(ConversionPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var media = approvedPlan.Analysis.Media;
        logger.LogInformation("Executing plan {PlanId}: target {Target}; output {Output}; archive {Archive}; scratch {ScratchBytes}; temporary directory {TemporaryDirectory}; {Decision}",
            approvedPlan.Id, approvedPlan.Target, approvedPlan.Output, approvedPlan.Archive, approvedPlan.ScratchBytes, approvedPlan.TemporaryDirectory, approvedPlan.Decision);
        await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
        files.EnsureAvailableSpace(Path.GetDirectoryName(approvedPlan.Output)!, checked(media.Source.Length * 2 + (1L << 30)));
        await using var workspace = await workspaces.CreateAsync(approvedPlan.ScratchBytes, approvedPlan.TemporaryDirectory, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!approvedPlan.DeleteBackup && string.Equals(Path.GetFullPath(approvedPlan.Output), media.Source.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Keeping the original requires a separate output path.");
        }
        var backupIdentity = media.Source;
        if (approvedPlan.DeleteBackup)
        {
            backupIdentity = files.RenameOriginal(media.Source);
            OperationLog.Audit(logger, "RenameOriginal", backupIdentity.Path, "Completed", approvedPlan.Id);
            media = media with { Source = backupIdentity };
        }
        string archiveNote = "";
        bool published = false;
        try
        {
            await using (var lease = await files.AcquireReadLeaseAsync(media.Source, cancellationToken))
            {
                if (approvedPlan.Archive is not null)
                {
                    await using var stagedArchive = publisher.Stage(approvedPlan.Archive);
                    progress?.Report(new(approvedPlan.Id, "Backing up enhancement layer", media.Source.Path));
                    var manifest = await processor.ExtractBackupAsync(media, workspace, cancellationToken);
                    manifest = manifest with { SourceName = Path.GetFileName(approvedPlan.Analysis.Media.Source.Path) };
                    await archives.WriteAsync(stagedArchive.Path, manifest, workspace, cancellationToken);
                    await stagedArchive.PublishAsync(cancellationToken);
                    OperationLog.Audit(logger, "PublishBackup", approvedPlan.Archive, "Completed", approvedPlan.Id);
                    archiveNote = $" Verified archive retained at {approvedPlan.Archive}.";
                }
                for (int attempt = 0; ; attempt++)
                {
                    await using var staged = publisher.Stage(approvedPlan.Output);
                    bool safe = approvedPlan.Safe || attempt > 0;
                    try
                    {
                        await processor.ConvertAsync(media, approvedPlan.Target, workspace, staged.Path, progress, approvedPlan.Id, cancellationToken, safe);
                        progress?.Report(new(approvedPlan.Id, "Verifying", media.Source.Path));
                        var findings = await verifier.VerifyAsync(media, staged.Path,
                            approvedPlan.Target == ConversionTarget.Profile81 ? DolbyVisionProfile.Profile81 : DolbyVisionProfile.None,
                            workspace, cancellationToken, progress, approvedPlan.Id);
                        if (findings.Count > 0)
                        {
                            throw new InvalidDataException("Verification failed: " + string.Join("; ", findings));
                        }
                    }
                    catch (Exception ex) when (!safe && ex is (IOException or InvalidDataException))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        logger.LogWarning(ex, "Standard conversion failed verification or processing; retrying in safe mode");
                        progress?.Report(new(approvedPlan.Id, "Retrying with safe disk extraction", media.Source.Path));
                        continue;
                    }
                    progress?.Report(new(approvedPlan.Id, "Verifying", media.Source.Path, 100));
                    await staged.PublishAsync(cancellationToken);
                    published = true;
                    OperationLog.Audit(logger, "PublishConversion", approvedPlan.Output, "Completed", approvedPlan.Id);
                    break;
                }
            }
            if (approvedPlan.DeleteBackup)
            {
                await files.DeleteAsync(backupIdentity, cancellationToken);
                OperationLog.Audit(logger, "DeleteOriginalBackup", backupIdentity.Path, "Completed", approvedPlan.Id);
            }
            return new(approvedPlan.Analysis.Media.Source.Path, OperationStatus.Completed, approvedPlan.Output,
                "Verified output published. " + (approvedPlan.DeleteBackup ? "Original backup deleted." : $"Original retained at {backupIdentity.Path}.") + archiveNote);
        }
        catch (Exception ex)
        {
            if (published)
            {
                logger.LogWarning(ex, "Conversion verified and published, but original backup cleanup did not complete: {BackupPath}", backupIdentity.Path);
                return new(approvedPlan.Analysis.Media.Source.Path, OperationStatus.Partial, approvedPlan.Output,
                    $"Conversion verified and published. Original backup cleanup did not complete: {ex.Message} Original retained at {backupIdentity.Path}." + archiveNote);
            }
            string recovery = $" Original retained at {backupIdentity.Path}.";
            if (approvedPlan.DeleteBackup)
            {
                try
                {
                    files.RestoreOriginal(backupIdentity, approvedPlan.Analysis.Media.Source.Path);
                    recovery = $" Original restored to {approvedPlan.Analysis.Media.Source.Path}.";
                    OperationLog.Audit(logger, "RestoreOriginal", approvedPlan.Analysis.Media.Source.Path, "Completed", approvedPlan.Id);
                }
                catch (Exception recoveryException)
                {
                    recovery += $" Automatic recovery failed: {recoveryException.Message}";
                    logger.LogError(recoveryException, "Could not restore original from {BackupPath}", backupIdentity.Path);
                }
            }
            logger.LogError(ex, "Conversion failed: {Reason}{ArchiveNote}", ex.Message, archiveNote);
            return new(approvedPlan.Analysis.Media.Source.Path,
                ex is OperationCanceledException ? OperationStatus.Cancelled : OperationStatus.Failed,
                published ? approvedPlan.Output : null, ex.Message + recovery + archiveNote);
        }
    }
}

public sealed class BatchConversionService(ConversionService conversion, ILogger<BatchConversionService> logger)
{
    public async Task<BatchResult> ExecuteAsync(IReadOnlyList<ConversionPlan> approvedPlans,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["BatchId"] = id, ["FileCount"] = approvedPlans.Count });
        return await OperationLog.RunAsync(logger, "BatchConversion", id, null,
            () => ExecuteCoreAsync(approvedPlans, progress, cancellationToken), cancellationToken, result => result.Status);
    }

    private async Task<BatchResult> ExecuteCoreAsync(IReadOnlyList<ConversionPlan> approvedPlans,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var results = new List<FileResult>();
        foreach (var plan in approvedPlans)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await conversion.ExecuteAsync(plan, progress, cancellationToken));
                if (results[^1].Status == OperationStatus.Cancelled)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results.Add(new(plan.Analysis.Media.Source.Path, OperationStatus.Cancelled, null, "Cancelled; original retained."));
                OperationLog.Result(logger, results[^1]);
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Batch item {PlanId} failed for {Input}", plan.Id, plan.Analysis.Media.Source.Path);
                results.Add(new(plan.Analysis.Media.Source.Path, OperationStatus.Failed, null, ex.Message));
                OperationLog.Result(logger, results[^1]);
            }
        }
        return new(results.AsReadOnly());
    }
}
