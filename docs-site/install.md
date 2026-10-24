# Install

DoViFixer runs on 64-bit Windows 10 and Windows 11.

## 1. Install the .NET runtime

DoViFixer needs the **.NET 10 Desktop Runtime** (x64).

1. Open the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Under **.NET Desktop Runtime**, download the **x64** Windows installer.
3. Run the installer.

## 2. Install DoViFixer

1. Open the [latest release](https://github.com/gbolotin/DoViFixer/releases/latest).
2. Download `DoViFixer-Setup-X.Y.Z.exe` and run it.
3. Allow the administrator prompt. Setup installs DoViFixer for all users in `Program Files`.
4. Leave **Start DoViFixer** checked and click **Finish**.

Setup offers three parts: the desktop app, the **MCP server for AI assistants** and the **command-line version**. It adds the MCP server to the assistants you tick: Claude Desktop, Claude Code, Codex and Hermes Agent. It ticks the ones it finds on your PC. It can also add the command-line version to `PATH`. See [Command line and AI assistants](console.md).

Setup also trusts the certificate of DoViFixer's File Explorer package. On Windows 11, **Open in DoViFixer** and **Convert with DoViFixer** are then in the first right-click menu as soon as DoViFixer has started once. To remove DoViFixer, use **Settings › Apps › Installed apps**. Uninstalling removes the File Explorer commands and the certificate too. It also asks whether to delete your media library; your settings and downloaded tools stay.

If an earlier installation left settings or a media library in `%LOCALAPPDATA%\DoViFixer`, setup checks whether the new version can read them:

- Setup deletes the settings or library that the new version cannot read, and tells you.
- Setup asks whether to keep the settings and library that the new version can read. Click **No** to start with new ones.

!!! note "Windows SmartScreen"
    Setup is not signed by a certificate authority, so Windows may say that it protected your PC. Click **More info**, then **Run anyway**.

### Without setup

You can also use DoViFixer without installing it:

1. Download `DoViFixer.App-X.Y.Z.zip` from the same release.
2. Unzip it to a folder of your choice, for example `C:\Apps\DoViFixer`.
3. Run `DoViFixer.App.exe`.

On Windows 11, the File Explorer commands are then under **Show more options** until you click **Settings › General › File Explorer › Show in the Windows 11 menu…**.

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

**Settings › About** shows your version. DoViFixer never updates itself. To update, run the new setup, or download the new zip and unzip it over the old folder. Your settings and media library are kept in `%LOCALAPPDATA%\DoViFixer`.

Next: [Before you convert](before-you-convert.md).
