# Features

## Check your movies

- **Finds the Dolby Vision profile** of every MKV file, and the type of its enhancement layer: MEL, Simple FEL or Complex FEL.
- **Three levels of checks:** a fast **Scan** of short clips, a full **Inspect** (++f4++) of the metadata of every frame, and **Deep Inspect**, which decodes every frame and measures its real brightness.
- **A clear verdict for each file:** a colored dot, a **Risk** row and a **Next step** button that says what to do next.
- **Export results** as a CSV, JSON or text file.

See [Add and scan movies](guide/scan.md).

## Convert

- **Profile 7 to Profile 8.1**, which Apple TV 4K, NVIDIA Shield TV and Amazon Fire TV play in Dolby Vision.
- **Keeps the base layer bit for bit**, and the audio, subtitles, chapters, tags and attachments as they are.
- **Skips the files that would lose picture:** Complex FEL is skipped by default, and Simple FEL converts only when you turn it on.
- **Convert to HDR10** when you want a file without Dolby Vision at all.
- **Batches:** convert several movies at the same time, each with its own progress card and time left. Cancel, skip or pause at any moment.
- **Fast and robust:** video streams directly between the tools, with an automatic retry from temporary files on disk if that fails.
- **Checks free disk space** before it starts, and waits or stops if there is not enough.

See [Convert](guide/convert.md).

## Safe by design

- **Your original stays unchanged.** The converted movie is saved next to it as `Movie - DV P8.1.mkv`, or in an output folder you choose.
- **Every result is verified** before it is saved: Dolby Vision profile, metadata of every frame, frame counts, timestamps, audio and subtitle data.
- **Nothing changes without your approval.** The app shows the exact files it will create or replace, and marks the choices that can lose data.
- **Replace originals only if you want to**, only after verification, and into the Recycle Bin by default.

See [Before you convert](before-you-convert.md).

## Backup and restore

- **Enhancement-layer backup:** a small `.dovi` file next to the converted movie, tied to it by a SHA-256 hash.
- **Restore to DV7** rebuilds the original Profile 7 movie whenever you need it, so you can delete the large original now.
- **Clean up backups** finds old backups in a folder and deletes exactly the files you review.

See [Backup and restore](guide/restore.md).

## Automation

- **Watch folder:** new movies in a folder, such as your download folder, are scanned and converted by themselves.
- **Presets:** four built-in presets set every scan, conversion and safety setting at once. Save your own.
- **Media library:** the app remembers your movies, their results and their history, and reuses saved results.

See [Watch folder](guide/watch-folder.md) and [Settings](guide/settings.md).

## Easy to use

- **A native Windows app** with light and dark themes, drag and drop, and keyboard shortcuts.
- **Selects like File Explorer**, or with check boxes if you prefer.
- **Sets up its own tools:** FFmpeg, MKVToolNix, MediaInfo CLI and dovi_tool. It asks first, checks every download against a known SHA-256 hash and needs no administrator rights.
- **Activity in plain words**, Windows notifications and progress on the taskbar.
- **Logs and a diagnostics ZIP** for bug reports. Nothing is uploaded.

See [Install](install.md) and [Troubleshooting](troubleshooting.md).

## Command line and AI assistants

- **DoViFixer.Console** runs the same engine from a terminal, with a plan mode that shows what would happen and changes nothing.
- **DoViFixer.Mcp** lets an AI assistant such as Claude Desktop or Claude Code scan, inspect and convert for you, after you approve its plan.

See [Command line and AI assistants](console.md).

## Free and open source

DoViFixer is free, and its source code is on [GitHub](https://github.com/gbolotin/DoViFixer) under the GPL-3.0 license.
