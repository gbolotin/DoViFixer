"""Generates dv-layers.svg: the parts of a Dolby Vision Profile 7 video stack up, then combine into the finished picture."""
import sys
from common import FONT, background, background_defs, keyframes, scene_symbol

W, H = 1000, 500
T = 16  # seconds per loop

# Each sheet is the 400x225 scene squashed to 0.78 width and clipped to a trapezoid, which reads as a picture turned in perspective.
SHEET = "matrix(0.78 0 0 1 0 0)"
TRAPEZOID = "M0 0 L400 26 L400 199 L0 225 Z"


def sheet(css_class, x, y, content, outline):
    return f"""
  <g transform="translate({x} {y})">
    <g class="{css_class}">
      <g transform="{SHEET}" filter="url(#sheetShadow)">
        <g clip-path="url(#trapezoid)">{content}</g>
        <path d="{TRAPEZOID}" fill="none" stroke="{outline}" stroke-width="2.5"/>
      </g>
    </g>
  </g>"""


def label(css_class, y, title, lines):
    sub = "".join(f'<text x="0" y="{28 + 24 * i}" class="sub" fill="{color}">{text}</text>' for i, (text, color) in enumerate(lines))
    return f"""
  <g transform="translate(520 {y})" class="{css_class}" filter="url(#textShadow)">
    <text x="0" y="0" class="big">{title}</text>
    {sub}
  </g>"""


