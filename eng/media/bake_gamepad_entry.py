#!/usr/bin/env python3
"""Bake bounded cinematic lighting textures; no runtime or build dependency.

The supplied SVG and normal brand PNG are never changed. Zoom is a draw recipe,
so these two 512px textures remain small even when the icon fills the monitor.
"""
import math
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2] / "Nexa.Desktop/Assets/Brand"

with Image.open(ROOT / "neon-n-dark.png") as source:
    source = source.convert("RGBA")
    lit = Image.new("RGBA", source.size)
    for y in range(source.height):
        for x in range(source.width):
            r, g, b, a = source.getpixel((x, y))
            distance = math.hypot((x / 512 - .28) * 1.1, y / 512 - .22)
            light = .22 + .90 * math.exp(-3.4 * distance * distance)
            lit.putpixel((x, y), (min(255, round(r * light)),
                                 min(255, round(g * light)), min(255, round(b * light)), a))
    lit.save(ROOT / "neon-n-entry.png", optimize=True)

flare = Image.new("RGBA", (512, 512))
for y in range(512):
    for x in range(512):
        dx, dy = (x - 255.5) / 256, (y - 255.5) / 256
        radius = math.hypot(dx, dy)
        core = math.exp(-radius * radius / .0015)
        halo = .18 * math.exp(-radius * radius / .105)
        horizontal = .32 * math.exp(-dx * dx / .28 - dy * dy / .00016)
        vertical = .15 * math.exp(-dy * dy / .12 - dx * dx / .00014)
        alpha = min(1, core + halo + horizontal + vertical)
        flare.putpixel((x, y), (round(175 + 80 * core), round(230 + 25 * core),
                               255, round(255 * alpha)))
flare.save(ROOT / "gamepad-entry-flash.png", optimize=True)
