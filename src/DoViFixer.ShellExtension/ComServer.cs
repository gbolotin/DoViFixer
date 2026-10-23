using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DoViFixer.ShellExtension;

/// <summary>
/// The two File Explorer commands, Open and Convert, as COM objects that implement IExplorerCommand, and the class
/// factory that creates them. The vtables are built by hand, so the native DLL needs no COM interop at run time.
/// </summary>
internal static unsafe class ComServer
{
    /// <summary>"Open in DoViFixer"; the package manifest names the same class.</summary>
    public static readonly Guid OpenClsid = new("a2760a01-4960-43ed-880d-7e19fbec2f5d");
    /// <summary>"Convert with DoViFixer"; the package manifest names the same class.</summary>
    public static readonly Guid ConvertClsid = new("64401690-ab1c-46c3-8512-78c0f3cbe147");

    internal static readonly Guid IUnknown = new("00000000-0000-0000-c000-000000000046");
    internal static readonly Guid IClassFactory = new("00000001-0000-0000-c000-000000000046");
    internal static readonly Guid IExplorerCommand = new("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9");

    internal const int S_OK = 0;
    internal const int S_FALSE = 1;
    internal const int E_NOTIMPL = unchecked((int)0x80004001);
    internal const int E_NOINTERFACE = unchecked((int)0x80004002);
    internal const int E_POINTER = unchecked((int)0x80004003);
    internal const int E_FAIL = unchecked((int)0x80004005);
    internal const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    internal const int CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);

    /// <summary>A COM object: its vtable, its reference count and the command it creates or is.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ComObject
    {
        public void** Vtable;
        public int References;
        public int Convert;
    }

    private static readonly void** FactoryVtable = CreateFactoryVtable();
    private static readonly void** CommandVtable = CreateCommandVtable();

    /// <summary>Returns the class factory for <paramref name="clsid"/>, as DllGetClassObject does.</summary>
    internal static int GetClassObject(Guid* clsid, Guid* iid, void** result)
    {
        if (result == null)
        {
            return E_POINTER;
        }

        *result = null;
        bool convert = *clsid == ConvertClsid;
        if (!convert && *clsid != OpenClsid)
        {
            return CLASS_E_CLASSNOTAVAILABLE;
        }

        return Create(FactoryVtable, convert, iid, result);
    }

    private static int Create(void** vtable, bool convert, Guid* iid, void** result)
    {
        var instance = (ComObject*)NativeMemory.Alloc((nuint)sizeof(ComObject));
        instance->Vtable = vtable;
        instance->References = 1;
        instance->Convert = convert ? 1 : 0;
        int hr = QueryInterfaceCore(instance, iid, result);
        ReleaseCore(instance);
        return hr;
    }

    private static int QueryInterfaceCore(ComObject* self, Guid* iid, void** result)
    {
        if (result == null)
        {
            return E_POINTER;
        }

        Guid own = self->Vtable == FactoryVtable ? IClassFactory : IExplorerCommand;
        if (*iid == IUnknown || *iid == own)
        {
            Interlocked.Increment(ref self->References);
            *result = self;
            return S_OK;
        }

        *result = null;
        return E_NOINTERFACE;
    }

    private static uint ReleaseCore(ComObject* self)
    {
        int references = Interlocked.Decrement(ref self->References);
        if (references == 0)
        {
            NativeMemory.Free(self);
        }

        return (uint)references;
    }

    private static void** CreateFactoryVtable()
    {
        var vtable = (void**)NativeMemory.Alloc(5, (nuint)sizeof(void*));
        vtable[0] = (delegate* unmanaged<ComObject*, Guid*, void**, int>)&QueryInterface;
        vtable[1] = (delegate* unmanaged<ComObject*, uint>)&AddRef;
        vtable[2] = (delegate* unmanaged<ComObject*, uint>)&Release;
        vtable[3] = (delegate* unmanaged<ComObject*, void*, Guid*, void**, int>)&CreateInstance;
        vtable[4] = (delegate* unmanaged<ComObject*, int, int>)&LockServer;
        return vtable;
    }

    private static void** CreateCommandVtable()
    {
        var vtable = (void**)NativeMemory.Alloc(11, (nuint)sizeof(void*));
        vtable[0] = (delegate* unmanaged<ComObject*, Guid*, void**, int>)&QueryInterface;
        vtable[1] = (delegate* unmanaged<ComObject*, uint>)&AddRef;
        vtable[2] = (delegate* unmanaged<ComObject*, uint>)&Release;
        vtable[3] = (delegate* unmanaged<ComObject*, void*, char**, int>)&GetTitle;
        vtable[4] = (delegate* unmanaged<ComObject*, void*, char**, int>)&GetIcon;
        vtable[5] = (delegate* unmanaged<ComObject*, void*, char**, int>)&GetToolTip;
        vtable[6] = (delegate* unmanaged<ComObject*, Guid*, int>)&GetCanonicalName;
        vtable[7] = (delegate* unmanaged<ComObject*, void*, int, uint*, int>)&GetState;
        vtable[8] = (delegate* unmanaged<ComObject*, void*, void*, int>)&Invoke;
        vtable[9] = (delegate* unmanaged<ComObject*, uint*, int>)&GetFlags;
        vtable[10] = (delegate* unmanaged<ComObject*, void**, int>)&EnumSubCommands;
        return vtable;
    }

    // IUnknown

    [UnmanagedCallersOnly]
    private static int QueryInterface(ComObject* self, Guid* iid, void** result) => QueryInterfaceCore(self, iid, result);

    [UnmanagedCallersOnly]
    private static uint AddRef(ComObject* self) => (uint)Interlocked.Increment(ref self->References);

    [UnmanagedCallersOnly]
    private static uint Release(ComObject* self) => ReleaseCore(self);

    // IClassFactory

    [UnmanagedCallersOnly]
    private static int CreateInstance(ComObject* self, void* outer, Guid* iid, void** result)
    {
        if (result == null)
        {
            return E_POINTER;
        }

        *result = null;
        return outer != null ? CLASS_E_NOAGGREGATION : Create(CommandVtable, self->Convert != 0, iid, result);
    }

    // The process that hosts the commands keeps this DLL loaded; DllCanUnloadNow never lets it go.
    [UnmanagedCallersOnly]
    private static int LockServer(ComObject* self, int locked) => S_OK;

    // IExplorerCommand

    [UnmanagedCallersOnly]
    private static int GetTitle(ComObject* self, void* items, char** title) =>
        ReturnString(self->Convert != 0 ? $"Convert with {ApplicationTitle.Name}" : $"Open in {ApplicationTitle.Name}", title);

    [UnmanagedCallersOnly]
    private static int GetIcon(ComObject* self, void* items, char** icon)
    {
        try
        {
            return ReturnString($"{AppExecutable.Path},0", icon);
        }
        catch (Exception)
        {
            *icon = null;
            return E_FAIL;
        }
    }

    [UnmanagedCallersOnly]
    private static int GetToolTip(ComObject* self, void* items, char** toolTip)
    {
        *toolTip = null;
        return E_NOTIMPL;
    }

    [UnmanagedCallersOnly]
    private static int GetCanonicalName(ComObject* self, Guid* name)
    {
        *name = self->Convert != 0 ? ConvertClsid : OpenClsid;
        return S_OK;
    }

    // The package manifest shows the commands only for MKV files and folders, so they are always enabled.
    [UnmanagedCallersOnly]
    private static int GetState(ComObject* self, void* items, int okToBeSlow, uint* state)
    {
        *state = 0;
        return S_OK;
    }

    [UnmanagedCallersOnly]
    private static int Invoke(ComObject* self, void* items, void* bindContext)
    {
        try
        {
            string executable = AppExecutable.Path;
            foreach (var arguments in CommandLines.For(ShellItems.Paths(items), self->Convert != 0))
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false };
                foreach (string argument in arguments)
                {
                    start.ArgumentList.Add(argument);
                }

                using var process = Process.Start(start);
            }

            return S_OK;
        }
        catch (Exception)
        {
            // An exception must not cross into File Explorer.
            return E_FAIL;
        }
    }

    [UnmanagedCallersOnly]
    private static int GetFlags(ComObject* self, uint* flags)
    {
        *flags = 0;
        return S_OK;
    }

    [UnmanagedCallersOnly]
    private static int EnumSubCommands(ComObject* self, void** commands)
    {
        *commands = null;
        return E_NOTIMPL;
    }

    private static int ReturnString(string value, char** result)
    {
        // The caller frees the string with CoTaskMemFree.
        *result = (char*)Marshal.StringToCoTaskMemUni(value);
        return S_OK;
    }
}
