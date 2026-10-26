"""Build a 9:16 rough cut of the YouTube Short with FFmpeg.

Usage:
    python make-rough-cut.py

Reads the app clips from clips/, the voiceover from clips/voice/, and the
frames of docs-site/assets/playback.svg from clips/svg/playback/ (made by
render-svg-frames.py) for the streaming-box pictures. Writes
clips/rough-cut.mp4; intermediate files go to clips/build/.

Times come from the voiceover: each section file starts where the previous
one ends, and the phrase times inside a section were measured with FFmpeg's
silencedetect. Regenerate the voiceover and the times must be re-measured.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CLIPS = HERE / "clips"
VOICE = CLIPS / "voice"
BUILD = CLIPS / "build"
OUTPUT = CLIPS / "rough-cut.mp4"
SVG_FRAMES = CLIPS / "svg" / "playback"
FFMPEG = os.environ.get("FFMPEG", shutil.which("ffmpeg") or str(
    Path.home() / "AppData/Local/Microsoft/WinGet/Packages"
    / "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-8.0.1-full_build/bin/ffmpeg.exe"))
LOGO = HERE.parent / "src/DoViFixer.ShellExtension/Package/Assets/Square44x44Logo.targetsize-256.png"

W, H, FPS = 1080, 1920, 30
BG = "0x0f1218"
ACCENT = "0x4cc2ff"
GOOD = "0x6ee787"
BAD = "0xff5c5c"

# Section start times (seconds) in the joined voiceover.
HOOK, PROBLEM, SOLUTION, CTA = 0.0, 3.2213, 18.1973, 41.536
END = 46.997

# App window crops in the 2880x1520 recordings: (columns, y, h), where each column is
# (x, w). Columns are placed side by side, so the text stays readable on a phone.
FILE, FORMAT, STATUS = (0, 440), (700, 340), (1040, 340)
LIST = ([FILE, FORMAT], 240, 600)   # movie list with their Dolby Vision profiles
SCAN = ([FILE, STATUS], 560, 560)    # the promo movies appearing at the end of the list
ROWS = ([FILE, STATUS], 700, 560)    # the promo movies while inspecting and converting
RESULT = ([FILE, STATUS], 860, 460)  # originals next to their DV P8.1 outputs
DETAILS = ([(1990, 870)], 620, 530)  # original-vs-result details panel

# playback.svg regions in SVG units: (x, y, w, h). The frames are rendered at SVG_SCALE pixels
# per unit. Boxes: 0 Apple TV, 1 NVIDIA Shield, 2 Fire TV.
SVG_SCALE = 4
panel = lambda box: (10 + 330 * box, 66, 320, 334)   # name, box, TV and status tag
screen = lambda box: (10 + 330 * box, 154, 320, 204)  # just the TV
OVERVIEW = (10, 18, 980, 445)                         # all three boxes
BEFORE_FRAME = 30  # 1s into the loop: every box shows its Profile 7 picture.
WIPE = 3.4  # 0.2s before the Apple TV picture starts turning into Dolby Vision; Fire TV finishes 2.2s later.

# Caption phrases: (start, end, text), relative to their section.
CAPTIONS = {
    HOOK: [(0.0, 1.21, "Your Dolby Vision movie?"),
           (1.57, 3.22, "It's NOT playing in Dolby Vision.")],
    PROBLEM: [(0.0, 3.18, "Most 4K Blu-ray movies use Dolby Vision Profile 7."),
              (3.67, 7.58, "But Apple TV, NVIDIA Shield and Fire TV can't play it properly!"),
              (8.06, 10.04, "You get a flat HDR10 picture,"),
              (10.29, 11.92, "or part of the video just gets dropped."),
              (12.33, 13.50, "You paid for the best picture..."),
              (13.94, 14.98, "and you're not seeing it!")],
    SOLUTION: [(0.0, 2.22, "Meet DoViFixer, a FREE Windows app!"),
               (2.64, 3.94, "Add your movies and hit Scan."),
               (4.29, 7.16, "It checks every file and tells you which ones are safe to convert."),
               (7.71, 10.35, "Then one click turns them into Profile 8.1,"),
               (10.57, 12.69, "real Dolby Vision on your streaming box!"),
               (13.11, 14.89, "The 4K picture stays bit for bit."),
               (15.39, 17.65, "Audio, subtitles, chapters: untouched."),
               (17.95, 19.79, "And your original file? Never changed."),
               (20.14, 20.72, "Want to go back?"),
               (21.18, 23.16, "Keep a tiny backup and restore it anytime!")],
    CTA: [(0.0, 2.02, "You deserve to watch in perfect quality."),
          (2.41, 3.50, "Link in the description."),
          (3.94, 5.46, "Go fix your movie library!")],
}

_text_files = 0


def run(args: list[str]) -> None:
    subprocess.run([FFMPEG, "-y", "-hide_banner", "-loglevel", "error", *args], cwd=BUILD, check=True)


def text(value: str, start: float, end: float | None, y: str, size: int,
         color: str = "white", font: str = "bold.ttf", x: str = "(w-tw)/2") -> str:
    """A drawtext filter that fades in at start; text goes through a file to avoid escaping."""
    global _text_files
    _text_files += 1
    name = f"text{_text_files:03}.txt"
    (BUILD / name).write_text(value, encoding="utf-8")
    window = f"between(t,{start:.3f},{end:.3f})" if end is not None else f"gte(t,{start:.3f})"
    return (f"drawtext=fontfile={font}:textfile={name}:expansion=none:fontsize={size}:fontcolor={color}"
            f":borderw=5:bordercolor=black@0.85:x={x}:y={y}:enable='{window}'"
            f":alpha='min(1,(t-{start:.3f})/0.15)'")


def simulated() -> str:
    return text("Simulated picture", 0, None, "h-90", 34, "white@0.75", "regular.ttf", "40")


def render(name: str, duration: float, inputs: list[str], graph: str, texts: list[str]) -> str:
    """Render one segment: graph must end in [v]; the texts are drawn on top."""
    chain = ",".join(texts) if texts else "null"
    run([*inputs, "-filter_complex", f"{graph};[v]{chain},format=yuv420p[out]", "-map", "[out]",
         "-t", f"{duration:.3f}", "-r", str(FPS), "-c:v", "libx264", "-crf", "18", "-preset", "medium",
         f"{name}.mp4"])
    return f"{name}.mp4"


def clip_graph(crop, speed: float = 1.0) -> str:
    """Crop columns of the app window, scale them to the frame width, and lay them over a blurred copy."""
    columns, y, h = crop
    parts = [f"[0:v]setpts=PTS/{speed},fps={FPS},split={len(columns)}" + "".join(f"[s{i}]" for i in range(len(columns)))]
    parts += [f"[s{i}]crop={w}:{h}:{x}:{y}[c{i}]" for i, (x, w) in enumerate(columns)]
    stack = "".join(f"[c{i}]" for i in range(len(columns)))
    parts.append(f"{stack}hstack=inputs={len(columns)}" if len(columns) > 1 else f"{stack}null")
    return framed(";".join(parts))


def framed(graph: str, out: str = "v") -> str:
    """Scale the graph's last output to the frame width and lay it over a blurred copy."""
    return (graph + f",scale={W}:-2,split[fg][blur];"
            f"[blur]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=40,"
            f"eq=brightness=-0.3[bg];[bg][fg]overlay=0:(H-h)/2-60[{out}]")


