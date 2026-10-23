using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DoViFixer.ShellExtension.Tests;

/// <summary>The COM objects File Explorer's surrogate process creates, called through their vtables as COM calls them.</summary>
[TestClass]
public sealed unsafe class ComServerTests
{
    [TestMethod]
    public void TheCommandsHaveTheirTitlesAndAreEnabled()
    {
        void* open = CreateCommand(ComServer.OpenClsid);
        void* convert = CreateCommand(ComServer.ConvertClsid);
        try
        {
            Assert.AreEqual("Open in DoViFixer", Title(open));
            Assert.AreEqual("Convert with DoViFixer", Title(convert));
            uint state = 1;
            Assert.AreEqual(ComServer.S_OK, ((delegate* unmanaged<void*, void*, int, uint*, int>)Vtable(open)[7])(open, null, 0, &state));
            Assert.AreEqual(0u, state, "ECS_ENABLED");
            Guid name;
            Assert.AreEqual(ComServer.S_OK, ((delegate* unmanaged<void*, Guid*, int>)Vtable(convert)[6])(convert, &name));
            Assert.AreEqual(ComServer.ConvertClsid, name);
        }
        finally
        {
            Assert.AreEqual(0u, Release(open), "The last release frees the command.");
            Release(convert);
        }
    }

    [TestMethod]
    public void UnknownClassesAndInterfacesAreRefused()
    {
        delegate* unmanaged<Guid*, Guid*, void**, int> getClassObject = &Exports.DllGetClassObject;
        Guid unknownClass = Guid.NewGuid();
        Guid factoryInterface = ComServer.IClassFactory;
        void* result;
        Assert.AreEqual(ComServer.CLASS_E_CLASSNOTAVAILABLE, getClassObject(&unknownClass, &factoryInterface, &result));

        void* command = CreateCommand(ComServer.OpenClsid);
        try
        {
            Guid other = Guid.NewGuid();
            Assert.AreEqual(ComServer.E_NOINTERFACE, ((delegate* unmanaged<void*, Guid*, void**, int>)Vtable(command)[0])(command, &other, &result));
            Assert.IsTrue(result == null);
        }
        finally
        {
            Release(command);
        }
    }

    [TestMethod]
    public void TheSelectedItemsAreReadAsFileSystemPaths()
    {
        string folder = Directory.CreateTempSubdirectory("DoViFixer ShellItems ").FullName;
        try
        {
            string movie = Path.Combine(folder, "Mountain Ä.mkv");
            File.WriteAllText(movie, "");
            Guid shellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            Guid shellItemArray = new("b63ea76d-1f85-456f-a19c-48159efa858b");
            void* item;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(movie, null, &shellItem, &item));
            void* array;
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromShellItem(item, &shellItemArray, &array));
            Release(item);
            try
            {
                CollectionAssert.AreEqual(new[] { movie }, ShellItems.Paths(array));
            }
            finally
            {
                Release(array);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void* CreateCommand(Guid clsid)
    {
        delegate* unmanaged<Guid*, Guid*, void**, int> getClassObject = &Exports.DllGetClassObject;
        Guid factoryInterface = ComServer.IClassFactory;
        void* factory;
        Assert.AreEqual(ComServer.S_OK, getClassObject(&clsid, &factoryInterface, &factory));
        Guid commandInterface = ComServer.IExplorerCommand;
        void* command;
        int hr = ((delegate* unmanaged<void*, void*, Guid*, void**, int>)Vtable(factory)[3])(factory, null, &commandInterface, &command);
        Release(factory);
        Assert.AreEqual(ComServer.S_OK, hr);
        return command;
    }

    private static string Title(void* command)
    {
        char* title;
        Assert.AreEqual(ComServer.S_OK, ((delegate* unmanaged<void*, void*, char**, int>)Vtable(command)[3])(command, null, &title));
        string value = new(title);
        Marshal.FreeCoTaskMem((nint)title);
        return value;
    }

    private static void** Vtable(void* instance) => *(void***)instance;

    private static uint Release(void* instance) => ((delegate* unmanaged<void*, uint>)Vtable(instance)[2])(instance);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, void* bindContext, Guid* iid, void** item);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHCreateShellItemArrayFromShellItem(void* item, Guid* iid, void** array);
}
