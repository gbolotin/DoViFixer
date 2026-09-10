using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Inspection;

public sealed class InspectionService(DependencyService dependencies, IMediaProbe probe,
    ITemporaryWorkspaceFactory workspaces, ISettingsStore settings, IFileOperations files, ILogger<InspectionService> logger,
    IAnalysisCache cache)
{
    public Task<MediaAnalysis> InspectAsync(string path, AnalysisMethod method, string? temporaryDirectory, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "Inspect", Guid.NewGuid(), path, async () =>
        {
            var analysis = await InspectCoreAsync(path, method, temporaryDirectory, cancellationToken);
            logger.LogInformation("Analysis {Profile} {Verdict}; method {Method}; frames {Frames}; {Reason}",
                analysis.Media.Profile, analysis.Verdict, analysis.Evidence.Method, analysis.Evidence.Frames, analysis.Reason);
            return analysis;
        }, cancellationToken, result => result.Verdict == AnalysisVerdict.AnalysisFailed ? OperationStatus.Failed : OperationStatus.Completed);

    private async Task<MediaAnalysis> InspectCoreAsync(string path, AnalysisMethod method, string? temporaryDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = files.Identify(path);
        await using var lease = await files.AcquireReadLeaseAsync(source, cancellationToken);
        var cached = await cache.ReadAsync(source, method, cancellationToken);
        if (cached is null && method == AnalysisMethod.SampledRpu)
        {
            cached = await cache.ReadAsync(source, AnalysisMethod.FullRpu, cancellationToken);
        }
        cached ??= await cache.ReadAsync(source, AnalysisMethod.MetadataOnly, cancellationToken);
        if (cached is not null)
        {
            logger.LogInformation("Using cached {Method} analysis for {Input}", cached.Evidence.Method, source.Path);
            return cached with { Media = cached.Media with { Source = source } };
        }
        await dependencies.RequireAsync(DependencyRequirements.Analysis, cancellationToken);
        // Sampled evidence cannot authorize conversion, but its probe metadata can be reused.
        var previous = await cache.ReadAsync(source, AnalysisMethod.SampledRpu, cancellationToken)
            ?? await cache.ReadAsync(source, AnalysisMethod.FullRpu, cancellationToken)
            ?? await cache.ReadAsync(source, AnalysisMethod.DeepInspection, cancellationToken);
        var media = previous is null ? await probe.ProbeAsync(source.Path, cancellationToken)
            : previous.Media with { Source = source };
        if (media.Profile != DolbyVisionProfile.Profile7)
        {
            var metadata = MediaClassifier.Classify(media, new(AnalysisMethod.MetadataOnly, EnhancementLayer.Unknown, 0, null, 0, 0));
            await cache.WriteAsync(metadata, cancellationToken);
            return metadata;
        }
        var snapshot = await settings.ReadAsync(cancellationToken);
        long space = method is AnalysisMethod.FullRpu or AnalysisMethod.DeepInspection ? ConversionPolicy.RequiredScratchBytes(media.Source.Length) : 1L << 30;
        await using var workspace = await workspaces.CreateAsync(space, temporaryDirectory ?? snapshot.TemporaryDirectory, cancellationToken);
        var evidence = await probe.AnalyzeAsync(media, method, workspace, cancellationToken);
        var analysis = MediaClassifier.Classify(media, evidence);
        await cache.WriteAsync(analysis, cancellationToken);
        return analysis;
    }
}
