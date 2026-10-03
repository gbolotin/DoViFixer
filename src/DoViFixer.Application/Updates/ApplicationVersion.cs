using System.Reflection;

namespace DoViFixer.Application.Updates;
/// <summary>The version the build stamped into an assembly: <c>VersionPrefix</c> in Directory.Build.props, or the release tag in CI.</summary>
public static class ApplicationVersion
{
    public static string Of(Assembly assembly) => Format(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "unknown");

    /// <summary>Shortens the commit hash the SDK appends ("0.1.0-dev+&lt;40 hex digits&gt;") to seven digits, as git does.</summary>
    public static string Format(string informationalVersion)
    {
        int plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 || informationalVersion.Length - plus - 1 <= 7 ? informationalVersion : informationalVersion[..(plus + 8)];
    }
}
