# Convert

<img class="illustration" src="../../assets/conversion.svg" alt="Animation: DoViFixer keeps the base layer, converts the RPU to Profile 8.1 and moves the enhancement layer into an optional .dovi backup">

## Convert your movies

1. Select the movies on the Media page.
2. Click **Convert**, or press ++f5++.
3. DoViFixer inspects each movie fully, then converts the ones your settings allow.

Each movie gets a progress card with its steps and the time left. You can keep adding and scanning movies while conversion runs.

**Convert** is unavailable when every selected movie would only be skipped: for example, the movie is not Profile 7, or it is FEL and the FEL options are off. Each skipped movie shows the reason in its row.

## What you get

By default, the converted movie is saved in the same folder as the original:

```
Movie.mkv                  ← your original, unchanged
Movie - DV P8.1.mkv        ← the converted movie
```

Plex and similar apps group both files under the same movie. To save converted movies in another folder, set **Settings › Conversion › Output folder**.

With **Settings › Conversion › Convert to HDR10**, the result has no Dolby Vision at all: `Movie - HDR10.mkv`.

## Before the result is saved

DoViFixer checks the new file before it publishes it:

- The Dolby Vision profile and the metadata for every frame.
- The video size, codec and frame count.
- The timestamps of every track.
- The audio and subtitle data, chapters, tags and attachments.

If a check fails, the result is not saved. **Settings › Safety › Verification level** sets how thorough the checks are.

## Cancel, skip and pause

- **Cancel** on a movie's card stops that movie and cleans up its temporary files.
- **Skip** leaves a waiting movie out of the batch.
- **Pause** lets running conversions finish but starts no new ones.

Closing the app cancels running work safely first.

## Replace originals

To save disk space, you can let DoViFixer replace the original: **Settings › Safety › Replace original after verification**. The original is deleted only after the new file passes every check, and it goes to the Recycle Bin by default.

!!! warning
    Without an [enhancement-layer backup](restore.md), you cannot get the Profile 7 original back after it is replaced. Turn on **Back up enhancement layer** first.

## Several movies at once

**Settings › Performance › Simultaneous conversions** sets how many movies convert at the same time. The recommended value depends on your CPU. Lower it if your disks are slow or temporary space is short.

## Safe mode

By default, DoViFixer streams the video directly between tools, which is faster. If that fails, it automatically tries again with temporary files on disk. **Settings › Conversion › Use disk-based conversion (Safe mode)** uses disk files from the start.
