using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Activity;
using DoViFixer.Application.Inspection;
using DoViFixer.Application.Operations;
using DoViFixer.Application.Settings;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Conversion;
using Microsoft.Extensions.Logging;

namespace DoViFixer.Application.WatchFolder;

/// <summary>Whether a file from the watch folder is converted, and its scan.</summary>
/// <param name="Analysis">The scan, or <see langword="null"/> when the file could not be scanned.</param>
public sealed record WatchedFileDecision(string Path, bool Accepted, MediaAnalysis? Analysis, string Reason);

/// <summary>
/// Decides whether a file that finished arriving in the watch folder is converted without asking. It scans the file
/// and accepts it when the conversion settings could convert it, the same test that offers Convert in the media list;
/// conversion then inspects it in full and applies the FEL settings as for any other file.
/// </summary>
public sealed class WatchFolderService(InspectionService inspection, IActivityFeed activity, ILogger<WatchFolderService> logger)
{
    public async Task<WatchedFileDecision> EvaluateAsync(string path, UserSettings settings, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        MediaAnalysis analysis;
        try
        {
            analysis = await inspection.ReadCachedAsync(path, AnalysisMethod.SampledRpu, cancellationToken)
                ?? await inspection.InspectAsync(path, AnalysisMethod.SampledRpu, null, cancellationToken, progress);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not scan {Input} from the watch folder", path);
            return Reject(path, null, $"The file could not be scanned: {ex.Message}", ActivitySeverity.Error);
        }

        if (!ConversionPolicy.MayAllow(analysis, settings.IncludeSimple, settings.ForceComplex))
        {
            return Reject(path, analysis, ConversionPolicy.Evaluate(analysis, settings.IncludeSimple, settings.ForceComplex).Reason, ActivitySeverity.Info);
        }

        OperationLog.Audit(logger, "AutoConvert", path, "StartedByWatchFolder");
        return new(path, true, analysis, analysis.Reason);
    }

    /// <summary>Records in Activity and the log that a file from the watch folder was left as it is, and why.</summary>
    public void ReportNotConverted(string path, string reason, ActivitySeverity severity)
    {
        OperationLog.Audit(logger, "AutoConvert", path, $"NotConverted: {reason}");
        activity.Report(ActivityText.NotAutoConverted(Guid.NewGuid(), path, reason, severity));
    }

    private WatchedFileDecision Reject(string path, MediaAnalysis? analysis, string reason, ActivitySeverity severity)
    {
        ReportNotConverted(path, reason, severity);
        return new(path, false, analysis, reason);
    }
}
