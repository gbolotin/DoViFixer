using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using DoViFixer.Domain.Media;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.Inspection;

public sealed class InspectionService(DependencyService dependencies, IMediaProbe probe,
    ITemporaryWorkspaceFactory workspaces, ISettingsStore settings, IFileOperations files, ILogger<InspectionService> logger)
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
        await dependencies.RequireAsync(DependencyRequirements.Analysis, cancellationToken);
        await using var lease = await files.AcquireReadLeaseAsync(files.Identify(path), cancellationToken);
        var media = await probe.ProbeAsync(path, cancellationToken);
        if (media.Profile != DolbyVisionProfile.Profile7)
        {
            return MediaClassifier.Classify(media, new(AnalysisMethod.MetadataOnly, EnhancementLayer.Unknown, 0, null, 0, 0));
        }
        var snapshot = await settings.ReadAsync(cancellationToken);
        long space = method is AnalysisMethod.FullRpu or AnalysisMethod.DeepInspection ? ConversionPolicy.RequiredScratchBytes(media.Source.Length) : 1L << 30;
        await using var workspace = await workspaces.CreateAsync(space, temporaryDirectory ?? snapshot.TemporaryDirectory, cancellationToken);
        var evidence = await probe.AnalyzeAsync(media, method, workspace, cancellationToken);
        return MediaClassifier.Classify(media, evidence);
    }
}
