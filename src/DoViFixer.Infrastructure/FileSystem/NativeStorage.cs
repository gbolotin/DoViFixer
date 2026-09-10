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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte information, uint size);

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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, IntPtr information, uint size);

    internal static void Rename(SafeFileHandle handle, string destination)
    {
        byte[] name = System.Text.Encoding.Unicode.GetBytes(Path.GetFullPath(destination));
        int lengthOffset = IntPtr.Size == 8 ? 16 : 8;
        int nameOffset = lengthOffset + 4;
        int size = nameOffset + name.Length + 2;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size);
            // FILE_RENAME_INFO: ReplaceIfExists=false, RootDirectory=null.
            Marshal.WriteInt32(buffer, lengthOffset, name.Length);
            Marshal.Copy(name, 0, IntPtr.Add(buffer, nameOffset), name.Length);
            if (!SetFileInformationByHandle(handle, 3, buffer, (uint)size))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot rename original to " + destination);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void DeleteOnClose(SafeFileHandle handle)
    {
        // FILE_DISPOSITION_INFO_EX: DELETE | POSIX_SEMANTICS | IGNORE_READONLY_ATTRIBUTE.
        int flags = 1 | 2 | 16;
        if (!SetFileInformationByHandle(handle, 21, ref flags, sizeof(int)))
        {
            int error = Marshal.GetLastWin32Error();
            // Some SMB servers reject FileDispositionInfoEx or its extended flags.
            // Fall back only for unsupported operations, using the same validated handle.
            if (error is 1 or 50 or 87 or 120)
            {
                byte deleteFile = 1;
                if (SetFileInformationByHandle(handle, 4, ref deleteFile, sizeof(byte)))
                {
                    return;
                }
                error = Marshal.GetLastWin32Error();
            }
            throw new Win32Exception(error, $"Could not delete the exclusively held approved backup (Windows error {error}).");
        }
    }
}