def clip(name: str, duration: float, source: str, start: float, crop, texts, speed: float = 1.0) -> str:
    inputs = ["-ss", f"{start}", "-i", str(CLIPS / source)]
    return render(name, duration, inputs, clip_graph(crop, speed), texts)


def svg(name: str, duration: float, region, texts, start: float | None = None, zoom: float = 0.06) -> str:
    """A region of playback.svg: a slowly zooming still of the Profile 7 pictures, or, with
    start, the animation from that second of the loop."""
    x, y, w, h = (v * SVG_SCALE for v in region)
    crop = f"[0:v]crop={w}:{h}:{x}:{y}"
    if start is None:
        frames = round(duration * FPS)
        graph = framed(crop, "still") + (
            f";[still]scale={W * 2}:{H * 2},zoompan=z='1+{zoom}*on/{frames}':d=1"
            f":x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':s={W}x{H}:fps={FPS}[v]")
        inputs = ["-loop", "1", "-i", str(SVG_FRAMES / f"{BEFORE_FRAME:04}.png")]
    else:
        graph = framed(crop)
        inputs = ["-framerate", str(FPS), "-start_number", str(round(start * FPS)), "-i", str(SVG_FRAMES / "%04d.png")]
    # Every playback.svg picture is a simulation.
    return render(name, duration, inputs, graph, [*texts, simulated()])


