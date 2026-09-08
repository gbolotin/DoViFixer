using System.Collections.Concurrent;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Dependencies;

namespace DoViFixer.Infrastructure.Dependencies;

internal sealed class ToolCatalog : IToolCatalog
{
    private readonly ConcurrentDictionary<NativeTool, string> paths = new();
    public string GetPath(NativeTool tool) => paths.TryGetValue(tool, out string? path) ? path
        : throw new InvalidOperationException($"{tool} has not passed dependency validation.");
    public void Refresh(IEnumerable<DependencyStatus> statuses)
    {
        foreach (var status in statuses)
        {
            if (status.State == DependencyState.Ready && status.Path is not null)
            {
                paths[status.Tool] = status.Path;
            }
            else
            {
                paths.TryRemove(status.Tool, out _);
            }
        }
    }
}

internal static class ToolDefinitions
{
    internal static string Executable(NativeTool tool) => tool switch
    {
        NativeTool.FFmpeg => "ffmpeg.exe",
        NativeTool.FFprobe => "ffprobe.exe",
        NativeTool.MkvMerge => "mkvmerge.exe",
        NativeTool.MkvExtract => "mkvextract.exe",
        NativeTool.MediaInfo => "mediainfo.exe",
        NativeTool.DoviTool => "dovi_tool.exe",
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
    internal static int MinimumMajor(NativeTool tool) => tool switch
    {
        NativeTool.FFmpeg or NativeTool.FFprobe => 6,
        NativeTool.MkvMerge or NativeTool.MkvExtract => 80,
        NativeTool.MediaInfo => 24,
        NativeTool.DoviTool => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
}
