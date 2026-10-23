using System.Runtime.InteropServices;

namespace DoViFixer.App.Startup;

/// <summary>
/// Whether this process runs from the Microsoft Store package, which installs the app with its own identity. The
/// package then declares the File Explorer commands, and setup's and the zip's registrations do not apply.
/// </summary>
internal static class AppPackage
{
    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE

    public static bool IsStoreApp { get; } = HasPackageIdentity();

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != NoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
