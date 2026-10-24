# Settings

Open Settings with the gear at the right of the title bar, with **Tools › Settings**, or with ++ctrl+comma++. Every change saves automatically.

<img class="screenshot" src="../../assets/screenshots/settings.png" alt="The Presets page in Settings">

## Presets

A preset sets every scan, conversion and safety setting at once. Folders, worker counts, tool paths and the theme belong to your computer, so presets never change them.

| Built-in preset | What it sets |
| --- | --- |
| Default | The settings a new installation starts with |
| Thorough check (40 clips) | 40 scan clips per movie and full verification; originals kept |
| Exhaustive check (100 clips) | 100 scan clips per movie and full verification; originals kept |
| Replace originals, keep EL archive | 40 clips, enhancement-layer backup, replace originals after full verification, Recycle Bin on |

**Apply preset…** first lists every setting it changes and marks the ones that can lose data. You can also save your own presets.

## The pages

| Page | What you set there |
| --- | --- |
| **General** | Theme, check boxes in the file list, notifications, File Explorer commands |
| **Conversion** | Safe mode, Convert to HDR10, FEL handling, enhancement-layer backup, output folder, subfolder search depth, temporary storage folder |
| **Safety** | Verification level, Replace original after verification, Recycle Bin, extra free-space margin |
| **Analysis** | Scan clips and clip length, required scan coverage, peak-brightness percentile, Complex FEL detection margin and assumed peak brightness |
| **Performance** | Simultaneous conversions, concurrent scan clips, tool priority, automatic scan, reuse of saved results, media library |
| **Watch folder** | A folder to watch and convert automatically. See [Watch folder](watch-folder.md). |
| **Native tools** | Tool status, dependency setup, custom tool paths |
| **About** | Version, update checks, logs, diagnostics export |

## Analysis defaults

| Setting | Default | Allowed values |
| --- | --- | --- |
| Clips per video | 10 clips | 1–100 |
| Clip length | 1 second | 0.1–60 seconds |
| Required scan coverage | 100% | 1–100% |
| Peak-brightness percentile | 100% (brightest frame) | 1–100% |
| Brightness margin | 50 nits | 0–1000 nits |
| Assumed peak brightness | 1,000 nits | Used when the file has no usable MaxCLL |

Lower values make scans faster but can miss brightness that the enhancement layer adds. Changing them makes saved results out of date; scan or inspect again to update them.

## Where your data lives

| What | Where |
| --- | --- |
| Settings and your presets | `%LOCALAPPDATA%\DoViFixer\settings.json` and `presets.json` |
| Media library (remembered movies, results, history) | `%LOCALAPPDATA%\DoViFixer\library.db` |
| Logs | `%LOCALAPPDATA%\DoViFixer\Logs` |
