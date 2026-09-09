using System.Globalization;
using DoViFixer.Application.Operations;

namespace DoViFixer.Infrastructure.MediaTools;

internal sealed class MkvProgress(IProgress<OperationProgress>? progress, Guid operationId, string stage, string file)
{
    private int lastPercent = -1;

    public void Report(string line)
    {
        const string prefix = "#GUI#progress ";
        if (line.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(line.AsSpan(prefix.Length).Trim().TrimEnd('%'), NumberStyles.None, CultureInfo.InvariantCulture, out int percent) &&
            percent is >= 0 and <= 100 && percent > lastPercent)
        {
            // Only the successful process exit can complete the stage.
            lastPercent = percent;
            progress?.Report(new(operationId, stage, file, Math.Min(percent, 99)));
        }
    }
}
