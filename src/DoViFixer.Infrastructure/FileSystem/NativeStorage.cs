using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DoViFixer.Infrastructure.FileSystem;

internal static class NativeStorage
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref int information, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint attributes, IntPtr template);

    internal static SafeFileHandle OpenForDeletion(string path)
    {
        var handle = CreateFile(path, 0x80000000 | 0x00010000, 0, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot acquire the approved backup exclusively.");
        }
        return handle;
    }

    internal static void DeleteOnClose(SafeFileHandle handle)
    {
        // FILE_DISPOSITION_INFO_EX: DELETE | POSIX_SEMANTICS | IGNORE_READONLY_ATTRIBUTE.
        int flags = 1 | 2 | 16;
        if (!SetFileInformationByHandle(handle, 21, ref flags, sizeof(int)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not delete the exclusively held approved backup.");
        }
    }
}
