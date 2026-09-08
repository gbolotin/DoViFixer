using System.Text.Json;

namespace DoViFixer.Infrastructure.MediaTools;

internal static class MkvVideoMetadata
{
    private static readonly (string Property, string Option)[] options =
    [
        ("display_dimensions", "--display-dimensions"), ("stereo_mode", "--stereo-mode"), ("field_order", "--field-order"),
        ("enabled_track", "--track-enabled-flag"), ("flag_original", "--original-flag"), ("flag_commentary", "--commentary-flag"),
        ("flag_hearing_impaired", "--hearing-impaired-flag"), ("flag_visual_impaired", "--visual-impaired-flag"),
        ("flag_text_descriptions", "--text-descriptions-flag"),
        ("color_matrix_coefficients", "--color-matrix-coefficients"), ("color_bits_per_channel", "--color-bits-per-channel"),
        ("color_transfer_characteristics", "--color-transfer-characteristics"), ("color_primaries", "--color-primaries"),
        ("color_range", "--color-range"), ("max_content_light", "--max-content-light"), ("max_frame_light", "--max-frame-light"),
        ("chromaticity_coordinates", "--chromaticity-coordinates"), ("white_color_coordinates", "--white-color-coordinates"),
        ("max_luminance", "--max-luminance"), ("min_luminance", "--min-luminance"),
        ("projection_type", "--projection-type"), ("projection_private", "--projection-private"),
        ("projection_pose_yaw", "--projection-pose-yaw"), ("projection_pose_pitch", "--projection-pose-pitch"), ("projection_pose_roll", "--projection-pose-roll")
    ];

    internal static void AppendOptions(JsonElement properties, List<string> arguments)
    {
        if (MediaMetadataParser.Number(properties, "display_unit") is > 0)
        {
            throw new InvalidDataException("Non-pixel Matroska display units are not supported by this conversion path.");
        }
        if (properties.TryGetProperty("default_duration", out var duration) && duration.GetInt64() > 0)
        {
            arguments.Add("--default-duration");
            arguments.Add("0:" + duration + "ns");
        }
        foreach (var (property, option) in options)
        {
            if (properties.TryGetProperty(property, out var value))
            {
                arguments.Add(option);
                arguments.Add("0:" + (value.ValueKind is JsonValueKind.True or JsonValueKind.False ? (value.GetBoolean() ? "1" : "0") : value.ToString()));
            }
        }
        string[] crop = ["pixel_crop_left", "pixel_crop_top", "pixel_crop_right", "pixel_crop_bottom"];
        if (crop.Any(p => properties.TryGetProperty(p, out _)))
        {
            arguments.Add("--cropping");
            arguments.Add("0:" + string.Join(",", crop.Select(p => MediaMetadataParser.Text(properties, p, "0"))));
        }
    }

    internal static IEnumerable<string> Differences(JsonElement source, JsonElement target)
    {
        // External timestamps can make mkvmerge normalize DefaultDuration to its modal delta.
        // The verifier compares every actual timestamp and the frame count instead.
        foreach (string property in options.Select(p => p.Property).Concat(new[] { "display_unit", "pixel_crop_left", "pixel_crop_top", "pixel_crop_right", "pixel_crop_bottom" }))
        {
            if (source.TryGetProperty(property, out var expected) && (!target.TryGetProperty(property, out var actual) || expected.ToString() != actual.ToString()))
            {
                yield return property;
            }
        }
    }
}
