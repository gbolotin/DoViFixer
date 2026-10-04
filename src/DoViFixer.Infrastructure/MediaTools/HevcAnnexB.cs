using System.Buffers.Binary;
using System.Text.Json;

namespace DoViFixer.Infrastructure.MediaTools;
// Turns Matroska HEVC frames (NAL units with big-endian length prefixes, as ffmpeg's data muxer writes them)
// into an Annex B stream. FFmpeg's hevc_mp4toannexb filter also inserts the CodecPrivate parameter sets and SEI
// at keyframes, which changes the base layer; this writes every NAL unit unchanged, as mkvextract does.
internal static class HevcAnnexB
{
    private static readonly byte[] startCode = [0, 0, 0, 1];

    // The length-prefix size is lengthSizeMinusOne + 1, stored in byte 21 of the HEVC decoder configuration record.
    internal static int LengthSize(string identificationJson, int trackId)
    {
        using var identification = JsonDocument.Parse(identificationJson);
        var properties = identification.RootElement.GetProperty("tracks").EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == trackId).GetProperty("properties");
        string? codecPrivate = properties.TryGetProperty("codec_private_data", out var value) ? value.GetString() : null;
        if (codecPrivate is null || codecPrivate.Length < 46)
        {
            throw new InvalidDataException("The video track has no HEVC decoder configuration record.");
        }

        return 1 + (Convert.ToByte(codecPrivate.Substring(42, 2), 16) & 3);
    }

    internal static async Task CopyAsync(Stream input, Stream output, int lengthSize, CancellationToken cancellationToken)
    {
        // Not disposed: the caller owns both streams.
        var reader = new BufferedStream(input, 1024 * 1024);
        var writer = new BufferedStream(output, 1024 * 1024);
        var prefix = new byte[4];
        var buffer = new byte[1024 * 1024];
        while (true)
        {
            int read = await reader.ReadAtLeastAsync(prefix.AsMemory(4 - lengthSize, lengthSize), lengthSize, throwOnEndOfStream: false, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read < lengthSize)
            {
                throw new InvalidDataException("The video stream ends inside a NAL unit length.");
            }

            uint size = BinaryPrimitives.ReadUInt32BigEndian(prefix);
            if (size < 2)
            {
                throw new InvalidDataException($"The video stream contains an invalid NAL unit length of {size} bytes.");
            }

            await writer.WriteAsync(startCode, cancellationToken);
            for (long remaining = size; remaining > 0;)
            {
                int chunk = await reader.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                if (chunk == 0)
                {
                    throw new InvalidDataException("The video stream ends inside a NAL unit.");
                }

                await writer.WriteAsync(buffer.AsMemory(0, chunk), cancellationToken);
                remaining -= chunk;
            }
        }

        await writer.FlushAsync(cancellationToken);
    }
}
