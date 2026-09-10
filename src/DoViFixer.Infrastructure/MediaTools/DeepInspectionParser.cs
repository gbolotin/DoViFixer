using System.Globalization;
using System.Text.Json;
using DoViFixer.Domain.Analysis;
using DoViFixer.Infrastructure.MediaTools.DoviTool;

namespace DoViFixer.Infrastructure.MediaTools;

internal static class DeepInspectionParser
{
    // Linear BT.2020 Y is 0.2627 R + 0.6780 G + 0.0593 B. With npl=10000,
    // full-range 16-bit Y spans 0..10000 nits (about 0.153 nit quantization).
    // No resize, tone map, RPU rendering, or frame rate conversion is performed.
    internal static string Filter(string range) =>
        $"zscale=pin=bt2020:tin=smpte2084:min=bt2020nc:rin={range}:p=bt2020:t=linear:m=gbr:r=full:npl=10000," +
        "format=gbrpf32le,zscale=p=bt2020:t=linear:m=bt2020nc:r=full,format=yuv444p16le," +
        "signalstats,metadata=mode=print:key=lavfi.signalstats.YMAX:file=brightness.txt";

    internal static string ReadHdr10Range(string json)
    {
        using var document = JsonDocument.Parse(json);
        var streams = document.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() != 1)
        {
            throw new InvalidDataException("Deep inspection requires exactly one base-layer video stream.");
        }
        var stream = streams[0];
        if (MediaMetadataParser.Text(stream, "color_transfer") != "smpte2084" ||
            MediaMetadataParser.Text(stream, "color_primaries") != "bt2020" ||
            MediaMetadataParser.Text(stream, "color_space") != "bt2020nc")
        {
            throw new InvalidDataException("Deep inspection requires explicit PQ / BT.2020 non-constant-luminance base-layer signaling.");
        }
        return MediaMetadataParser.Text(stream, "color_range") switch
        {
            "tv" => "limited",
            "pc" => "full",
            _ => throw new InvalidDataException("Deep inspection requires an explicit base-layer color range.")
        };
    }

    internal static async Task<RpuEvidence> CompareAsync(Stream rpu, TextReader measurements, CancellationToken cancellationToken)
    {
        long compared = 0, expanded = 0, maxFrame = 0;
        double basePeak = 0, maxDelta = double.NegativeInfinity;
        const double threshold = 50;
        var evidence = await RpuParser.ParseAsync(rpu, AnalysisMethod.DeepInspection, cancellationToken, (index, pq) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pq is null)
            {
                throw new InvalidDataException($"RPU frame {index} lacks L1 peak metadata; deep comparison is incomplete.");
            }
            string? header = measurements.ReadLine();
            string? value = measurements.ReadLine();
            string expected = $"frame:{index.ToString(CultureInfo.InvariantCulture)}";
            if (header is null || !header.StartsWith(expected + " ", StringComparison.Ordinal) ||
                value is null || !value.StartsWith("lavfi.signalstats.YMAX=", StringComparison.Ordinal) ||
                !int.TryParse(value.AsSpan("lavfi.signalstats.YMAX=".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int code) ||
                code is < 0 or > 65535)
            {
                throw new InvalidDataException($"Missing, invalid, or misaligned base-layer measurement at frame {index}.");
            }
            double nits = code * (10000d / 65535);
            double delta = MediaClassifier.PqToNits(pq.Value) - nits;
            basePeak = Math.Max(basePeak, nits);
            if (delta > maxDelta)
            {
                maxDelta = delta;
                maxFrame = index;
            }
            if (delta > threshold)
            {
                expanded++;
            }
            compared++;
        });
        if (compared == 0 || measurements.ReadLine() is not null)
        {
            throw new InvalidDataException("Base-layer and RPU frame counts differ or are empty.");
        }
        return evidence with { Brightness = new(compared, expanded, basePeak, maxDelta, maxFrame, threshold) };
    }
}
