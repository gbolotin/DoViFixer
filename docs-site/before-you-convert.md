# Before you convert

Read this page once before you convert your movies. It explains what conversion removes and why DoViFixer skips some files.

## What conversion removes

A Profile 7 movie has an **enhancement layer (EL)**. Streaming boxes cannot use it, so a Profile 8.1 movie does not have one. Conversion takes the enhancement layer out. Everything else stays: the base layer picture, the Dolby Vision metadata (converted to Profile 8.1), audio, subtitles and chapters.

Whether that matters depends on what the enhancement layer holds.

<img class="illustration" src="../assets/fel-check.svg" alt="Animation: MEL files are converted, Simple FEL files are converted only when you turn it on, and Complex FEL files are skipped">

| Type | What the enhancement layer holds | What DoViFixer does by default |
| --- | --- | --- |
| **MEL** (minimal enhancement layer) | Almost nothing | Converts it. Nothing visible is lost. |
| **Simple FEL** (full enhancement layer) | Some picture data, but no extra brightness | Skips it, unless you turn on **Include Simple FEL conversion** |
| **Complex FEL** | Picture data that makes the picture brighter than the base layer | Skips it, unless you turn on **Allow Complex FEL conversion** |

Both FEL options are in **Settings › Conversion**.

!!! warning "Complex FEL"
    A complex FEL movie needs its enhancement layer to look right. Converting it keeps the Dolby Vision metadata but loses the extra brightness. On a streaming box, that movie looks wrong whether you convert it or not. Keep the original for a UHD Blu-ray player, or turn on the enhancement-layer backup before you convert.

## How DoViFixer decides

DoViFixer compares the brightness that the Dolby Vision metadata describes with the brightness of the base layer.

- **Scan** checks short clips spread over the movie. It is fast, and good for a first look.
- **Inspect** (++f4++) reads the metadata of every frame.
- **Deep Inspect** decodes every frame of the base layer and measures it. It is the most reliable check, and the slowest.

Convert always does a full inspection first, even when Scan already sorted the file. Results are a strong guide, not a playback guarantee. See [Add and scan movies](guide/scan.md).

## Your original is safe

- By default, the converted movie is saved **next to** the original as `<name> - DV P8.1.mkv`. The original is not renamed, changed or deleted.
- If the converted file already exists, the app asks you what to do: skip, replace it once the new file is verified, or keep both. A batch skips such files.
- Before it saves the result, DoViFixer checks it: Dolby Vision profile, frame counts, timestamps, and the audio and subtitle data.

You can choose to replace originals instead: **Settings › Safety › Replace original after verification**. Turn on the [enhancement-layer backup](guide/restore.md) first if you do.

## Disk space

Conversion needs temporary space while it works. DoViFixer reserves about eight times the movie size plus 1 GiB before it starts, and waits or stops if there is not enough. Real use is usually lower. You can move the **Temporary storage folder** to a bigger drive in **Settings › Conversion**.
