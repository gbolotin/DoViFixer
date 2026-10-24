# Privacy policy

*Last updated: October 24, 2026*

DoViFixer does not collect, store or send any personal data. It has no account, no advertising, no analytics and no telemetry. Everything it does with your movies happens on your PC.

## What stays on your PC

- **Your movies.** DoViFixer reads and converts files only on your PC and on drives you choose. It never uploads them.
- **Settings and library.** Your settings, the list of movies you added and their scan results are saved in `%LOCALAPPDATA%\DoViFixer`.
- **Logs.** DoViFixer writes log files to `%LOCALAPPDATA%\DoViFixer\Logs` to help you solve problems. They can contain file names, folder paths and your Windows user name. They are deleted after 14 to 90 days.
- **Diagnostics.** **Settings › About › Export diagnostics** saves a ZIP file with the logs and settings, to a place you choose. DoViFixer does not send it anywhere. You decide whether to share it, for example in a bug report.

## When DoViFixer connects to the internet

DoViFixer connects to the internet only in these cases:

| When | Where | What is sent |
| --- | --- | --- |
| You agree to install a missing tool (FFmpeg, MKVToolNix, MediaInfo or dovi_tool) | The tool's official download site, such as github.com, mkvtoolnix.download or mediaarea.net, or Microsoft's WinGet service | A normal download request with the app name and version |
| You open the documentation (++f1++) | gbolotin.github.io, in your web browser | What your browser normally sends |
| The app starts, then every 6 hours while it runs, unless you turn off **Settings › About › Check for updates automatically**; you click **Check for updates**; you run `update-check` in DoViFixer.Console; or an AI assistant asks DoViFixer.Mcp to check for updates | api.github.com | A request for the latest release, with the app name and version |

These sites receive your IP address, as with any download, and handle it under their own privacy policies. DoViFixer adds no identifier and nothing about you or your files.

## File Explorer and notifications

DoViFixer adds **Open in DoViFixer** and **Convert with DoViFixer** to File Explorer's right-click menu, and shows Windows notifications when an operation finishes. Both are settings on your PC; no data leaves it. You can turn them off in **Settings › General**.

## Children

DoViFixer collects no data from anyone, including children.

## Changes

If DoViFixer ever starts to send data, this page will say so before the release that does it. Changes are listed in the [release notes](https://github.com/gbolotin/DoViFixer/releases).

## Contact

Questions about privacy: open an issue at [github.com/gbolotin/DoViFixer/issues](https://github.com/gbolotin/DoViFixer/issues).
