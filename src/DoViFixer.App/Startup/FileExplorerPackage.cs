using System.IO.Compression;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace DoViFixer.App.Startup;

/// <summary>
/// Registers, for the current user, the sparse package that lets Windows 11 show the app's commands in File Explorer's
/// menu. CI publishes it next to DoViFixer.App.exe. Only this project has the Windows Runtime projection that
/// registers packages, so the registration lives here rather than in Infrastructure.
/// </summary>
internal static class FileExplorerPackage
{
    public const string FileName = "DoViFixer.FileExplorer.msix";
    // As in src/DoViFixer.ShellExtension/Package/AppxManifest.xml.
    private const string Name = "gbolotin.DoViFixer.FileExplorer";
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    /// <summary>
    /// Registers the package for the app in <paramref name="folder"/> when <paramref name="show"/> is
    /// <see langword="true"/>, or removes it. Returns whether File Explorer shows the package's commands, so the
    /// classic commands can step aside. Before Windows 11, or without the package file, it registers nothing.
    /// Package registration waits on the deployment service, so it never runs on the UI thread.
    /// </summary>
    public static Task<bool> UpdateAsync(bool show, string folder) => Task.Run(() => UpdateCoreAsync(show, folder));

    private static async Task<bool> UpdateCoreAsync(bool show, string folder)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return false;
        }

        var manager = new PackageManager();
        var registered = manager.FindPackagesForUser("").Where(existing => existing.Id.Name == Name).ToList();
        string package = Path.Combine(folder, FileName);
        if (!show || !File.Exists(package))
        {
            foreach (var existing in registered)
            {
                await manager.RemovePackageAsync(existing.Id.FullName);
            }

            return false;
        }

        var version = ReadVersion(package);
        // Registering takes a moment, so a start that finds this version for this folder leaves it as it is.
        if (registered.Any(existing => IsVersion(existing.Id.Version, version) && IsFolder(existing.EffectiveExternalPath, folder)))
        {
            return true;
        }

        // Another version or folder, such as after an update or a move, is replaced; a lower version could not be added over it.
        foreach (var existing in registered)
        {
            await manager.RemovePackageAsync(existing.Id.FullName);
        }

        await manager.AddPackageByUriAsync(new Uri(package), new AddPackageOptions { ExternalLocationUri = new Uri(folder) });
        return true;
    }

    private static Version ReadVersion(string package)
    {
        using var archive = ZipFile.OpenRead(package);
        var entry = archive.GetEntry("AppxManifest.xml") ?? throw new InvalidDataException($"{package} has no manifest.");
        using var stream = entry.Open();
        string? value = XDocument.Load(stream).Root?.Element(Foundation + "Identity")?.Attribute("Version")?.Value;
        return Version.Parse(value ?? throw new InvalidDataException($"{package} has no version."));
    }

    private static bool IsVersion(Windows.ApplicationModel.PackageVersion registered, Version version) =>
        registered.Major == version.Major && registered.Minor == version.Minor && registered.Build == version.Build && registered.Revision == version.Revision;

    private static bool IsFolder(string? registered, string folder) =>
        registered is not null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(registered)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase);
}
