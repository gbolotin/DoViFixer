# How to make the YouTube Short

This turns [youtube-shorts-script.md](youtube-shorts-script.md) into a 47-second vertical video, using only free tools on Windows 11: Snipping Tool or FFmpeg to record, and Microsoft Clipchamp to edit.

Recorded footage lives in `marketing/clips/`, which git ignores. Copy it to another machine yourself if you need it there.

## 1. Footage you already have

| Clip | Length | Shows | Script section |
| --- | --- | --- | --- |
| `clips/01-media-page.mp4` | 17s | Clicking through movies; the details panel shows Profile 7 FEL, risk and original-vs-result | Problem / Solution intro |
| `clips/02-scan-trimmed.mp4` | 26s | Three movies added, scanned in about 1 second, then the inspection starting | "hit Scan" |
| `clips/03-inspect-trimmed.mp4` | 15s | Inspection progress bar and queued files | "checks every file" |
| `clips/04-convert-trimmed.mp4` | 30s | Batch convert with three progress bars, then "Verifying" and "Converted" (3 movies in 14 seconds) | "one click turns them into Profile 8.1" |

`clips/raw/` holds the untrimmed takes.

All clips are captures of the app window only, 16:9 at about 2880×1520. The "Update to 0.1.67" button shows at the bottom right; crop it out.

## 2. Footage you still need

