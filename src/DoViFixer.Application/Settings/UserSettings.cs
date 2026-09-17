using System.Collections.Immutable;
using DoViFixer.Application.Dependencies;

namespace DoViFixer.Application.Settings;
public sealed record UserSettings
{
    public bool AutomaticallyScanAddedFiles
    {
        get;
        init;
    }
    = true;
    public bool UseCachedResults
    {
        get;
        init;
    }
    = true;
    public ImmutableDictionary<NativeTool, string> ToolPaths
    {
        get;
        init;
    }
    = ImmutableDictionary<NativeTool, string>.Empty;
    public string? TemporaryDirectory
    {
        get;
        init;
    }
    public string? OutputDirectory
    {
        get;
        init;
    }
    public bool ReplaceOriginal
    {
        get;
        init;
    }
    public bool CreateElArchive
    {
        get;
        init;
    }
}
