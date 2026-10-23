# Install

DoViFixer runs on 64-bit Windows 10 and Windows 11.

## 1. Install the .NET runtime

DoViFixer needs the **.NET 10 Desktop Runtime** (x64).

1. Open the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Under **.NET Desktop Runtime**, download the **x64** Windows installer.
3. Run the installer.

## 2. Download DoViFixer

1. Open the [latest release](https://github.com/gbolotin/DoViFixer/releases/latest).
2. Download `DoViFixer.App-X.Y.Z.zip`.
3. Unzip it to a folder of your choice, for example `C:\Apps\DoViFixer`.
4. Run `DoViFixer.App.exe`.

!!! tip "Other downloads in the release"
    `DoViFixer.Console` is the command-line version. `DoViFixer.Mcp` lets an AI assistant such as Claude Desktop use DoViFixer. You only need `DoViFixer.App` for the desktop app. See [Command line and AI assistants](console.md).

## 3. Let the app set up its tools

DoViFixer uses free, well-known tools to read and write video files:

| Tool | What DoViFixer uses it for |
| --- | --- |
| FFmpeg and ffprobe | Read the video stream and frame information |
| MKVToolNix (mkvmerge, mkvextract, mkvpropedit) | Take MKV files apart and put them back together |
| MediaInfo **CLI** | Read the HDR and Dolby Vision details of a file |
| dovi_tool | Read and convert the Dolby Vision metadata |

On first start, the app checks for these tools.

<img class="screenshot" src="../assets/screenshots/dependencies.png" alt="The first-start check of the tools the app needs">

- Tools that are already installed are used as they are.
- If a tool is missing, the app shows the exact version, download address and destination. It installs nothing until you agree.
- Downloads are checked against a known SHA-256 hash. The tools go into your user profile; no administrator rights are needed.

!!! note
    The MediaInfo **GUI** app does not count. DoViFixer needs the MediaInfo **CLI** (command-line) version.

You can check the tools again at any time with **Tools › Check dependencies**, or set your own tool paths in **Settings › Native tools**.

## Updates

**Settings › About** shows your version. DoViFixer never updates itself. To update, download the new zip and unzip it over the old folder. Your settings and media library are kept in `%LOCALAPPDATA%\DoViFixer`.

Next: [Before you convert](before-you-convert.md).
