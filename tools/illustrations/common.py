"""Shared pieces for the DoViFixer illustrations: background, scene, mascot, fonts, keyframe helper."""
import random

FONT = "'Futura', 'Century Gothic', 'Trebuchet MS', 'Segoe UI', system-ui, sans-serif"


def pct(seconds, total):
    return f"{seconds / total * 100:.2f}%"


def keyframes(name, total, stops):
    """stops: list of (seconds, css) pairs; seconds may be a tuple of several times sharing one css."""
    lines = []
    for when, css in stops:
        times = when if isinstance(when, tuple) else (when,)
        lines.append(f"{', '.join(pct(t, total) for t in times)} {{ {css} }}")
    return f"@keyframes {name} {{ " + " ".join(lines) + " }"


def background_defs():
    return """
    <pattern id="chevrons" width="140" height="140" patternUnits="userSpaceOnUse">
      <rect width="140" height="140" fill="#ffa412"/>
      <path d="M-35 105 L35 35 L105 105 L175 35" fill="none" stroke="#ffb02e" stroke-width="30"/>
      <path d="M-35 175 L35 105 L105 175 L175 105" fill="none" stroke="#ffb02e" stroke-width="30"/>
      <path d="M-35 35 L35 -35 L105 35 L175 -35" fill="none" stroke="#ffb02e" stroke-width="30"/>
    </pattern>
    <filter id="textShadow" x="-10%" y="-30%" width="120%" height="160%">
      <feDropShadow dx="0" dy="2" stdDeviation="2" flood-color="#7a3d00" flood-opacity="0.45"/>
    </filter>
    <filter id="sheetShadow" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="6" dy="8" stdDeviation="8" flood-color="#6b3200" flood-opacity="0.35"/>
    </filter>"""


def background(width, height):
    return f"""
  <rect width="{width}" height="{height}" rx="18" fill="url(#chevrons)"/>"""


def scene_symbol(seed=7):
    """An original mountain meadow, 400x225, with the DoViFixer mascot: a small TV that walks the meadow."""
    rnd = random.Random(seed)
    flowers = []
    for _ in range(150):
        x = rnd.uniform(0, 400)
        y = rnd.uniform(160, 225)
        r = 1.2 + (y - 160) / 65 * 3.2 * rnd.uniform(0.6, 1.1)
        flowers.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r:.1f}"/>')
    grass = []
    for _ in range(70):
        x = rnd.uniform(0, 400)
        y = rnd.uniform(175, 225)
        h = rnd.uniform(6, 16)
        grass.append(f'<path d="M{x:.1f} {y:.1f} q{rnd.uniform(-3, 3):.1f} {-h / 2:.1f} {rnd.uniform(-4, 4):.1f} {-h:.1f}"/>')
    return f"""
    <linearGradient id="skyDay" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#2f6fd6"/>
      <stop offset="1" stop-color="#9fd0ff"/>
    </linearGradient>
    <linearGradient id="meadow" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#6fbf3a"/>
      <stop offset="1" stop-color="#2d6b1c"/>
    </linearGradient>
    <symbol id="scene" viewBox="0 0 400 225">
      <rect width="400" height="225" fill="url(#skyDay)"/>
      <g fill="#ffffff">
        <ellipse cx="70" cy="30" rx="26" ry="7"/><ellipse cx="88" cy="26" rx="16" ry="8"/>
        <ellipse cx="250" cy="44" rx="30" ry="7"/><ellipse cx="268" cy="39" rx="17" ry="8"/>
        <ellipse cx="180" cy="20" rx="12" ry="4"/><ellipse cx="335" cy="22" rx="14" ry="4"/>
      </g>
      <path d="M0 60 L30 40 L58 62 L92 30 L130 80 L170 66 L200 92 L230 70 L262 96 L0 112 Z" fill="#5b6f8f"/>
      <path d="M30 40 L44 50 L38 52 L50 58 L58 62 L44 56 L30 52 L18 50 Z M92 30 L104 45 L96 46 L110 58 L98 54 L86 44 Z M170 66 L182 76 L172 76 Z" fill="#f4f8ff"/>
      <path d="M0 96 L60 82 L120 100 L170 92 L210 110 L210 140 L0 150 Z" fill="#245a2b"/>
      <path d="M180 120 L260 92 L330 70 L400 52 L400 160 L180 160 Z" fill="#5d9a35"/>
      <path d="M300 100 q10 -14 22 -4 q8 -10 16 2 q-4 14 -38 2 Z" fill="#3f7a27"/>
      <rect y="132" width="400" height="93" fill="url(#meadow)"/>
      <g stroke="#2a5e19" stroke-width="1.4" fill="none">{''.join(grass)}</g>
      <g fill="#ffffff" opacity="0.92">{''.join(flowers)}</g>
      {mascot(200, 112)}
    </symbol>"""


def mascot(x, y):
    """The mascot: a friendly retro TV with sunglasses-free round eyes, antenna and sneakers. Original artwork."""
    return f"""
      <g transform="translate({x - 40} {y - 40})" stroke="#111" stroke-width="3" stroke-linejoin="round" stroke-linecap="round">
        <path d="M30 14 L22 0 M50 14 L60 2" fill="none"/>
        <circle cx="22" cy="0" r="3.5" fill="#ff5a5f"/>
        <circle cx="60" cy="2" r="3.5" fill="#ff5a5f"/>
        <path d="M30 76 L24 92 M50 76 L58 92" fill="none"/>
        <path d="M14 92 h16 a5 5 0 0 1 0 8 h-18 a4 4 0 0 1 2 -8 Z M52 92 h14 a6 6 0 0 1 2 8 h-18 a4 4 0 0 1 2 -8 Z" fill="#ffffff"/>
        <path d="M6 44 L-6 54 M74 44 L88 36" fill="none"/>
        <circle cx="-7" cy="56" r="5" fill="#ffffff"/>
        <circle cx="89" cy="34" r="5" fill="#ffffff"/>
        <rect x="2" y="14" width="76" height="62" rx="14" fill="#4c8dff"/>
        <rect x="11" y="22" width="58" height="42" rx="9" fill="#e9f3ff"/>
        <circle cx="29" cy="38" r="4.5" fill="#111" stroke="none"/>
        <circle cx="51" cy="38" r="4.5" fill="#111" stroke="none"/>
        <path d="M28 50 q12 9 24 0" fill="none"/>
        <circle cx="70" cy="70" r="2.5" fill="#111" stroke="none"/>
      </g>"""
