"""Regenerates every illustration in docs-site/assets. Run: python tools/illustrations/build.py"""
from pathlib import Path

from common import mascot

import conversion
import dv_layers
import fel_check
import hero
import playback

ASSETS = Path(__file__).resolve().parents[2] / "docs-site" / "assets"

for name, module in [("hero", hero), ("dv-layers", dv_layers), ("conversion", conversion), ("playback", playback), ("fel-check", fel_check)]:
    (ASSETS / f"{name}.svg").write_text(module.build(), encoding="utf-8", newline="\n")
    print(f"Wrote {name}.svg")

logo = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="-20 -10 120 120" width="120" height="120" role="img" aria-label="DoViFixer">
  {mascot(40, 40)}
</svg>
'''
(ASSETS / "logo.svg").write_text(logo, encoding="utf-8", newline="\n")
print("Wrote logo.svg")
