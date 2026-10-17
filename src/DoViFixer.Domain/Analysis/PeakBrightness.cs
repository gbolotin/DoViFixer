namespace DoViFixer.Domain.Analysis;

/// <summary>Selects a nearest-rank percentile from counts of 12-bit L1 peak values.</summary>
public static class PeakBrightness
{
    public static int? SelectPq(IReadOnlyList<long> counts, double percentile)
    {
        if (counts.Count != 4096)
        {
            throw new ArgumentException("Expected counts for all 4096 PQ code values.", nameof(counts));
        }
        if (!double.IsFinite(percentile) || percentile is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }
        long total = 0;
        foreach (long count in counts)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            total = checked(total + count);
        }
        if (total == 0)
        {
            return null;
        }
        long rank = Math.Max(1, (long)Math.Ceiling(total * percentile / 100d));
        long cumulative = 0;
        for (int pq = 0; pq < counts.Count; pq++)
        {
            cumulative += counts[pq];
            if (cumulative >= rank)
            {
                return pq;
            }
        }
        return null;
    }
}
