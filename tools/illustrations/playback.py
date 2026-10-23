"""Generates playback.svg: what a Profile 7 video looks like on common streaming boxes, then the same video after conversion to Profile 8.1."""
import sys
from common import FONT, background, background_defs, keyframes, scene_symbol

W, H = 1000, 560
T = 12
SCREEN_W, SCREEN_H = 280, 158


def device_art(kind):
    """Generic box drawings; no logos."""
    if kind == "box":
        return """<rect x="-34" y="-20" width="68" height="40" rx="10" fill="#1b1b1f"/><rect x="-34" y="-20" width="68" height="10" rx="5" fill="#33343a"/>
        <rect x="48" y="-22" width="14" height="44" rx="7" fill="#c9cbd2"/><circle cx="55" cy="-12" r="3.5" fill="#1b1b1f"/>"""
    if kind == "tube":
        return """<rect x="-52" y="-14" width="104" height="28" rx="14" fill="#1b1b1f"/><rect x="-52" y="-14" width="104" height="8" rx="4" fill="#33343a"/>
        <rect x="18" y="-3" width="20" height="5" rx="2.5" fill="#76d36b"/>"""
    return """<rect x="-46" y="-12" width="80" height="24" rx="8" fill="#1b1b1f"/><rect x="34" y="-7" width="18" height="14" rx="2" fill="#8d9099"/>
        <rect x="-46" y="-12" width="80" height="7" rx="3.5" fill="#33343a"/>"""


def column(index, x, name, kind, filter_id, problem, problem_color, lines, fixed, fixed_lines):
    d = index * 0.4
    return f"""
  <g transform="translate({x} 92)">
    <g filter="url(#textShadow)"><text x="{SCREEN_W / 2}" y="0" class="mid" text-anchor="middle">{name}</text></g>
    <g transform="translate({SCREEN_W / 2} 40)">{device_art(kind)}</g>
    <g transform="translate(0 78)">
      <rect x="-8" y="-8" width="{SCREEN_W + 16}" height="{SCREEN_H + 16}" rx="10" fill="#111216"/>
      <use href="#scene" width="{SCREEN_W}" height="{SCREEN_H}" filter="url(#{filter_id})"/>
      <g clip-path="url(#wipe{index})"><use href="#scene" width="{SCREEN_W}" height="{SCREEN_H}" filter="url(#vivid)"/></g>
      <rect class="a bar" style="animation-delay: {d}s" x="-2" y="0" width="4" height="{SCREEN_H}" fill="#ffffff"/>
      <rect x="{SCREEN_W / 2 - 24}" y="{SCREEN_H + 8}" width="48" height="10" fill="#111216"/>
      <rect x="{SCREEN_W / 2 - 56}" y="{SCREEN_H + 17}" width="112" height="6" rx="3" fill="#111216"/>
    </g>
    <g transform="translate({SCREEN_W / 2} 290)">
      <g class="a before" style="animation-delay: {d}s"><rect x="-140" y="-19" width="280" height="30" rx="15" fill="{problem_color}"/><text y="2" class="tag" text-anchor="middle">{problem}</text></g>
      <g class="a after" style="animation-delay: {d}s"><rect x="-140" y="-19" width="280" height="30" rx="15" fill="#1e9e57"/><text y="2" class="tag" text-anchor="middle">{fixed}</text></g>
    </g>
    <g filter="url(#textShadow)" text-anchor="middle">
      <g class="a before" style="animation-delay: {d}s"><text x="{SCREEN_W / 2}" y="336" class="sub">{lines[0]}</text><text x="{SCREEN_W / 2}" y="356" class="sub">{lines[1]}</text></g>
      <g class="a after" style="animation-delay: {d}s"><text x="{SCREEN_W / 2}" y="336" class="sub">{fixed_lines[0]}</text><text x="{SCREEN_W / 2}" y="356" class="sub">{fixed_lines[1]}</text></g>
    </g>
  </g>"""


