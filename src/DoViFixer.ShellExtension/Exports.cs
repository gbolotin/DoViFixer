using System.Runtime.InteropServices;

namespace DoViFixer.ShellExtension;

/// <summary>The functions COM calls in an in-process server.</summary>
public static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = nameof(DllGetClassObject))]
    public static int DllGetClassObject(Guid* clsid, Guid* iid, void** result)
    {
        try
        {
            return ComServer.GetClassObject(clsid, iid, result);
        }
        catch (Exception)
        {
            return ComServer.E_FAIL;
        }
    }

    /// <summary>A Native AOT library cannot be unloaded, so it never allows it.</summary>
    [UnmanagedCallersOnly(EntryPoint = nameof(DllCanUnloadNow))]
    public static int DllCanUnloadNow() => ComServer.S_FALSE;
}
