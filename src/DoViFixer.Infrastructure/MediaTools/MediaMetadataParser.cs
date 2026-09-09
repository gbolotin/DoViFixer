using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoViFixer.Domain.Media;

namespace DoViFixer.Infrastructure.MediaTools;

internal static class MediaMetadataParser
{
    internal static MediaInfo Parse(FileIdentity source, string mkvJson, string mediaInfoJson)
    {
        using var mkv = JsonDocument.Parse(mkvJson);
        using var mi = JsonDocument.Parse(mediaInfoJson);
        var root = mkv.RootElement;
        if (Text(root.GetProperty("container"), "type") != "Matroska" ||
            (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() != 0))
        {
            throw new InvalidDataException("A recognized Matroska input without identification errors is required.");
        }
        if (!root.TryGetProperty("tracks", out var tracksJson) || !mi.RootElement.TryGetProperty("media", out var mediaRoot))
        {
            throw new InvalidDataException("Tool output lacks required media metadata.");
        }
        var tracks = tracksJson.EnumerateArray().Select(t =>
        {
            var p = t.GetProperty("properties");
            return new MediaTrack(t.GetProperty("id").GetInt32(), Text(t, "type"), Text(t, "codec"),
                Text(p, "language_ietf", Text(p, "language", "und")), Text(p, "track_name"),
                Bool(p, "default_track"), Bool(p, "forced_track"), Text(p, "uid"));
        }).ToArray();
        if (tracks.Count(t => t.Type == "video") != 1)
        {
            throw new InvalidDataException("Exactly one video track is required; multiple video tracks are not supported.");
        }
        var video = tracks.Single(t => t.Type == "video");
        var properties = tracksJson.EnumerateArray().Single(t => t.GetProperty("id").GetInt32() == video.Id).GetProperty("properties");
        var miVideo = mediaRoot.GetProperty("track").EnumerateArray().FirstOrDefault(t => Text(t, "@type") == "Video");
        if (miVideo.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("MediaInfo returned no video metadata.");
        }
        string hdr = Text(miVideo, "HDR_Format") + " " + Text(miVideo, "HDR_Format_Profile") + " " + Text(miVideo, "CodecID");
        // MediaInfo can return the level separately (dvhe.07 + HDR_Format_Level),
        // or include it in the profile string (dvhe.07.06).
        var match = Regex.Match(hdr, @"(?i)\bdv(?:he|h1)\.(\d{2})(?:\.\d{2})?(?=$|[\s/])");
        var profile = !hdr.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase) && !match.Success ? DolbyVisionProfile.None
            : match.Success ? match.Groups[1].Value switch
            {
                "07" => DolbyVisionProfile.Profile7,
                "05" => DolbyVisionProfile.Profile5,
                "08" when Text(miVideo, "HDR_Format_Compatibility").Contains("HDR10", StringComparison.OrdinalIgnoreCase) => DolbyVisionProfile.Profile81,
                _ => DolbyVisionProfile.Other
            } : DolbyVisionProfile.Unknown;
        var container = root.GetProperty("container").GetProperty("properties");
        return new(source, profile, Text(miVideo, "Format", video.Codec), video.Id,
            (int)(Number(miVideo, "Width") ?? 0), (int)(Number(miVideo, "Height") ?? 0),
            (long?)Number(miVideo, "FrameCount"), Number(miVideo, "FrameRate"), Number(miVideo, "Duration") ?? Number(container, "duration") / 1_000_000_000d,
            (long)(Number(properties, "minimum_timestamp") ?? 0), Number(miVideo, "MaxCLL"),
            Array.AsReadOnly(tracks), root.TryGetProperty("attachments", out var attachments) ? attachments.GetArrayLength() : 0,
            root.TryGetProperty("chapters", out var chapters) ? chapters.EnumerateArray().Sum(c => (int)(Number(c, "num_entries") ?? 0)) : 0,
            Text(container, "title"), mkvJson);
    }

    internal static string Text(JsonElement element, string name, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString() : fallback;
    internal static double? Number(JsonElement element, string name) =>
        double.TryParse(Text(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;
    internal static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