def build():
    wipe = keyframes("wipe", T, [((0, 3.6), "transform: scaleX(0);"), ((4.8, 10), "transform: scaleX(1);"), ((11.2, T), "transform: scaleX(0);")])
    bar = keyframes("bar", T, [((0, 3.6), f"transform: translateX(0); opacity: 0;"), (3.7, "transform: translateX(0); opacity: 1;"), (4.8, f"transform: translateX({SCREEN_W}px); opacity: 1;"), ((4.9, 10), f"transform: translateX({SCREEN_W}px); opacity: 0;"), (10.1, f"transform: translateX({SCREEN_W}px); opacity: 1;"), (11.2, "transform: translateX(0); opacity: 1;"), ((11.3, T), "transform: translateX(0); opacity: 0;")])
    before = keyframes("before", T, [((0, 4.4), "opacity: 1;"), ((4.8, 10.6), "opacity: 0;"), ((11.2, T), "opacity: 1;")])
    after = keyframes("after", T, [((0, 4.4), "opacity: 0;"), ((4.8, 10.6), "opacity: 1;"), ((11.2, T), "opacity: 0;")])
    stage = keyframes("stage", T, [((0, 4.4), "opacity: 1;"), ((4.8, 10.6), "opacity: 0;"), ((11.2, T), "opacity: 1;")])
    stage2 = keyframes("stage2", T, [((0, 4.4), "opacity: 0;"), ((4.8, 10.6), "opacity: 1;"), ((11.2, T), "opacity: 0;")])
    css = "\n    ".join([
        f"text {{ font-family: {FONT}; font-style: italic; font-weight: 700; }}",
        ".big { font-size: 28px; fill: #ffffff; } .mid { font-size: 20px; fill: #ffffff; } .sub { font-size: 14px; fill: #ffffff; }",
        ".tag { font-size: 14px; font-style: normal; font-weight: 700; fill: #ffffff; }",
        ".caption { font-size: 14px; fill: #fff6e6; font-style: normal; font-weight: 600; }",
        ".a { animation-duration: %ds; animation-iteration-count: infinite; animation-timing-function: ease-in-out; }" % T,
        ".wipe { animation-name: wipe; transform-box: view-box; } .bar { animation-name: bar; } .before { animation-name: before; } .after { animation-name: after; }",
        ".stage { animation-name: stage; } .stage2 { animation-name: stage2; }",
        wipe, bar, before, after, stage, stage2,
        # Without motion, the problem pictures and their explanations stay on screen.
        "@media (prefers-reduced-motion: reduce) { .a { animation: none; } .wipe { transform: scaleX(0); } .bar, .after, .stage2 { opacity: 0; } }",
    ])
    clips = "".join(f'<clipPath id="wipe{i}"><rect class="a wipe" style="animation-delay: {i * 0.4}s" width="{SCREEN_W}" height="{SCREEN_H}"/></clipPath>' for i in range(3))
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-labelledby="title desc">
  <title id="title">Dolby Vision Profile 7 on Apple TV, NVIDIA Shield and Amazon Fire TV</title>
  <desc id="desc">Animation with simulated pictures. Apple TV 4K cannot decode Profile 7 and usually plays only the HDR10 base layer, which looks flatter. NVIDIA Shield plays Dolby Vision from the base layer and RPU but drops the enhancement layer, so titles with a full enhancement layer can show the wrong brightness. Amazon Fire TV does one or the other depending on the model and app. After DoViFixer converts the video to Profile 8.1, all three play it in Dolby Vision.</desc>
  <defs>{background_defs()}{scene_symbol()}{clips}
    <!-- HDR10 fallback: static metadata only, so the picture is flatter and less colorful. -->
    <filter id="hdr10"><feColorMatrix type="saturate" values="0.6"/><feComponentTransfer><feFuncR type="linear" slope="0.72" intercept="0.1"/><feFuncG type="linear" slope="0.72" intercept="0.1"/><feFuncB type="linear" slope="0.72" intercept="0.12"/></feComponentTransfer></filter>
    <!-- Enhancement layer dropped from a full-EL title: the RPU maps a picture it was not made for, so brightness is wrong. -->
    <filter id="noel"><feColorMatrix type="saturate" values="0.9"/><feComponentTransfer><feFuncR type="gamma" amplitude="0.75" exponent="1.9"/><feFuncG type="gamma" amplitude="0.72" exponent="1.9"/><feFuncB type="gamma" amplitude="0.8" exponent="1.7"/></feComponentTransfer></filter>
    <filter id="mixed"><feColorMatrix type="saturate" values="0.7"/><feComponentTransfer><feFuncR type="gamma" amplitude="0.85" exponent="1.4" offset="0.04"/><feFuncG type="gamma" amplitude="0.85" exponent="1.4" offset="0.04"/><feFuncB type="gamma" amplitude="0.9" exponent="1.3" offset="0.05"/></feComponentTransfer></filter>
    <filter id="vivid"><feColorMatrix type="saturate" values="1.2"/><feComponentTransfer><feFuncR type="linear" slope="1.06"/><feFuncG type="linear" slope="1.06"/><feFuncB type="linear" slope="1.04"/></feComponentTransfer></filter>
  </defs>
  <style>
    {css}
  </style>{background(W, H)}
  <g filter="url(#textShadow)">
    <text x="30" y="50" class="big a stage">A Profile 7 movie on streaming boxes…</text>
    <text x="30" y="50" class="big a stage2">…and the same movie after DoViFixer (Profile 8.1)</text>
  </g>
  {column(0, 30, "Apple TV 4K", "box", "hdr10", "HDR10 only: no Dolby Vision", "#c0392b", ("Can't decode Profile 7. Most apps show", "only the HDR10 base layer: flatter picture."),
           "After DoViFixer: Dolby Vision ✓", ("Profile 8.1 plays in Dolby Vision,", "with the same base layer and RPU."))}
  {column(1, 360, "NVIDIA Shield TV", "tube", "noel", "Dolby Vision, but the EL is dropped", "#d35400", ("Uses BL + RPU, drops the EL. With a", "full EL (FEL), brightness can be wrong."),
           "Safe titles: Dolby Vision ✓", ("Converts only titles whose EL is safe", "to drop. Complex FEL is found and skipped."))}
  {column(2, 690, "Amazon Fire TV", "stick", "mixed", "Varies: HDR10, or the EL is dropped", "#d35400", ("Depends on the model and player app:", "HDR10 only, or Dolby Vision minus the EL."),
           "After DoViFixer: Dolby Vision ✓", ("Profile 8.1 plays in Dolby Vision on", "every Fire TV model with Dolby Vision."))}
  <text x="30" y="{H - 20}" class="caption">Simulated pictures. Results depend on the player app and firmware.</text>
</svg>
"""


if __name__ == "__main__":
    with open(sys.argv[1], "w", encoding="utf-8", newline="\n") as out:
        out.write(build())
