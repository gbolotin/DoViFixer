"""Render an animated docs-site SVG to PNG frames with Microsoft Edge, for use in the promo video.

FFmpeg cannot play CSS animations in an SVG, so this pauses every animation, seeks it to each
frame's time and takes a screenshot. Frame files are numbered by their time in the loop
(frame 0123 is 123/fps seconds in), so a part of the loop can be rendered on its own.
Run it with Playwright, using the installed Edge:

    uv run --no-project --with playwright marketing/render-svg-frames.py docs-site/assets/playback.svg marketing/clips/svg/playback
"""

import argparse
import asyncio
from pathlib import Path

from playwright.async_api import async_playwright


async def render(svg: Path, out: Path, start: float, end: float, period: float, fps: int, scale: float) -> None:
    out.mkdir(parents=True, exist_ok=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(channel="msedge")
        page = await browser.new_page(device_scale_factor=scale)
        await page.goto(svg.resolve().as_uri())
        size = await page.evaluate("""() => {
            const box = document.documentElement.viewBox.baseVal;
            return { width: Math.ceil(box.width), height: Math.ceil(box.height) };
        }""")
        await page.set_viewport_size(size)
        await page.evaluate("() => document.getAnimations().forEach(a => a.pause())")
        first, last = round(start * fps), round(end * fps)
        for frame in range(first, last):
            # Seek one loop later, so animations with a delay have already started.
            ms = (period + frame / fps) * 1000
            await page.evaluate("ms => document.getAnimations().forEach(a => a.currentTime = ms)", ms)
            await page.screenshot(path=out / f"{frame:04}.png")
        await browser.close()
    print(f"frames {first}-{last - 1} in {out}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("svg", type=Path)
    parser.add_argument("out", type=Path, help="folder for the numbered PNG frames")
    parser.add_argument("--start", type=float, default=0.0, help="first second of the loop to render")
    parser.add_argument("--end", type=float, default=12.0, help="second of the loop to stop at")
    parser.add_argument("--period", type=float, default=12.0, help="loop length in seconds (12 for playback.svg)")
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--scale", type=float, default=4.0, help="device pixels per SVG pixel")
    args = parser.parse_args()
    asyncio.run(render(args.svg, args.out, args.start, args.end, args.period, args.fps, args.scale))


if __name__ == "__main__":
    main()
