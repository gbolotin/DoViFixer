using System.Globalization;
using System.Security.Cryptography;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;
using DoViFixer.Domain.Analysis;
using DoViFixer.Infrastructure.MediaTools.Processes;

namespace DoViFixer.Infrastructure.MediaTools;

internal sealed record AccessUnit(int RpuCount, bool HasEnhancementLayer, int Slices);

internal static class RpuCoverage
{
    internal static async Task<MetadataFreeTail> VerifyAsync(string raw, string source, long expectedRpu, IToolCatalog tools, IProcessRunner processes, CancellationToken token, bool requireNoEnhancementLayer = false)
    {
        await using var stream = File.OpenRead(raw);
        var units = await ReadAsync(stream, token);
        if (requireNoEnhancementLayer && units.Any(u => u.HasEnhancementLayer))
        {
            throw new InvalidDataException("Converted output still contains enhancement-layer data.");
        }
        var timestamps = new List<long>();
        await processes.RunAsync(new(tools.GetPath(NativeTool.FFprobe), new[]
        {
            "-v", "error", "-select_streams", "v:0", "-show_packets", "-show_entries", "packet=pts", "-of", "csv=p=0", source
        }, OutputLine: line =>
        {
            if (!long.TryParse(line.Trim(), CultureInfo.InvariantCulture, out long pts))
            {
                throw new InvalidDataException("Cannot establish packet presentation order for RPU coverage.");
            }

            timestamps.Add(pts);
        }), token);
        return Validate(units, timestamps, expectedRpu);
    }

    internal static MetadataFreeTail Validate(IReadOnlyList<AccessUnit> units, IReadOnlyList<long> timestamps, long expectedRpu)
    {
        if (units.Count == 0 || units.Count != timestamps.Count || timestamps.Distinct().Count() != timestamps.Count)
        {
            throw new InvalidDataException("RPU coverage requires one access unit per uniquely timestamped video packet.");
        }

        long count = 0;
        bool tail = false;
        foreach (int index in Enumerable.Range(0, units.Count).OrderBy(i => timestamps[i]))
        {
            var unit = units[index];
            if (unit.Slices != 1 || unit.RpuCount is < 0 or > 1)
            {
                throw new InvalidDataException("Unsupported or duplicate frame/RPU structure; coverage cannot be verified.");
            }

            if (unit.RpuCount == 0)
            {
                tail = true;
                if (unit.HasEnhancementLayer)
                {
                    throw new InvalidDataException("Enhancement-layer data exists in a frame without RPU metadata.");
                }
            }
            else
            {
                if (tail)
                {
                    throw new InvalidDataException("RPU metadata has a leading or internal gap, not a metadata-free ending.");
                }

                count++;
            }
        }

        if (count == 0 || count != expectedRpu)
        {
            throw new InvalidDataException("Parsed RPU count does not match the verified per-frame metadata map.");
        }

        string hash = Convert.ToHexString(SHA256.HashData(units.Select(u => (byte)u.RpuCount).ToArray()));
        return new(units.Count, count, hash);
    }

    // Read only the first three bytes of each Annex B NAL; video payload is never retained.
    internal static async Task<IReadOnlyList<AccessUnit>> ReadAsync(Stream stream, CancellationToken token, bool countRpuOnly = false)
    {
        var units = new List<AccessUnit>();
        var buffer = new byte[1024 * 1024];
        var header = new byte[3];
        int headerLength = 0;
        int zeros = 0;
        bool inNal = false;
        bool active = false;
        int rpu = 0;
        bool el = false;
        int slices = 0;
        void FinishNal()
        {
            if (!inNal)
            {
                return;
            }

            if (headerLength < 2 || (header[0] & 128) != 0 || (header[1] & 7) == 0)
            {
                throw new InvalidDataException("Invalid HEVC NAL header in RPU coverage scan.");
            }

            int type = (header[0] >> 1) & 63;
            if (countRpuOnly)
            {
                if (type == 62)
                {
                    units.Add(new(1, false, 0));
                }

                return;
            }

            if (type == 35)
            {
                if (active)
                {
                    units.Add(new(rpu, el, slices));
                }

                active = true;
                rpu = 0;
                el = false;
                slices = 0;
            }
            else if (type <= 31 || type is 62 or 63)
            {
                if (!active)
                {
                    throw new InvalidDataException("Access-unit delimiters are required to verify metadata-free endings.");
                }

                if (type == 62)
                {
                    rpu++;
                }
                else if (type == 63)
                {
                    el = true;
                }
                else
                {
                    if (headerLength < 3 || (header[2] & 128) == 0)
                    {
                        throw new InvalidDataException("Unsupported multi-slice picture in RPU coverage scan.");
                    }

                    slices++;
                }
            }
        }

        int length;
        while ((length = await stream.ReadAsync(buffer, token)) != 0)
        {
            for (int i = 0; i < length; i++)
            {
                byte value = buffer[i];
                if (value == 1 && zeros >= 2)
                {
                    FinishNal();
                    inNal = true;
                    headerLength = 0;
                    zeros = 0;
                    continue;
                }

                if (inNal && headerLength < header.Length)
                {
                    header[headerLength++] = value;
                }

                zeros = value == 0 ? Math.Min(zeros + 1, 3) : 0;
            }
        }

        FinishNal();
        if (active)
        {
            units.Add(new(rpu, el, slices));
        }

        return units;
    }
}
