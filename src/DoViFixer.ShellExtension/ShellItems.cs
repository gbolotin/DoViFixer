using System.Runtime.InteropServices;

namespace DoViFixer.ShellExtension;

/// <summary>Reads the file system paths of the items in an IShellItemArray through its vtable.</summary>
internal static unsafe class ShellItems
{
    private const uint SigdnFileSystemPath = 0x80058000;

    // Vtable slots after QueryInterface, AddRef and Release.
    private const int ArrayGetCount = 7;
    private const int ArrayGetItemAt = 8;
    private const int ItemGetDisplayName = 5;
    private const int Release = 2;

    /// <summary>The paths of the items in <paramref name="array"/>; items without one, such as a library, are left out.</summary>
    public static List<string> Paths(void* array)
    {
        List<string> paths = [];
        if (array == null)
        {
            return paths;
        }

        void** arrayVtable = *(void***)array;
        uint count;
        if (((delegate* unmanaged<void*, uint*, int>)arrayVtable[ArrayGetCount])(array, &count) < 0)
        {
            return paths;
        }

        for (uint index = 0; index < count; index++)
        {
            void* item;
            if (((delegate* unmanaged<void*, uint, void**, int>)arrayVtable[ArrayGetItemAt])(array, index, &item) < 0 || item == null)
            {
                continue;
            }

            void** itemVtable = *(void***)item;
            char* name;
            if (((delegate* unmanaged<void*, uint, char**, int>)itemVtable[ItemGetDisplayName])(item, SigdnFileSystemPath, &name) >= 0 && name != null)
            {
                paths.Add(new string(name));
                Marshal.FreeCoTaskMem((nint)name);
            }

            ((delegate* unmanaged<void*, uint>)itemVtable[Release])(item);
        }

        return paths;
    }
}
