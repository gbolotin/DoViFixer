namespace DoViFixer.Application.Conversion;

internal static class ConversionSpaceMargin
{
    public static long OutputBytes(long sourceBytes, double percent)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "Free-space margin must be between 0 and 200 percent.");
        }

        return checked(sourceBytes * 2 + (1L << 30) + checked((long)Math.Ceiling(sourceBytes * percent / 100)));
    }
}
