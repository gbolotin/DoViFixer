using System.Globalization;

namespace DoViFixer.Infrastructure.MediaTools;
internal static class FFmpegProgress
{
    /// <summary>Reads the decoded frame count from an FFmpeg <c>-progress</c> key=value line.</summary>
    public static bool TryParseFrame(string line, out long frame)
    {
        const string prefix = "frame=";
        frame = 0;
        return line.StartsWith(prefix, StringComparison.Ordinal) && long.TryParse(line.AsSpan(prefix.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out frame);
    }
}
