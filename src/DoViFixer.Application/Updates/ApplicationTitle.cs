using System.Reflection;

namespace DoViFixer.Application.Updates;
/// <summary>The app name shown to users and sent to services: <c>AssemblyTitle</c> in Directory.Build.props, which every DoViFixer assembly carries.</summary>
public static class ApplicationTitle
{
    public static string Name { get; } = typeof(ApplicationTitle).Assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? typeof(ApplicationTitle).Assembly.GetName().Name ?? "unknown";
}
