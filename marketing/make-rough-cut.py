"""Build a 9:16 rough cut of the YouTube Short with FFmpeg.

Usage:
    python make-rough-cut.py

Reads the app clips from clips/, the voiceover from clips/voice/, and two
sample movies (for the before/after stills) from MOVIES. Writes
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
MOVIES = Path(os.environ.get("PROMO_MOVIES", Path.home() / "Downloads" / "DoViFixer-promo"))
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
    return (";".join(parts) + f",scale={W}:-2,split[fg][blur];"
            f"[blur]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=40,"
            f"eq=brightness=-0.3[bg];[bg][fg]overlay=0:(H-h)/2-60[v]")


def clip(name: str, duration: float, source: str, start: float, crop, texts, speed: float = 1.0) -> str:
    inputs = ["-ss", f"{start}", "-i", str(CLIPS / source)]
    return render(name, duration, inputs, clip_graph(crop, speed), texts)


def still(name: str, duration: float, image: str, texts, zoom: float = 0.06) -> str:
    frames = round(duration * FPS)
    graph = (f"[0:v]scale={W * 2}:{H * 2},zoompan=z='1+{zoom}*on/{frames}':d=1"
             f":x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':s={W}x{H}:fps={FPS}[v]")
    return render(name, duration, ["-loop", "1", "-i", image], graph, texts)


def card(name: str, duration: float, texts, logo: bool = False) -> str:
    inputs = ["-f", "lavfi", "-i", f"color=c={BG}:s={W}x{H}:r={FPS}:d={duration}"]
    if logo:
        inputs += ["-loop", "1", "-i", str(LOGO)]
        graph = "[1:v]scale=360:360[logo];[0:v][logo]overlay=(W-w)/2:520[v]"
    else:
        graph = "[0:v]null[v]"
    return render(name, duration, inputs, graph, texts)


def extract_stills() -> None:
    portrait = f"crop=1215:2160:(iw-1215)/2:0,scale={W}:{H}"
    for movie, at, stem in (("title_t06.mkv", 20, "t06"), ("title_t01.mkv", 30, "t01")):
        source = str(MOVIES / movie)
        # Untouched PQ frames look washed out on an SDR screen, which stands in for the "before".
        run(["-ss", str(at), "-i", source, "-frames:v", "1", "-vf",
             f"scale=in_color_matrix=bt2020:in_range=tv,{portrait},eq=saturation=0.75", f"dull-{stem}.png"])
        run(["-ss", str(at), "-i", source, "-frames:v", "1", "-vf",
             "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,"
             f"zscale=t=bt709:m=bt709:r=tv,format=yuv420p,{portrait},eq=saturation=1.3:contrast=1.08",
             f"vivid-{stem}.png"])


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
    extract_stills()

    p, s = PROBLEM, SOLUTION
    cross = lambda y: text("\u2715", 0, None, y, 110, BAD, "symbol.ttf", "830")
    segments = [
        # Hook: a dull "before" picture.
        still("01-hook", PROBLEM - HOOK, "dull-t06.png", [
            text("Dolby Vision?", 0, None, "300", 120),
            text("NOT really.", 1.57, None, "450", 120, BAD), simulated()]),
        # Problem.
        clip("02-profile7", 3.67, "01-media-page.mp4", 0.5, LIST, [
            text("Most 4K Blu-rays use", 0, None, "220", 72),
            text("Dolby Vision Profile 7", 0.3, None, "320", 84, ACCENT)]),
        card("03-boxes", (p + 8.06) - (p + 3.67), [
            text("Can't play Profile 7 right", 0, None, "360", 76),
            text("Apple TV", 0.0, None, "700", 100, x="780-tw"), cross("700"),
            text("NVIDIA Shield", 1.17, None, "900", 100, x="780-tw"),
            text("\u2715", 1.17, None, "900", 110, BAD, "symbol.ttf", "830"),
            text("Fire TV", 2.4, None, "1100", 100, x="780-tw"),
            text("\u2715", 2.4, None, "1100", 110, BAD, "symbol.ttf", "830")]),
        still("04-flat", SOLUTION - (p + 8.06), "dull-t01.png", [
            text("Flat HDR10 picture", 0, None, "300", 96),
            text("or part of the video dropped", 2.23, None, "430", 64),
            text("You're not seeing it.", 4.27, None, "900", 100, BAD), simulated()]),
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
        clip("09-convert", (s + 13.11) - (s + 7.71), "04-convert-trimmed.mp4", 1.5, ROWS, [
            text("One click", 0, None, "260", 96),
            text("Profile 7 \u2192 8.1", 0.4, None, "390", 96, ACCENT, "symbol.ttf")], speed=3.5),
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

    # Call to action: wipe from the dull picture to the vivid one.
    cta = END - CTA
    graph = (f"[0:v]scale={W}:{H},fps={FPS},trim=duration={cta}[a];[1:v]scale={W}:{H},fps={FPS},trim=duration={cta}[b];"
             f"[a][b]xfade=transition=wiperight:duration=0.8:offset=0.3[v]")
    segments.append(render("13-cta", cta, ["-loop", "1", "-t", f"{cta}", "-i", "dull-t06.png",
                                            "-loop", "1", "-t", f"{cta}", "-i", "vivid-t06.png"], graph, [
        text("You deserve", 0.3, None, "260", 96),
        text("perfect quality.", 0.6, None, "380", 110, GOOD),
        text("DoViFixer \u00b7 Free for Windows", 2.41, None, "1180", 64),
        text("Link in the description", 3.94, None, "1280", 64, ACCENT), simulated()]))

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
