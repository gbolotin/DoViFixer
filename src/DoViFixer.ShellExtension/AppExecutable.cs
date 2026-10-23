using System.Runtime.InteropServices;

namespace DoViFixer.ShellExtension;

/// <summary>DoViFixer.App.exe, which is published in the folder of this DLL.</summary>
internal static unsafe partial class AppExecutable
{
    private const uint FromAddress = 0x4;
    private const uint UnchangedReferenceCount = 0x2;

    private static string? path;

    public static string Path => path ??= System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ModulePath())!, "DoViFixer.App.exe");

    private static string ModulePath()
    {
        nint module;
        // Any address inside this DLL finds the module, wherever the hosting process loaded it from.
        if (!GetModuleHandleExW(FromAddress | UnchangedReferenceCount, (nint)(delegate* unmanaged<int>)&Exports.DllCanUnloadNow, &module))
        {
            throw new InvalidOperationException("The module of the File Explorer commands was not found.");
        }

        const int capacity = 32768;
        char* buffer = stackalloc char[capacity];
        uint length = GetModuleFileNameW(module, buffer, capacity);
        if (length == 0 || length >= capacity)
        {
            throw new InvalidOperationException("The path of the File Explorer commands was not read.");
        }

        return new string(buffer, 0, (int)length);
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, nint address, nint* module);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetModuleFileNameW(nint module, char* fileName, uint size);
}
