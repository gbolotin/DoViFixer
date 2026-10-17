using DoViFixer.Application.Settings;

namespace DoViFixer.Application.Abstractions;

public interface IHardwareInfoProvider
{
    HardwareProfile Profile { get; }
}