def build():
    enter = "opacity: 0; transform: translate(-70px, 0);"
    shown = "opacity: 1; transform: translate(0, 0);"
    gone = "opacity: 0; transform: translate(40px, 0);"
    css = "\n    ".join([
        f"text {{ font-family: {FONT}; font-style: italic; font-weight: 700; }}",
        ".big { font-size: 28px; fill: #ffffff; }",
        ".sub { font-size: 17px; }",
        ".caption { font-size: 15px; fill: #fff6e6; font-style: normal; font-weight: 600; }",
        ".a { animation-duration: %ds; animation-iteration-count: infinite; animation-timing-function: ease-in-out; }" % T,
        ".bl { animation-name: bl; } .rpu { animation-name: rpu; } .el { animation-name: el; }",
        ".mel { animation-name: mel; } .fel { animation-name: fel; } .fel-text { animation-name: fel-text; }",
        ".l-bl { animation-name: l-bl; } .l-rpu { animation-name: l-rpu; } .l-el { animation-name: l-el; }",
        ".final { animation-name: final; transform-box: fill-box; transform-origin: center; } .l-final { animation-name: l-final; }",
        keyframes("bl", T, [(0, enter), ((1, 10), shown), ((11, T), gone)]),
        keyframes("rpu", T, [((0, 2.5), enter), ((3.5, 10), shown), ((11, T), gone)]),
        keyframes("el", T, [((0, 5), enter), ((6, 10), shown), ((11, T), gone)]),
        keyframes("mel", T, [((0, 7.5), "opacity: 1;"), ((8.5, T), "opacity: 0;")]),
        keyframes("fel", T, [((0, 7.5), "opacity: 0;"), ((8.5, T), "opacity: 1;")]),
        keyframes("l-bl", T, [((0, 0.8), "opacity: 0;"), ((1.6, 10), "opacity: 1;"), ((10.8, T), "opacity: 0;")]),
        keyframes("l-rpu", T, [((0, 3.3), "opacity: 0;"), ((4.1, 10), "opacity: 1;"), ((10.8, T), "opacity: 0;")]),
        keyframes("l-el", T, [((0, 5.8), "opacity: 0;"), ((6.6, 10), "opacity: 1;"), ((10.8, T), "opacity: 0;")]),
        keyframes("fel-text", T, [((0, 8.2), "opacity: 0;"), ((9, 10), "opacity: 1;"), ((10.8, T), "opacity: 0;")]),
        keyframes("final", T, [((0, 10.4), "opacity: 0; transform: scale(0.82);"), ((11.4, 15.2), "opacity: 1; transform: scale(1);"), (T, "opacity: 0; transform: scale(1);")]),
        keyframes("l-final", T, [((0, 11.2), "opacity: 0;"), ((12, 15.2), "opacity: 1;"), (T, "opacity: 0;")]),
        # Without motion, the stack and every label stay visible, with the FEL shown.
        "@media (prefers-reduced-motion: reduce) { .a { animation: none; } .mel, .final, .l-final { opacity: 0; } }",
    ])
    flat = """<use href="#scene" width="400" height="225" filter="url(#flat)"/>"""
    rpu = """<rect width="400" height="225" fill="#f5f5f5" opacity="0.28"/>
          <path d="M20 170 L60 120 L100 140 L140 70 L180 110 L220 60 L260 120 L300 90 L340 130 L380 80" fill="none" stroke="#ffffff" stroke-width="4" opacity="0.8"/>
          <g fill="#ffffff" opacity="0.8"><circle cx="60" cy="120" r="5"/><circle cx="140" cy="70" r="5"/><circle cx="220" cy="60" r="5"/><circle cx="300" cy="90" r="5"/><circle cx="380" cy="80" r="5"/></g>"""
    el = """<rect width="400" height="225" fill="#9b9b9b" opacity="0.35"/>
          <rect class="a mel" x="110" y="58" width="190" height="112" fill="#000000"/>
          <use class="a fel" href="#scene" width="400" height="225" filter="url(#grey)" opacity="0.95"/>"""
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="title desc">
  <title id="title">How a Dolby Vision Profile 7 video is built</title>
  <desc id="desc">Animation. A Profile 7 video stacks three parts: the base layer, a 4K 10-bit HDR10 picture; the RPU, dynamic metadata; and the enhancement layer, which is a minimal layer (MEL) with almost no picture data or a full layer (FEL) with real picture data. A Dolby Vision decoder combines them into the finished picture.</desc>
  <defs>{background_defs()}{scene_symbol()}
    <clipPath id="trapezoid"><path d="{TRAPEZOID}"/></clipPath>
    <filter id="flat"><feColorMatrix type="saturate" values="0.8"/><feComponentTransfer><feFuncR type="linear" slope="0.85" intercept="0.05"/><feFuncG type="linear" slope="0.85" intercept="0.05"/><feFuncB type="linear" slope="0.85" intercept="0.06"/></feComponentTransfer></filter>
    <filter id="grey"><feColorMatrix type="saturate" values="0"/><feComponentTransfer><feFuncR type="linear" slope="0.9" intercept="0.04"/><feFuncG type="linear" slope="0.9" intercept="0.04"/><feFuncB type="linear" slope="0.9" intercept="0.04"/></feComponentTransfer></filter>
    <filter id="vivid"><feColorMatrix type="saturate" values="1.25"/><feComponentTransfer><feFuncR type="linear" slope="1.08"/><feFuncG type="linear" slope="1.08"/><feFuncB type="linear" slope="1.06"/></feComponentTransfer></filter>
  </defs>
  <style>
    {css}
  </style>{background(W, H)}
  {sheet("a bl", 50, 120, flat, "#ffffff")}
  {sheet("a rpu", 105, 128, rpu, "#ffffff")}
  {sheet("a el", 160, 136, el, "#ffffff")}

  <g class="a final">
    <g transform="translate(70 95)" filter="url(#sheetShadow)">
      <use href="#scene" width="480" height="270" filter="url(#vivid)"/>
      <rect width="480" height="270" fill="none" stroke="#ffffff" stroke-width="3"/>
    </g>
  </g>
  {label("a l-bl", 150, "Base Layer (BL)", [("4K 10-bit HDR10 video", "#ffffff")])}
  {label("a l-rpu", 245, "Reference Processing Unit (RPU)", [("Dynamic metadata: brightness, scene by scene", "#ffffff")])}
  {label("a l-el", 340, "Enhancement Layer (EL)", [("Minimal Enhancement Layer (MEL): almost empty", "#ffffff")])}
  <g transform="translate(520 340)" class="a fel-text" filter="url(#textShadow)">
    <text x="0" y="52" class="sub" fill="#0b6b2f">Full Enhancement Layer (FEL): real picture data</text>
  </g>
  <g transform="translate(600 210)" class="a l-final" filter="url(#textShadow)">
    <text x="0" y="0" class="big">= Full Dolby Vision</text>
    <text x="0" y="30" class="sub" fill="#ffffff">BL + RPU + EL, combined by the player</text>
    <text x="0" y="54" class="sub" fill="#ffffff">Up to 12-bit color, scene-by-scene brightness</text>
  </g>
  <text x="24" y="{H - 18}" class="caption">Dolby Vision Profile 7, as on UHD Blu-ray · illustration</text>
</svg>
"""
    return svg


if __name__ == "__main__":
    with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as out:
        out.write(build())
