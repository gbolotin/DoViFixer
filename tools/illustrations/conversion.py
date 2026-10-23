"""Generates conversion.svg: DoViFixer keeps the base layer, converts the RPU to Profile 8.1 and moves the enhancement layer into a .dovi backup."""
import sys
from common import FONT, background, background_defs, keyframes, scene_symbol

W, H = 1000, 500
T = 14

SHEET = "matrix(0.62 0 0 0.8 0 0)"
TRAPEZOID = "M0 0 L400 26 L400 199 L0 225 Z"


def sheet(css_class, x, y, content):
    return f"""
  <g transform="translate({x} {y})">
    <g class="{css_class}">
      <g transform="{SHEET}" filter="url(#sheetShadow)">
        <g clip-path="url(#trapezoid)">{content}</g>
        <path d="{TRAPEZOID}" fill="none" stroke="#ffffff" stroke-width="3"/>
      </g>
    </g>
  </g>"""


def build():
    css = "\n    ".join([
        f"text {{ font-family: {FONT}; font-style: italic; font-weight: 700; }}",
        ".big { font-size: 26px; fill: #ffffff; } .mid { font-size: 18px; fill: #ffffff; } .sub { font-size: 15px; fill: #ffffff; }",
        ".tag { font-size: 14px; font-style: normal; font-weight: 700; }",
        ".caption { font-size: 15px; fill: #fff6e6; font-style: normal; font-weight: 600; }",
        ".a { animation-duration: %ds; animation-iteration-count: infinite; animation-timing-function: ease-in-out; }" % T,
        ".src { animation-name: src; } .el { animation-name: el; } .box { animation-name: box; }",
        ".out { animation-name: out; } .t-keep { animation-name: t-keep; } .t-rpu { animation-name: t-rpu; } .t-el { animation-name: t-el; } .t-out { animation-name: t-out; } .restore { animation-name: restore; }",
        ".gear { animation: spin 3s linear infinite; transform-box: fill-box; transform-origin: center; }",
        "@keyframes spin { to { transform: rotate(360deg); } }",
        keyframes("src", T, [((0, 12.6), "opacity: 1;"), (T, "opacity: 0;")]),
        keyframes("el", T, [((0, 2), "opacity: 1; transform: translate(0, 0);"), ((3.6, 12.6), "opacity: 1; transform: translate(268px, 190px) scale(0.5);"), (T, "opacity: 0; transform: translate(268px, 190px) scale(0.5);")]),
        keyframes("box", T, [((0, 2.6), "opacity: 0.35;"), ((3.6, 12.6), "opacity: 1;"), (T, "opacity: 0.35;")]),
        keyframes("out", T, [((0, 6.4), "opacity: 0; transform: translate(-60px, 0);"), ((7.6, 12.6), "opacity: 1; transform: translate(0, 0);"), (T, "opacity: 0; transform: translate(0, 0);")]),
        keyframes("t-keep", T, [((0, 0.4), "opacity: 0;"), ((1, 12.6), "opacity: 1;"), (T, "opacity: 0;")]),
        keyframes("t-el", T, [((0, 2.4), "opacity: 0;"), ((3, 12.6), "opacity: 1;"), (T, "opacity: 0;")]),
        keyframes("t-rpu", T, [((0, 4.6), "opacity: 0;"), ((5.2, 12.6), "opacity: 1;"), (T, "opacity: 0;")]),
        keyframes("t-out", T, [((0, 7.6), "opacity: 0;"), ((8.2, 12.6), "opacity: 1;"), (T, "opacity: 0;")]),
        keyframes("restore", T, [((0, 9), "opacity: 0; stroke-dashoffset: 0;"), ((9.6, 12.6), "opacity: 1; stroke-dashoffset: -60;"), (T, "opacity: 0; stroke-dashoffset: -80;")]),
        "@media (prefers-reduced-motion: reduce) { .a, .gear { animation: none; } .el { transform: translate(268px, 190px) scale(0.5); } }",
    ])
    base = """<use href="#scene" width="400" height="225"/>"""
    rpu = """<rect width="400" height="225" fill="#f5f5f5" opacity="0.3"/>
          <path d="M20 170 L60 120 L100 140 L140 70 L180 110 L220 60 L260 120 L300 90 L340 130 L380 80" fill="none" stroke="#ffffff" stroke-width="5" opacity="0.85"/>"""
    el = """<rect width="400" height="225" fill="#9b9b9b" opacity="0.4"/>
          <use href="#scene" width="400" height="225" filter="url(#grey)" opacity="0.85"/>"""
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="title desc">
  <title id="title">What DoViFixer does to a Profile 7 video</title>
  <desc id="desc">Animation. DoViFixer keeps the base layer exactly as it is, converts the RPU metadata from Profile 7 to Profile 8.1 and takes the enhancement layer out. You can keep the enhancement layer in a small .dovi backup file and restore the original Profile 7 video later. The result is a Profile 8.1 video that streaming boxes play in Dolby Vision.</desc>
  <defs>{background_defs()}{scene_symbol()}
    <clipPath id="trapezoid"><path d="{TRAPEZOID}"/></clipPath>
    <filter id="grey"><feColorMatrix type="saturate" values="0"/></filter>
    <marker id="arrow" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0 0 L10 5 L0 10 Z" fill="#ffffff"/></marker>
  </defs>
  <style>
    {css}
  </style>{background(W, H)}

  <!-- Source: a Profile 7 file -->
  <g filter="url(#textShadow)"><text x="40" y="58" class="big">Profile 7 file</text></g>
  <g class="a src">
    {sheet("", 40, 100, base)}
    {sheet("", 80, 108, rpu)}
  </g>
  {sheet("a el", 120, 116, el)}
  <g transform="translate(70 315)" filter="url(#textShadow)">
    <text class="tag" fill="#ffffff"><tspan x="0">BL</tspan><tspan x="60">RPU</tspan><tspan x="125">EL</tspan></text>
  </g>
  <g transform="translate(120 82)"><rect x="-4" y="-17" width="44" height="24" rx="12" fill="#c0392b"/><text x="18" y="0" class="tag" fill="#ffffff" text-anchor="middle">P7</text></g>

  <!-- DoViFixer in the middle -->
  <g transform="translate(470 200)">
    <circle r="46" fill="#ffffff" opacity="0.18"/>
    <g class="gear"><path d="M0 -34 L8 -34 L10 -24 L18 -20 L27 -26 L33 -20 L27 -11 L31 -3 L41 0 L41 8 L31 10 L27 18 L33 27 L27 33 L18 27 L10 31 L8 41 L0 41 L-2 31 L-10 27 L-19 33 L-25 27 L-19 18 L-23 10 L-33 8 L-33 0 L-23 -3 L-19 -11 L-25 -20 L-19 -26 L-10 -20 L-2 -24 Z" transform="translate(-4 -4)" fill="#ffffff"/><circle r="12" fill="#ffa412"/></g>
    <text y="76" class="mid" text-anchor="middle" filter="url(#textShadow)">DoViFixer</text>
  </g>
  <path d="M340 200 H410" stroke="#ffffff" stroke-width="4" marker-end="url(#arrow)"/>
  <path d="M530 200 H600" stroke="#ffffff" stroke-width="4" marker-end="url(#arrow)"/>

  <!-- The .dovi backup -->
  <g class="a box" transform="translate(380 330)">
    <rect width="150" height="100" rx="14" fill="#5a3a12" opacity="0.85"/>
    <rect x="10" y="10" width="130" height="18" rx="6" fill="#ffffff" opacity="0.25"/>
    <text x="75" y="78" class="mid" text-anchor="middle">Movie.dovi</text>
  </g>
  <g class="a t-el" transform="translate(372 458)" filter="url(#textShadow)">
    <text class="sub">Optional: EL kept in a small backup file</text>
  </g>
  <path class="a restore" d="M380 400 C 220 470, 80 440, 60 340" fill="none" stroke="#ffffff" stroke-width="3" stroke-dasharray="8 8" marker-end="url(#arrow)"/>
  <g class="a restore" transform="translate(150 484)" filter="url(#textShadow)"><text class="sub">Restore to DV7 any time</text></g>

  <!-- Result: a Profile 8.1 file -->
  <g class="a out">
    <g filter="url(#textShadow)"><text x="640" y="58" class="big">Profile 8.1 file</text></g>
    {sheet("", 640, 100, base)}
    {sheet("", 680, 108, rpu)}
    <g transform="translate(720 82)"><rect x="-10" y="-17" width="56" height="24" rx="12" fill="#1e9e57"/><text x="18" y="0" class="tag" fill="#ffffff" text-anchor="middle">P8.1</text></g>
    <g transform="translate(670 315)" filter="url(#textShadow)"><text class="tag" fill="#ffffff"><tspan x="0">BL</tspan><tspan x="60">RPU</tspan></text></g>
  </g>
  <g class="a t-keep" transform="translate(640 350)" filter="url(#textShadow)"><text class="sub">✓ Base layer: bit-for-bit the same</text></g>
  <g class="a t-rpu" transform="translate(640 375)" filter="url(#textShadow)"><text class="sub">✓ RPU: converted to Profile 8.1</text></g>
  <g class="a t-out" transform="translate(640 400)" filter="url(#textShadow)"><text class="sub">✓ Audio, subtitles, chapters: untouched</text></g>
  <g class="a t-out" transform="translate(640 425)" filter="url(#textShadow)"><text class="sub">✓ Plays in Dolby Vision on streaming boxes</text></g>
  </svg>
"""


if __name__ == "__main__":
    with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as out:
        out.write(build())
