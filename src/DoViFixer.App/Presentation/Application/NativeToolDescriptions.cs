using DoViFixer.Application.Dependencies;

namespace DoViFixer.App.Presentation.Application;
/// <summary>Display names and purposes of the native tools, shown in Settings and the dependency setup review.</summary>
public static class NativeToolDescriptions
{
    private static readonly IReadOnlyDictionary<NativeTool, (string Name, string Purpose)> descriptions = new Dictionary<NativeTool, (string, string)>
    {
        [NativeTool.FFmpeg] = ("FFmpeg", "Extracts video and generates frame previews."),
        [NativeTool.FFprobe] = ("FFprobe", "Reads stream and frame information."),
        [NativeTool.MkvMerge] = ("mkvmerge (MKVToolNix)", "Builds the output MKV with video, audio, and subtitles."),
        [NativeTool.MkvExtract] = ("mkvextract (MKVToolNix)", "Extracts tracks for conversion, backup, and restoration."),
        [NativeTool.MediaInfo] = ("MediaInfo CLI", "Identifies media formats and Dolby Vision profiles."),
        [NativeTool.DoviTool] = ("dovi_tool", "Analyzes and converts Dolby Vision metadata and handles enhancement-layer data.")
    };

    public static string Summary { get; } = string.Join("\n", Enum.GetValues<NativeTool>().Select(tool => $"{Name(tool)} — {Purpose(tool)}"));

    public static string Name(NativeTool tool) => descriptions.TryGetValue(tool, out var description) ? description.Name : tool.ToString();

    public static string Purpose(NativeTool tool) => descriptions.TryGetValue(tool, out var description) ? description.Purpose : string.Empty;
}
