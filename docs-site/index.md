---
hide:
  - navigation
---

# DoViFixer

<img class="illustration" src="assets/hero.svg" alt="DoViFixer: make Dolby Vision Profile 7 movies play right on Apple TV, NVIDIA Shield and Amazon Fire TV">

DoViFixer is a free Windows app for your 4K movie files. Most UHD Blu-ray movies use **Dolby Vision Profile 7**. Most streaming boxes cannot play Profile 7 correctly. DoViFixer checks each movie and converts the safe ones to **Profile 8.1**, which these boxes play in Dolby Vision.

<div class="cards" markdown>
<div markdown>
**:material-download: Install**

Download the app and let it set up the tools it needs.

[Install DoViFixer](install.md)
</div>
<div markdown>
**:material-shield-check: Before you convert**

Find out which movies are safe to convert, and why some are not.

[Read this first](before-you-convert.md)
</div>
<div markdown>
**:material-play-circle: Convert your movies**

Add movies, scan them and convert them in a few clicks.

[Use the app](guide/scan.md)
</div>
</div>

## Why Profile 7 looks wrong on streaming boxes

A Profile 7 video has three parts. Streaming boxes cannot use the third part, the enhancement layer. Some boxes fall back to a flatter HDR10 picture. Others drop the enhancement layer and play the rest.

<img class="illustration" src="assets/playback.svg" alt="Animation with simulated pictures: a Profile 7 movie on Apple TV 4K, NVIDIA Shield TV and Amazon Fire TV, then the same movie after conversion to Profile 8.1">

[How Dolby Vision works](dolby-vision.md){ .md-button } [What each streaming box does](devices.md){ .md-button }

## What DoViFixer does

<img class="illustration" src="assets/conversion.svg" alt="Animation: DoViFixer keeps the base layer, converts the RPU to Profile 8.1 and moves the enhancement layer into an optional .dovi backup">

- The base layer, audio, subtitles and chapters stay exactly as they are.
- The Dolby Vision metadata (RPU) becomes Profile 8.1.
- The converted movie is saved next to the original as `Movie - DV P8.1.mkv`. Your original is not changed.
- If you want, the enhancement layer goes into a small `.dovi` backup. With it, you can restore the original Profile 7 movie later.

## The app

<img class="screenshot" src="assets/screenshots/media.png" alt="The Media page: a list of movies with their Dolby Vision profile and enhancement layer type, and the details of the selected movie">

Press ++f1++ in the app to open this documentation.

## Credits

DoViFixer is inspired by [dovi_convert](https://github.com/cryptochrome/dovi_convert) and by [DoviConvert](https://github.com/myristate/dovi_convert), its C# / .NET rewrite for Windows. It runs [dovi_tool](https://github.com/quietvoid/dovi_tool), [FFmpeg](https://ffmpeg.org/), [MKVToolNix](https://mkvtoolnix.download/) and [MediaInfo](https://mediaarea.net/en/MediaInfo).
