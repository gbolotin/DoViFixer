using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Diagnostics;

namespace DoViFixer.Infrastructure.Diagnostics;

internal sealed class WindowsFileLockInspector : IFileLockInspector
{
    public Task<FileLockReport> InspectAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => Inspect(path, cancellationToken), cancellationToken);

    private static FileLockReport Inspect(string path, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new([], "Lock inspection is only available on Windows.");
        }

        uint session = 0;
        bool started = false;
        try
        {
            token.ThrowIfCancellationRequested();
            Check(RmStartSession(out session, 0, new StringBuilder(33)));
            started = true;
            Check(RmRegisterResources(session, 1, [Path.GetFullPath(path)], 0, IntPtr.Zero, 0, IntPtr.Zero));
            uint count = 0;
            ProcessInfo[]? processes = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                token.ThrowIfCancellationRequested();
                int status = RmGetList(session, out uint needed, ref count, processes, out _);
                if (status == 0)
                {
                    return new(processes?.Take((int)count).Select(p => new LockHolder(p.Process.ProcessId, p.ApplicationName)).ToArray() ?? []);
                }
                if (status != 234)
                {
                    Check(status);
                }
                if (needed > 4096)
                {
                    return new([], "Too many processes to inspect.");
                }
                processes = new ProcessInfo[needed];
                count = needed;
            }
            return new([], "The process list changed repeatedly during inspection.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new([], ex.Message);
        }
        finally
        {
            if (started)
            {
                RmEndSession(session);
            }
        }
    }

    private static void Check(int status)
    {
        if (status != 0)
        {
            throw new Win32Exception(status);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        public int ProcessId;
        public FILETIME StartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ApplicationName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceName;
        public int ApplicationType;
        public uint Status;
        public uint SessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint fileCount, string[] files, uint applicationCount, IntPtr applications, uint serviceCount, IntPtr services);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? processes, out uint rebootReasons);
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
}
