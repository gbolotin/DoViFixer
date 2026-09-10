using System.Text.Json;
using DoViFixer.Domain.Analysis;
using DoViFixer.Domain.Media;

namespace DoViFixer.Infrastructure.MediaTools.DoviTool;

internal static class RpuParser
{
    internal static async Task<RpuEvidence> ParseAsync(Stream stream, AnalysisMethod method, CancellationToken cancellationToken,
        Action<long, int?>? framePeak = null)
    {
        long frames = 0;
        int? maxPq = null;
        int? frameMaxPq = null;
        var layers = new HashSet<string>(StringComparer.Ordinal);
        bool unknownLayer = false;
        // dovi_tool exports a top-level RPU array; materialize at most one frame at a time.
        await foreach (var frame in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frame.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Invalid RPU frame entry.");
            }
            frames++;
            unknownLayer |= !frame.TryGetProperty("el_type", out var frameLayer) || frameLayer.GetString() is not ("MEL" or "FEL");
            frameMaxPq = null;
            Visit(frame, false);
            framePeak?.Invoke(frames - 1, frameMaxPq);
        }
        var layer = unknownLayer ? EnhancementLayer.Unknown : layers.SetEquals(new[] { "MEL" }) ? EnhancementLayer.Mel
            : layers.Contains("FEL") ? EnhancementLayer.Fel : EnhancementLayer.Unknown;
        return new(method, layer, frames, maxPq is int value ? MediaClassifier.PqToNits(value) : null, 1, 1);

        void Visit(JsonElement node, bool level1)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    if (property.Name == "el_type" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        layers.Add(property.Value.GetString()!);
                    }
                    if (level1 && property.Name == "max_pq")
                    {
                        int value = property.Value.GetInt32();
                        if (value is < 0 or > 4095)
                        {
                            throw new InvalidDataException("L1 max_pq is outside its 12-bit range.");
                        }
                        maxPq = Math.Max(maxPq ?? 0, value);
                        frameMaxPq = Math.Max(frameMaxPq ?? 0, value);
                    }
                    Visit(property.Value, level1 || property.Name.Equals("Level1", StringComparison.OrdinalIgnoreCase));
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray())
                {
                    Visit(child, level1);
                }
            }
        }
    }
}