- **Hook and before/after picture:** `docs-site/assets/playback.svg` shows a Profile 7 movie on Apple TV, NVIDIA Shield and Fire TV, then the same movie in Dolby Vision after conversion. [render-svg-frames.py](render-svg-frames.py) turns it into PNG frames (see [SVG frames](#svg-frames)); to use it in Clipchamp, turn the frames into a video with `ffmpeg -framerate 30 -start_number 90 -i clips/svg/playback/%04d.png -c:v libx264 -pix_fmt yuv420p clips/playback.mp4`, or screen-record the SVG in a browser (Snipping Tool: **Win+Shift+R**). `hero.svg` and `conversion.svg` work the same way. Add a small "Simulated" caption to the before/after shot.
- **Logo end card:** take the logo from the docs-site assets.

Show the streaming boxes as text (Apple TV, NVIDIA Shield, Fire TV), not their logos, to avoid trademark problems.

## 3. Voiceover

Use one of these:

- **Kokoro TTS (installed, free, runs locally):** natural neural voices under the Apache licence, so they're fine for a public promo. Nothing leaves your PC. See [Kokoro voiceover](#kokoro-voiceover).
- **Record yourself** on your phone, reading the script fast and with energy. It usually takes 2–3 tries to fit 47 seconds.
- **Use an AI voice:** in Clipchamp, **Record & create → Text to speech**. Paste each script section, pick an upbeat voice, and set about 1.1× speed.

## 4. Edit in Clipchamp

1. Create a new video and set the aspect ratio to **9:16**.
2. Put the voiceover on the timeline first.
3. Line up the clips with the script's timestamps:
   - 0–3s: hook (dull picture from `playback.svg`)
   - 3–18s: problem (`01-media-page.mp4`, then each box from `playback.svg` as the voiceover names it)
   - 18–41s: solution (`02-scan-trimmed.mp4` → `03-inspect-trimmed.mp4` → `04-convert-trimmed.mp4`, with the `playback.svg` pictures turning into Dolby Vision at "real Dolby Vision on your streaming box")
   - 41–47s: call to action and logo
4. **Zoom into each clip.** The full window is too small to read on a phone. Crop to the movie list, the progress bars, or the details panel.
5. Add the pop-up text from the script's 📱 lines, such as "FREE", "Original stays safe ✅" and "Profile 7 → 8.1 ⚡".
6. Turn on **Captions → Auto-captions**, since most Shorts are watched muted.
7. Add quiet background music from Clipchamp's library, plus a "whoosh" at the before/after switch (about 39s).
8. Export at **1080p**.

## 5. Upload

Upload from the YouTube app on your phone, or on desktop. A vertical video under 3 minutes is posted as a Short automatically. Use a title like *"Your 4K movies aren't really Dolby Vision 😳"* and put the download link in the description: <https://github.com/gbolotin/DoViFixer/releases/latest>.

## Rough cut

[make-rough-cut.py](make-rough-cut.py) builds a finished 47-second, 1080×1920 rough cut with FFmpeg: the Kokoro voiceover, the app clips cropped to their readable columns, the pop-up text from the script, burned-in captions, and the streaming-box pictures from `docs-site/assets/playback.svg`, labelled "Simulated picture". It writes `clips/rough-cut.mp4`, with intermediate files in `clips/build/`. Render the SVG frames first (see below), then run it; it takes about 30 seconds:

```powershell
& "$env:USERPROFILE\kokoro-tts\.venv\Scripts\python.exe" make-rough-cut.py
```

- **SVG shots:** the hook, the problem section, "real Dolby Vision on your streaming box" and the call to action all use `playback.svg`: one box at a time, a close-up of a TV, or all three boxes. `panel()`, `screen()` and `OVERVIEW` are regions in SVG units; `WIPE` is the second of the animation loop where the pictures start turning into Dolby Vision. It's the project's own artwork, so it's safe to post.
- **Timings:** the section and phrase times at the top of the script were measured from the current voiceover with FFmpeg's `silencedetect`. If you regenerate the voiceover, measure them again:

  ```powershell
  ffmpeg -i clips/voice/2-problem.wav -af silencedetect=n=-35dB:d=0.18 -f null -
  ```

- **Crops:** `LIST`, `SCAN`, `ROWS`, `RESULT` and `DETAILS` pick columns of the 2880×1520 app recordings and place them side by side. Re-recorded clips may need new coordinates.
- **Not included:** music and sound effects. Add them in Clipchamp or CapCut, along with any polish such as zoom animations.

### SVG frames

FFmpeg can't play an SVG's CSS animation, so [render-svg-frames.py](render-svg-frames.py) opens it in Microsoft Edge, pauses the animation, and screenshots each frame at 4× size. The rough cut needs frame 30 (the Profile 7 pictures) and 3–9.5 seconds of the 12-second loop (the change to Dolby Vision), about 180 MB in `clips/svg/playback/`. It uses [uv](https://docs.astral.sh/uv/) to fetch Playwright, and the installed Edge, so no browser download is needed. Run it from the repository root:

```powershell
uv run --no-project --with playwright marketing/render-svg-frames.py docs-site/assets/playback.svg marketing/clips/svg/playback --start 1 --end 1.04
uv run --no-project --with playwright marketing/render-svg-frames.py docs-site/assets/playback.svg marketing/clips/svg/playback --start 3 --end 9.5
```

Run it again whenever `playback.svg` changes.

## Kokoro voiceover

Kokoro is installed in `%USERPROFILE%\kokoro-tts`: a uv-managed Python 3.12 environment in `.venv` with `kokoro-onnx` and `soundfile`, plus the model files from the [kokoro-onnx releases](https://github.com/thewh1teagle/kokoro-onnx/releases/tag/model-files-v1.0) in `models\`. [make-voiceover.py](make-voiceover.py) wraps it.

Run these from the `marketing` folder:

```powershell
$py = "$env:USERPROFILE\kokoro-tts\.venv\Scripts\python.exe"
& $py make-voiceover.py --list-voices
& $py make-voiceover.py "Your Dolby Vision movie? It's NOT playing in Dolby Vision." clips/voice/hook.wav --voice am_michael --speed 1.1
& $py make-voiceover.py --file clips/voice/solution.txt clips/voice/solution.wav --voice am_michael
```

- **Voices:** names start with `a` for American or `b` for British English, then `f` or `m` for female or male. Good energetic picks are `am_michael`, `am_puck`, `af_heart` and `af_bella`. Samples of the hook line are in `clips/voice/hook-*.wav`.
- **Speed:** `--speed` defaults to 1.1. Raise it to about 1.2 if a section runs over its time slot in the script.
- **One file per section:** generate each script section (hook, problem, solution, call to action) as its own file. They're easier to line up with the clips, and you can redo one without touching the others.
- **Pronunciation:** if Kokoro says something wrong, spell it the way it sounds in the text you pass in, for example "Dee-oh-vee Fixer" for DoViFixer. Keep the real spelling in the on-screen captions.
- **Output:** `clips/` is git-ignored, so the generated audio stays local.

To reinstall Kokoro on another PC:

```powershell
uv venv --python 3.12 "$env:USERPROFILE\kokoro-tts\.venv"
uv pip install --python "$env:USERPROFILE\kokoro-tts\.venv\Scripts\python.exe" kokoro-onnx soundfile
```

Then download `kokoro-v1.0.onnx` and `voices-v1.0.bin` from the release page above into `%USERPROFILE%\kokoro-tts\models`.

## Re-recording the app footage

Use this when the UI changes, or to get rid of the update button.

1. Copy a few Profile 7 sample movies into a separate folder, for example `Downloads\DoViFixer-promo`, so conversions never touch the real files.
2. Build and start the app:

   ```powershell
   dotnet build src/DoViFixer.App/DoViFixer.App.csproj
   ```

   Then run `src\DoViFixer.App\bin\Debug\net10.0-windows10.0.19041.0\DoViFixer.App.exe`.

3. Find the app window's handle:

   ```powershell
   (Get-Process DoViFixer.App).MainWindowHandle
   ```

4. Record that window only. It is captured even when other windows cover it, but dialogs such as the folder picker are not captured. Change `-t` to set the length in seconds:

   ```powershell
   ffmpeg -f gdigrab -framerate 30 -i hwnd=<handle> -t 30 -c:v libx264 -preset ultrafast -crf 16 -pix_fmt yuv420p marketing/clips/raw/take.mp4
   ```

5. Trim a take, keeping the first N seconds:

   ```powershell
   ffmpeg -i marketing/clips/raw/take.mp4 -t 25 -c:v libx264 -crf 18 -pix_fmt yuv420p marketing/clips/take-trimmed.mp4
   ```

   Add `-ss <start>` before `-i` to start later.

Screen recording loads the CPU heavily. During the first session, one inspection failed with "FFmpeg: Configured path is invalid", probably because the app's 8-second FFmpeg version check timed out. It passed on **Retry analysis**. Keep a failed state out of the final cut.

## Clean up

The sample copies and their converted `- DV P8.1.mkv` outputs in `Downloads\DoViFixer-promo` take about 3.5 GB. Delete the folder when the video is done, and remove those movies from the app's list.
