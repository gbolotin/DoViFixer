using System.Runtime.InteropServices;

namespace DoViFixer.Infrastructure.FileSystem;

internal static class RecycleBin
{
    public static void Send(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Recycle Bin requires Windows.");
        }

        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).DriveType != DriveType.Fixed)
        {
            throw new IOException("The Recycle Bin is unavailable for this drive; the file was retained.");
        }

        var operation = new ShellFileOperation
        {
            Function = 3,
            Source = Path.GetFullPath(path) + '\0' + '\0',
            Flags = 0x0004 | 0x0010 | 0x0040 | 0x0400
        };
        int result = SHFileOperation(ref operation);
        if (result != 0 || operation.Aborted)
        {
            throw new IOException($"Could not move the file to the Recycle Bin (Windows code {result}).");
        }

        if (File.Exists(path))
        {
            throw new IOException("Windows did not remove the file; the file was retained.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public nint Window;
        public uint Function;
        [MarshalAs(UnmanagedType.LPWStr)] public string Source;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Destination;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public nint NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref ShellFileOperation operation);
}
