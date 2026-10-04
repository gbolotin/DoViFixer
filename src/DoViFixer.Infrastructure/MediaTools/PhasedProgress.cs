using DoViFixer.Application.Operations;

namespace DoViFixer.Infrastructure.MediaTools;
/// <summary>Maps the progress of consecutive phases onto one stage percentage. Weights are relative shares of the stage.</summary>
internal sealed class PhasedProgress(IProgress<OperationProgress>? progress, Guid operationId, string stage, string file, params double[] weights)
{
    private readonly double total = weights.Sum();
    private int lastPercent = -1;
    public void Report(int phase, double fraction)
    {
        double completed = weights.Take(phase).Sum() + weights[phase] * Math.Clamp(fraction, 0, 1);
        // Only the caller can complete the stage, after its last phase has succeeded.
        int percent = Math.Min((int)(100 * completed / total), 99);
        if (percent > lastPercent)
        {
            lastPercent = percent;
            progress?.Report(new(operationId, stage, file, percent));
        }
    }
}
