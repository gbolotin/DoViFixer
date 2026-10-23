"""Generates the File Explorer package's logo images in src/DoViFixer.ShellExtension/Package/Assets.

Windows 11's right-click menu shows the package logo at 16 to 32 pixels. The app icon's interlaced double band blurs
at those sizes, so they show the same knot drawn as a single line, from its geometry: the outlines of three lenses,
each the overlap of two circles that pass through each other's centres. Larger sizes use the icon's own frames.
Needs Pillow: python tools/package-icons.py
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ICON = ROOT / "src/DoViFixer.App/dovifixer.ico"
ASSETS = ROOT / "src/DoViFixer.ShellExtension/Package/Assets"
COLOUR = (124, 58, 237)
# Target size: stroke width in pixels for the single-line knot; sizes without one use the icon's frame.
LINE_KNOT = {16: 1.5, 20: 1.7, 24: 1.9, 30: 2.2, 32: 2.3}
ICON_FRAMES = [40, 48, 64, 96, 256]


def knot_lines():
    """Each circle's arcs that lie inside another circle: together, the outlines of the three lenses."""
    radius = 1.0
    distance = radius / math.sqrt(3)
    centres = [(distance * math.cos(math.radians(a)), -distance * math.sin(math.radians(a))) for a in (270, 30, 150)]
    lines = []
    for index, (cx, cy) in enumerate(centres):
        others = [c for other, c in enumerate(centres) if other != index]
        run = []
        for step in range(3601):
            angle = 2 * math.pi * step / 3600
            point = (cx + radius * math.cos(angle), cy + radius * math.sin(angle))
            if any(math.hypot(point[0] - ox, point[1] - oy) < radius - 1e-9 for ox, oy in others):
                run.append(point)
            elif run:
                lines.append(run)
                run = []
        if run:
            lines.append(run)
    return lines


def line_knot(size, stroke, margin=0.4, supersample=16):
    lines = knot_lines()
    xs = [x for line in lines for x, _ in line]
    ys = [y for line in lines for _, y in line]
    big = size * supersample
    span = max(max(xs) - min(xs), max(ys) - min(ys))
    scale = (big - 2 * (margin + stroke / 2) * supersample) / span
    left = (big - (max(xs) - min(xs)) * scale) / 2 - min(xs) * scale
    top = (big - (max(ys) - min(ys)) * scale) / 2 - min(ys) * scale
    mask = Image.new("L", (big, big), 0)
    draw = ImageDraw.Draw(mask)
    width = max(1, round(stroke * supersample))
    for line in lines:
        points = [(left + x * scale, top + y * scale) for x, y in line]
        draw.line(points, fill=255, width=width, joint="curve")
        for px, py in (points[0], points[-1]):
            draw.ellipse([px - width / 2, py - width / 2, px + width / 2, py + width / 2], fill=255)
    image = Image.new("RGBA", (size, size), COLOUR + (0,))
    image.putalpha(mask.resize((size, size), Image.BOX))
    return image


def icon_frame(size):
    icon = Image.open(ICON)
    icon.size = (size, size)
    return icon.convert("RGBA")


def save(image, name):
    image.save(ASSETS / name, optimize=True)


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    for old in ASSETS.glob("Square44x44Logo.targetsize-*.png"):
        old.unlink()
    for size in sorted([*LINE_KNOT, *ICON_FRAMES]):
        image = line_knot(size, LINE_KNOT[size]) if size in LINE_KNOT else icon_frame(size)
        # Plated for the Start menu, unplated for File Explorer and the taskbar in dark and light themes.
        for suffix in ("", "_altform-unplated", "_altform-lightunplated"):
            save(image, f"Square44x44Logo.targetsize-{size}{suffix}.png")
    large = icon_frame(256)
    save(large.resize((44, 44), Image.LANCZOS), "Square44x44Logo.png")
    save(large.resize((150, 150), Image.LANCZOS), "Square150x150Logo.png")
    save(large.resize((50, 50), Image.LANCZOS), "StoreLogo.png")


if __name__ == "__main__":
    main()
