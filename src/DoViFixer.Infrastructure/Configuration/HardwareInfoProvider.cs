using System.Runtime.InteropServices;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Settings;

namespace DoViFixer.Infrastructure.Configuration;

internal sealed class HardwareInfoProvider : IHardwareInfoProvider
{
    private readonly Lazy<HardwareProfile> profile = new(Detect);
    public HardwareProfile Profile => profile.Value;

    private static HardwareProfile Detect()
    {
        int logical = Math.Max(1, Environment.ProcessorCount);
        int physical = 0;
        if (OperatingSystem.IsWindows())
        {
            uint length = 0;
            GetLogicalProcessorInformationEx(0, nint.Zero, ref length);
            if (length is > 0 and <= int.MaxValue)
            {
                nint buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (GetLogicalProcessorInformationEx(0, buffer, ref length))
                    {
                        int offset = 0;
                        while (offset <= (long)length - 8)
                        {
                            int size = Marshal.ReadInt32(buffer, offset + 4);
                            if (size < 8 || size > length - offset)
                            {
                                physical = 0;
                                break;
                            }
                            physical++;
                            offset += size;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        // Affinity and CPU limits may expose fewer processors than the machine contains.
        return new(physical > 0 ? Math.Min(physical, logical) : Math.Max(1, logical / 2), logical, physical > 0);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, nint buffer, ref uint returnedLength);
}
