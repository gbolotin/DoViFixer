using System.Globalization;
using DoViFixer.Application.Operations;

namespace DoViFixer.Infrastructure.MediaTools;
internal sealed class MkvProgress(IProgress<OperationProgress>? progress, Guid operationId, string stage, string file)
{
    private int lastPercent = -1;
    public void Report(string line)
    {
        if (TryParse(line, out int percent) && percent > lastPercent)
        {
            // Only the successful process exit can complete the stage.
            lastPercent = percent;
            progress?.Report(new(operationId, stage, file, Math.Min(percent, 99)));
        }
    }

    /// <summary>Reads the percentage from an MKVToolNix <c>--gui-mode</c> progress line.</summary>
    public static bool TryParse(string line, out int percent)
    {
        const string prefix = "#GUI#progress ";
        percent = 0;
        return line.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(line.AsSpan(prefix.Length).Trim().TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out percent) && percent is >= 0 and <= 100;
    }
}
