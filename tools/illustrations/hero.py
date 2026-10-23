"""Generates hero.svg: the README and documentation banner with the mascot."""
import sys
from common import FONT, background, background_defs, mascot

W, H = 1000, 300


def build():
    css = "\n    ".join([
        f"text {{ font-family: {FONT}; font-style: italic; font-weight: 700; fill: #ffffff; }}",
        ".title { font-size: 68px; } .tagline { font-size: 22px; } .chip { font-size: 15px; font-style: normal; }",
        ".hop { animation: hop 1.2s ease-in-out infinite alternate; }",
        "@keyframes hop { from { transform: translateY(0); } to { transform: translateY(-10px); } }",
        ".spark { animation: spark 2.4s ease-in-out infinite; transform-box: fill-box; transform-origin: center; }",
        ".spark:nth-of-type(2) { animation-delay: -0.8s; } .spark:nth-of-type(3) { animation-delay: -1.6s; }",
        "@keyframes spark { 0%, 100% { opacity: 0; transform: scale(0.4); } 50% { opacity: 1; transform: scale(1); } }",
        "@media (prefers-reduced-motion: reduce) { .hop, .spark { animation: none; } }",
    ])
    chip_svg, x = "", 0
    for text in ("Windows app", "Checks every file first", "Keeps your original"):
        width = round(len(text) * 8.8 + 32)
        chip_svg += f'<g transform="translate({x} 0)"><rect width="{width}" height="32" rx="16" fill="#ffffff" opacity="0.22"/><text x="15" y="21" class="chip">{text}</text></g>'
        x += width + 10
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="title desc">
  <title id="title">DoViFixer</title>
  <desc id="desc">DoViFixer makes Dolby Vision Profile 7 movies play right on Apple TV, NVIDIA Shield and Amazon Fire TV.</desc>
  <defs>{background_defs()}</defs>
  <style>
    {css}
  </style>{background(W, H)}
  <g transform="translate(70 70) scale(1.7)">
    <g class="hop">{mascot(40, 40)}</g>
  </g>
  <g fill="#ffffff">
    <path class="spark" d="M70 70 l4 12 l12 4 l-12 4 l-4 12 l-4 -12 l-12 -4 l12 -4 Z"/>
    <path class="spark" d="M250 60 l3 9 l9 3 l-9 3 l-3 9 l-3 -9 l-9 -3 l9 -3 Z"/>
    <path class="spark" d="M240 230 l3 9 l9 3 l-9 3 l-3 9 l-3 -9 l-9 -3 l9 -3 Z"/>
  </g>
  <g filter="url(#textShadow)">
    <text x="300" y="120" class="title">DoViFixer</text>
    <text x="304" y="162" class="tagline">Make Dolby Vision Profile 7 movies play right</text>
    <text x="304" y="190" class="tagline">on Apple TV, NVIDIA Shield and Amazon Fire TV.</text>
  </g>
  <g transform="translate(304 218)">{chip_svg}</g>
</svg>
"""


if __name__ == "__main__":
    with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as out:
        out.write(build())
