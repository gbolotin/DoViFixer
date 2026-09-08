using System.Runtime.InteropServices;
using DoViFixer.Application.Dependencies;
using DoViFixer.Infrastructure.Configuration;

namespace DoViFixer.Infrastructure.Dependencies;

internal sealed record ToolPackage(string Id, string Version, string Url, string Sha256, IReadOnlyList<NativeTool> Tools, bool WinGet);
internal static class InstallationCatalog
{
    // Pinned, independently reviewed upstream/WinGet SHA-256 values. Update these deliberately, never from the downloaded ZIP itself.
    internal static IReadOnlyList<ToolPackage> Packages => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => new ToolPackage[]
        {
            new("Gyan.FFmpeg", "8.0.1", "https://github.com/GyanD/codexffmpeg/releases/download/8.0.1/ffmpeg-8.0.1-full_build.zip",
                "467CDE100A47ED4B03A897988AEB4A296890C1E2B2D2864204657D002BC5FB90", new[] { NativeTool.FFmpeg, NativeTool.FFprobe }, true),
            new("MoritzBunkus.MKVToolNix", "95.0.0", "https://mkvtoolnix.download/windows/releases/95.0/mkvtoolnix-64-bit-95.0.zip",
                "561CF85FE27406CB8842E237EFD1803376A58061848ACBAD89E822A6BEA9AE80", new[] { NativeTool.MkvMerge, NativeTool.MkvExtract }, true),
            new("MediaArea.MediaInfo", "26.05", "https://mediaarea.net/download/binary/mediainfo/26.05/MediaInfo_CLI_26.05_Windows_x64.zip",
                "F7F80620CE6D14F4995F0DE6F98E3EF18AD29496DB01899571152EE3311229F9", new[] { NativeTool.MediaInfo }, true),
            new("quietvoid.dovi_tool", "2.3.3", "https://github.com/quietvoid/dovi_tool/releases/download/2.3.3/dovi_tool-2.3.3-x86_64-pc-windows-msvc.zip",
                "37AE198F2A535C910BEFAD39FC09C21CDED76BF3EF2D5459D542E58C2C158311", new[] { NativeTool.DoviTool }, false)
        },
        _ => Array.Empty<ToolPackage>()
    };

    internal static InstallationItem Item(ToolPackage package, StorageOptions storage, bool winget) => new(package.Id, package.Version,
        winget ? InstallationProvider.WinGet : InstallationProvider.VerifiedZip, package.Url,
        winget ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages")
            : Path.Combine(storage.ToolsDirectory, package.Id, package.Version),
        "user (portable ZIP)", false, package.Tools, package.Sha256);
}
