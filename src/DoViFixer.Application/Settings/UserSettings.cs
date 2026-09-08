using System.Collections.Immutable;
using DoViFixer.Application.Dependencies;

namespace DoViFixer.Application.Settings;

public sealed record UserSettings
{
    public ImmutableDictionary<NativeTool, string> ToolPaths { get; init; } = ImmutableDictionary<NativeTool, string>.Empty;
    public string? TemporaryDirectory { get; init; }
}
