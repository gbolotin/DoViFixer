using System.Collections.Immutable;
using System.Text.Json.Serialization;
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
    public bool AutoSelectAfterScan
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
    public bool IncludeSimple
    {
        get;
        init;
    }
    public bool ForceComplex
    {
        get;
        init;
    }
    [JsonIgnore]
    public bool AllowFel
    {
        get => IncludeSimple || ForceComplex;
        init
        {
            IncludeSimple = value;
            ForceComplex = value;
        }
    }
    public AppTheme Theme
    {
        get;
        init;
    }
    = AppTheme.System;
}
