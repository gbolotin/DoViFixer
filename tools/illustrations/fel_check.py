"""Generates fel-check.svg: which Profile 7 files DoViFixer converts by default, shown as brightness bars."""
import sys
from common import FONT, background, background_defs, keyframes

W, H = 1000, 470
T = 10
BAR_BOTTOM = 330


def card(index, x, name, what, base, extra, verdict, color, note):
    d = index * 0.6
    base_h, extra_h = base, extra
    extra_svg = ""
    if extra_h:
        extra_svg = f'<rect class="a extra" style="animation-delay: {d}s" x="70" y="{BAR_BOTTOM - base_h - extra_h}" width="70" height="{extra_h}" rx="6" fill="#ffffff" opacity="0.9"/>' \
                    f'<text class="a extra small" style="animation-delay: {d}s" x="150" y="{BAR_BOTTOM - base_h - extra_h / 2 + 5}">+ EL brightness</text>'
    return f"""
  <g transform="translate({x} 0)">
    <rect x="0" y="70" width="290" height="380" rx="16" fill="#ffffff" opacity="0.16"/>
    <g filter="url(#textShadow)">
      <text x="20" y="108" class="mid">{name}</text>
      <text x="20" y="132" class="sub">{what}</text>
    </g>
    <line x1="20" y1="{BAR_BOTTOM}" x2="270" y2="{BAR_BOTTOM}" stroke="#ffffff" stroke-width="2" opacity="0.7"/>
    <rect class="a grow" style="animation-delay: {d}s" x="70" y="{BAR_BOTTOM - base_h}" width="70" height="{base_h}" rx="6" fill="#4c8dff"/>
    {extra_svg}
    <text x="105" y="{BAR_BOTTOM + 20}" class="small" text-anchor="middle">BL peak</text>
    <g class="a verdict" style="animation-delay: {d}s">
      <rect x="20" y="368" width="250" height="32" rx="16" fill="{color}"/>
      <text x="145" y="390" class="tag" text-anchor="middle">{verdict}</text>
    </g>
    <g filter="url(#textShadow)"><text x="145" y="428" class="small" text-anchor="middle">{note}</text></g>
  </g>"""


def build():
    css = "\n    ".join([
        f"text {{ font-family: {FONT}; font-style: italic; font-weight: 700; fill: #ffffff; }}",
        ".big { font-size: 28px; } .mid { font-size: 22px; } .sub { font-size: 15px; } .small { font-size: 13px; }",
        ".tag { font-size: 14px; font-style: normal; }",
        ".a { animation-duration: %ds; animation-iteration-count: infinite; animation-timing-function: ease-out; }" % T,
        ".grow { animation-name: grow; transform-box: fill-box; transform-origin: bottom; }",
        ".extra { animation-name: extra; } .verdict { animation-name: verdict; }",
        keyframes("grow", T, [(0, "transform: scaleY(0);"), ((1.2, 9.2), "transform: scaleY(1);"), (T, "transform: scaleY(0);")]),
        keyframes("extra", T, [((0, 1.4), "opacity: 0;"), ((2.4, 9.2), "opacity: 1;"), (T, "opacity: 0;")]),
        keyframes("verdict", T, [((0, 3), "opacity: 0;"), ((3.6, 9.2), "opacity: 1;"), (T, "opacity: 0;")]),
        "@media (prefers-reduced-motion: reduce) { .a { animation: none; } }",
    ])
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="title desc">
  <title id="title">Which Profile 7 files DoViFixer converts</title>
  <desc id="desc">Animation. A MEL file's enhancement layer is almost empty, so DoViFixer converts it. A Simple FEL file's enhancement layer holds picture data but adds no brightness; DoViFixer converts it only when you turn on Include Simple FEL conversion. A Complex FEL file's enhancement layer raises brightness above the base layer; DoViFixer skips it unless you turn on Allow Complex FEL conversion.</desc>
  <defs>{background_defs()}</defs>
  <style>
    {css}
  </style>{background(W, H)}
  <g filter="url(#textShadow)"><text x="30" y="48" class="big">Is it safe to drop the enhancement layer?</text></g>
  {card(0, 30, "MEL", "The EL is almost empty.", 130, 0, "Converted", "#1e9e57", "Nothing visible is lost.")}
  {card(1, 355, "Simple FEL", "Picture data, no extra brightness.", 130, 0, "Converted if you turn it on", "#d35400", "Settings › Conversion › Include Simple FEL")}
  {card(2, 680, "Complex FEL", "The EL makes the picture brighter.", 100, 70, "Skipped", "#c0392b", "Converting would lose that brightness.")}
</svg>
"""


if __name__ == "__main__":
    with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as out:
        out.write(build())
