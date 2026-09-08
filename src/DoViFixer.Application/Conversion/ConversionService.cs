using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;

namespace DoViFixer.Application.Conversion;

public sealed record ConversionRequest(string Input, int RecursiveDepth = 0, ConversionTarget Target = ConversionTarget.Profile81,
    string? OutputDirectory = null, string? TemporaryDirectory = null, bool IncludeSimple = false,
    bool ForceComplex = false, bool CreateBackup = false);
public sealed record ConversionPlan(Guid Id, MediaAnalysis Analysis, ConversionTarget Target, string Output,
    string? Archive, string? TemporaryDirectory, long ScratchBytes, string Decision);
public sealed record ConversionPlanningResult(IReadOnlyList<ConversionPlan> Plans, IReadOnlyList<FileResult> Skipped);

public sealed class ConversionPlanner(IFileDiscovery discovery, IFileOperations files,
    InspectionService inspection, ISettingsStore settings)
{
    public async Task<ConversionPlanningResult> PlanAsync(ConversionRequest request,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var plans = new List<ConversionPlan>();
        var skipped = new List<FileResult>();
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = await settings.ReadAsync(cancellationToken);
        foreach (string path in discovery.Discover(request.Input, request.RecursiveDepth))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                progress?.Report(new(Guid.Empty, "Planning", path));
                var analysis = await inspection.InspectAsync(path, AnalysisMethod.FullRpu, request.TemporaryDirectory, cancellationToken);
                var decision = ConversionPolicy.Evaluate(analysis, request.IncludeSimple, request.ForceComplex);
                if (!decision.Allowed)
                {
                    skipped.Add(new(path, OperationStatus.Skipped, null, decision.Reason));
                    continue;
                }
                string output = files.PrepareOutputPath(path, request.OutputDirectory, request.Target == ConversionTarget.Profile81 ? ".dv81.mkv" : ".hdr10.mkv");
                string? archive = request.CreateBackup ? files.PrepareOutputPath(path, request.OutputDirectory, ".dovi") : null;
                if (!outputs.Add(output) || (archive is not null && !outputs.Add(archive)))
                {
                    throw new IOException("Multiple inputs resolve to the same output. Use separate output directories.");
                }
                long scratch = ConversionPolicy.RequiredScratchBytes(analysis.Media.Source.Length);
                plans.Add(new(Guid.NewGuid(), analysis, request.Target, output, archive,
                    request.TemporaryDirectory ?? snapshot.TemporaryDirectory, scratch, decision.Reason));
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not DependencyNotReadyException)
            {
                skipped.Add(new(path, OperationStatus.Failed, null, ex.Message));
            }
        }
        return new(plans.AsReadOnly(), skipped.AsReadOnly());
    }
}

public sealed class ConversionService(DependencyService dependencies, IFileOperations files, ITemporaryWorkspaceFactory workspaces,
    IVideoProcessor processor, IMediaVerifier verifier, IOutputPublisher publisher, IBackupArchiveStore archives)
{
    public async Task<FileResult> ExecuteAsync(ConversionPlan approvedPlan, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var media = approvedPlan.Analysis.Media;
        await dependencies.RequireAsync(DependencyRequirements.All, cancellationToken);
        await using var lease = await files.AcquireReadLeaseAsync(media.Source, cancellationToken);
        files.EnsureAvailableSpace(Path.GetDirectoryName(approvedPlan.Output)!, checked(media.Source.Length * 2 + (1L << 30)));
        await using var workspace = await workspaces.CreateAsync(approvedPlan.ScratchBytes, approvedPlan.TemporaryDirectory, cancellationToken);
        await using var staged = publisher.Stage(approvedPlan.Output);
        string archiveNote = "";
        if (approvedPlan.Archive is not null)
        {
            await using var stagedArchive = publisher.Stage(approvedPlan.Archive);
            progress?.Report(new(approvedPlan.Id, "Backing up enhancement layer", media.Source.Path));
            var manifest = await processor.ExtractBackupAsync(media, workspace, cancellationToken);
            await archives.WriteAsync(stagedArchive.Path, manifest, workspace, cancellationToken);
            await stagedArchive.PublishAsync(cancellationToken);
            archiveNote = $" Verified archive retained at {approvedPlan.Archive}.";
        }
        try
        {
            await processor.ConvertAsync(media, approvedPlan.Target, workspace, staged.Path, progress, approvedPlan.Id, cancellationToken);
            progress?.Report(new(approvedPlan.Id, "Verifying", media.Source.Path));
            var findings = await verifier.VerifyAsync(media, staged.Path,
                approvedPlan.Target == ConversionTarget.Profile81 ? DolbyVisionProfile.Profile81 : DolbyVisionProfile.None, workspace, cancellationToken);
            if (findings.Count > 0)
            {
                throw new InvalidDataException("Verification failed: " + string.Join("; ", findings));
            }
            await staged.PublishAsync(cancellationToken);
            return new(media.Source.Path, OperationStatus.Completed, approvedPlan.Output, "Verified output published. Original retained." + archiveNote);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(media.Source.Path, OperationStatus.Failed, null, ex.Message + archiveNote);
        }
    }
}

public sealed class BatchConversionService(ConversionService conversion)
{
    public async Task<BatchResult> ExecuteAsync(IReadOnlyList<ConversionPlan> approvedPlans,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        var results = new List<FileResult>();
        foreach (var plan in approvedPlans)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await conversion.ExecuteAsync(plan, progress, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                results.Add(new(plan.Analysis.Media.Source.Path, OperationStatus.Cancelled, null, "Cancelled; original retained."));
                break;
            }
            catch (Exception ex)
            {
                results.Add(new(plan.Analysis.Media.Source.Path, OperationStatus.Failed, null, ex.Message));
            }
        }
        return new(results.AsReadOnly());
    }
}