def card(name: str, duration: float, texts, logo: bool = False) -> str:
    inputs = ["-f", "lavfi", "-i", f"color=c={BG}:s={W}x{H}:r={FPS}:d={duration}"]
    if logo:
        inputs += ["-loop", "1", "-i", str(LOGO)]
        graph = "[1:v]scale=360:360[logo];[0:v][logo]overlay=(W-w)/2:520[v]"
    else:
        graph = "[0:v]null[v]"
    return render(name, duration, inputs, graph, texts)


def captions() -> str:
    """Bottom captions, with long phrases split into chunks timed by their length."""
    filters = []
    for offset, phrases in CAPTIONS.items():
        for start, end, phrase in phrases:
            words, chunks, current = phrase.split(), [], []
            for word in words:
                if current and len(" ".join(current + [word])) > 24:
                    chunks.append(" ".join(current))
                    current = []
                current.append(word)
            chunks.append(" ".join(current))
            total = sum(len(c) for c in chunks)
            t = offset + start
            for chunk in chunks:
                length = (end - start) * len(chunk) / total
                filters.append(text(chunk, t, t + length, "1540", 70, "white"))
                t += length
    return ",".join(filters)


def main() -> int:
    BUILD.mkdir(parents=True, exist_ok=True)
    shutil.copy(Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/segoeuib.ttf", BUILD / "bold.ttf")
    shutil.copy(Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/segoeui.ttf", BUILD / "regular.ttf")
    shutil.copy(Path(os.environ.get("WINDIR", "C:/Windows")) / "Fonts/seguisym.ttf", BUILD / "symbol.ttf")

    p, s = PROBLEM, SOLUTION
    # Shown from the start of a segment that continues the previous one, without fading in again.
    held = lambda value, y, size, color="white": text(value, -1, None, y, size, color)
    segments = [
        # Hook: the flat Apple TV picture.
        svg("01-hook", PROBLEM - HOOK, screen(0), [
            text("Dolby Vision?", 0, None, "300", 120),
            text("NOT really.", 1.57, None, "450", 120, BAD)]),
        # Problem.
        clip("02-profile7", 3.67, "01-media-page.mp4", 0.5, LIST, [
            text("Most 4K Blu-rays use", 0, None, "220", 72),
            text("Dolby Vision Profile 7", 0.3, None, "320", 84, ACCENT)]),
        # Each box as the voiceover names it.
        svg("03-apple-tv", 1.17, panel(0), [text("Can't play Profile 7 right", 0, None, "180", 76)]),
        svg("03-shield", 1.23, panel(1), [held("Can't play Profile 7 right", "180", 76)]),
        svg("03-fire-tv", (p + 8.06) - (p + 6.07), panel(2), [held("Can't play Profile 7 right", "180", 76)]),
        svg("04-flat", 2.23, screen(2), [text("Flat HDR10 picture", 0, None, "300", 96)]),
        svg("04-dropped", 2.04, screen(1), [
            text("or part of the video", 0, None, "300", 80),
            text("dropped", 0, None, "410", 96, BAD)]),
        svg("04-not-seeing", SOLUTION - (p + 12.33), OVERVIEW, [
            text("You paid for the best picture", 0, None, "360", 68),
            text("You're not seeing it.", 1.61, None, "1250", 100, BAD)]),
        # Solution.
        card("05-meet", 2.637, [
            text("DoViFixer", 0, None, "980", 140),
            text("FREE Windows app", 0.9, None, "1160", 84, GOOD)], logo=True),
        clip("06-scan", (s + 4.29) - (s + 2.637), "02-scan-trimmed.mp4", 0.8, SCAN, [
            text("Add movies. Hit Scan.", 0, None, "260", 84)]),
        clip("07-inspect", 1.9, "03-inspect-trimmed.mp4", 0.5, ROWS, [
            text("Checks every file first", 0, None, "300", 84)]),
        clip("08-safe", (s + 7.71) - (s + 4.29) - 1.9, "04-convert-trimmed.mp4", 0.0, ROWS, [
            text("Checks every file first", 0, None, "300", 84),
            text("Tells you what's safe", 0, None, "420", 72, GOOD)]),
        clip("09-convert", (s + 10.57) - (s + 7.71), "04-convert-trimmed.mp4", 1.5, ROWS, [
            text("One click", 0, None, "260", 96),
            text("Profile 7 \u2192 8.1", 0.4, None, "390", 96, ACCENT, "symbol.ttf")], speed=3.5),
        # The boxes' pictures turning into Dolby Vision, one after another.
        svg("09-dolby-vision", (s + 13.11) - (s + 10.57), OVERVIEW, [
            text("Real Dolby Vision", 0, None, "300", 96, GOOD),
            text("on your streaming box", 0.3, None, "420", 72)], start=WIPE),
        clip("10-bit-for-bit", (s + 17.95) - (s + 13.11), "04-convert-trimmed.mp4", 22, DETAILS, [
            text("4K picture: bit for bit", 0, None, "260", 84),
            text("Audio. Subtitles. Chapters.", 2.28, None, "380", 68, GOOD)]),
        clip("11-original", (s + 21.18) - (s + 17.95), "04-convert-trimmed.mp4", 24, RESULT, [
            text("Your original:", 0, None, "260", 84),
            text("never changed", 0.9, None, "370", 96, GOOD)]),
        card("12-restore", CTA - (s + 21.18), [
            text("Want to go back?", 0, None, "620", 96),
            text("Keep a tiny .dovi backup", 0.3, None, "820", 76, ACCENT),
            text("Restore anytime", 0.9, None, "940", 76, ACCENT)]),
    ]

    # Call to action: the hook's flat Apple TV picture turns into Dolby Vision.
    segments.append(svg("13-cta", END - CTA, screen(0), [
        text("You deserve", 0.3, None, "260", 96),
        text("perfect quality.", 0.6, None, "380", 110, GOOD),
        text("DoViFixer \u00b7 Free for Windows", 2.41, None, "1300", 64),
        text("Link in the description", 3.94, None, "1400", 64, ACCENT)], start=WIPE))

    (BUILD / "segments.txt").write_text("".join(f"file '{name}'\n" for name in segments), encoding="utf-8")
    run(["-f", "concat", "-safe", "0", "-i", "segments.txt", "-c", "copy", "joined.mp4"])

    (BUILD / "voice.txt").write_text(
        "".join(f"file '{(VOICE / n).as_posix()}'\n" for n in ("1-hook.wav", "2-problem.wav", "3-solution.wav", "4-cta.wav")),
        encoding="utf-8")
    run(["-f", "concat", "-safe", "0", "-i", "voice.txt", "-c", "copy", "voice.wav"])

    run(["-i", "joined.mp4", "-i", "voice.wav", "-filter_complex", f"[0:v]{captions()},format=yuv420p[v]",
         "-map", "[v]", "-map", "1:a", "-c:v", "libx264", "-crf", "18", "-preset", "medium",
         "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-shortest", str(OUTPUT)])
    print(OUTPUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
