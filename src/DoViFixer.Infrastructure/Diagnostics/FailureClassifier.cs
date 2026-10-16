using System.ComponentModel;
using DoViFixer.Application.Diagnostics;

namespace DoViFixer.Infrastructure.Diagnostics;

internal static class FailureClassifier
{
    public static FailureDiagnosis Describe(Exception exception, string operation, string? path = null, Guid? invocationId = null, bool startingTool = false)
    {
        if (FailureDiagnosis.FromException(exception) is { } known)
        {
            return known;
        }

        int? code = exception is Win32Exception win32 ? win32.NativeErrorCode
            : ((uint)exception.HResult & 0xFFFF0000) == 0x80070000 ? exception.HResult & 0xFFFF : null;
        var category = code switch
        {
            32 or 33 => FailureCategory.FileInUse,
            5 => FailureCategory.AccessDenied,
            39 or 112 => FailureCategory.DiskFull,
            2 or 3 => startingTool ? FailureCategory.MissingTool : FailureCategory.MissingFile,
            80 or 183 => FailureCategory.DestinationExists,
            _ => exception switch
            {
                UnauthorizedAccessException => FailureCategory.AccessDenied,
                TimeoutException => FailureCategory.Timeout,
                FileNotFoundException or DirectoryNotFoundException => startingTool ? FailureCategory.MissingTool : FailureCategory.MissingFile,
                InvalidDataException => FailureCategory.InvalidMedia,
                _ => FailureCategory.Unknown
            }
        };
        return new(category, operation, path, invocationId, exception.Message);
    }

    public static FailureDiagnosis ToolExit(string tool, int exitCode, string detail, Guid invocationId)
    {
        // Exit codes are tool-specific, not Win32 errors. Only unambiguous output phrases refine the diagnosis.
        var category = detail.Contains("No space left on device", StringComparison.OrdinalIgnoreCase)
            ? FailureCategory.DiskFull
            : detail.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
                ? FailureCategory.AccessDenied
                : detail.Contains("Invalid data found when processing input", StringComparison.OrdinalIgnoreCase)
                    ? FailureCategory.InvalidMedia : FailureCategory.NativeToolFailed;
        return new(category, tool, null, invocationId, $"Exited {exitCode}: {detail}");
    }
}
