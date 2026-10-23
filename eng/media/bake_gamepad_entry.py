#!/usr/bin/env python3
"""Bake bounded entry lighting, a plate-outline horizon and an occulted soft sun.

Only asset regeneration needs Inkscape/Pillow; runtime and normal builds do not.
The supplied SVG and normal brand PNG are never changed.
"""
import math
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2] / "Nexa.Desktop/Assets/Brand"

with Image.open(ROOT / "neon-n-dark.png") as source:
    source = source.convert("RGBA")
    lit = Image.new("RGBA", source.size)
    for y in range(source.height):
        for x in range(source.width):
            r, g, b, a = source.getpixel((x, y))
            distance = math.hypot((x / 512 - .105) * 1.1, y / 512 - .105)
            light = .25 + .95 * math.exp(-3.4 * distance * distance)
            lit.putpixel((x, y), (min(255, round(r * light)),
                                 min(255, round(g * light)), min(255, round(b * light)), a))
    lit.save(ROOT / "neon-n-entry.png", optimize=True)

# A vector-derived silhouette preserves the actual rounded plate, rather than
# approximating its edge with the letter N or introducing a different logo shape.
svg = ET.parse(ROOT / "neon-n-dark-transparent.svg").getroot()
plate = next(node for node in svg.iter() if node.get("id") == "plate-shape")
with tempfile.TemporaryDirectory(prefix="nexacl-horizon-") as temporary:
    path = Path(temporary) / "plate.svg"
    output = ET.Element("svg", {"xmlns": "http://www.w3.org/2000/svg", "viewBox": "0 0 1254 1254"})
    ET.SubElement(output, "path", {"d": plate.get("d"), "fill": "white"})
    path.write_bytes(ET.tostring(output))
    subprocess.run(["inkscape", str(path), "--export-type=png", "--export-width=1024", "--export-height=1024"],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    with Image.open(path.with_suffix(".png")) as bitmap:
        mask = bitmap.getchannel("A").copy()

focus = next(i for i in range(1024) if mask.getpixel((i, i)) > 127) / 1024
assert focus == 118 / 1024, "Update the documented camera focus when replacing the SVG plate."
# Low-pass the silhouette before extracting its rim: a huge close-up must not
# amplify antialias coverage into dark dotted patches along the atmosphere.
solid = mask.filter(ImageFilter.GaussianBlur(.8))
near = solid.filter(ImageFilter.GaussianBlur(1.1))
atmosphere = solid.filter(ImageFilter.GaussianBlur(4))
halo = solid.filter(ImageFilter.GaussianBlur(11))
solid_pixels, near_pixels = solid.load(), near.load()
atmosphere_pixels, halo_pixels = atmosphere.load(), halo.load()
airglow = Image.new("RGBA", (1024, 1024))
rim = Image.new("RGBA", (1024, 1024))
for y in range(1024):
    for x in range(1024):
        surface = solid_pixels[x, y] / 255
        edge = near_pixels[x, y] / 255
        air = atmosphere_pixels[x, y] / 255
        bloom = halo_pixels[x, y] / 255
        distance = math.hypot(x / 1024 - focus, y / 1024 - focus)
        illumination = .20 + .80 * math.exp(-distance * distance / .014)
        glow_alpha = min(1, (1 - surface) * (air * .82 + bloom * .30)) * illumination
        rim_alpha = .90 * 4 * edge * (1 - edge) * illumination
        airglow.putpixel((x, y), (36, 156, 255, round(255 * glow_alpha)))
        rim.putpixel((x, y), (170, 233, 255, round(255 * rim_alpha)))
body = Image.new("RGBA", (1024, 1024), (3, 6, 10, 255))
body.putalpha(solid)
# Proper alpha composition keeps the bright rim continuous across fractional
# vector coverage. The opaque interior actually occludes the rising light.
horizon = Image.alpha_composite(Image.alpha_composite(airglow, body), rim)
horizon.save(ROOT / "gamepad-entry-horizon.png", optimize=True)

# A luminous disc and diffuse atmosphere replace the previous four-point star.
flare = Image.new("RGBA", (512, 512))
for y in range(512):
    for x in range(512):
        dx, dy = (x - 255.5) / 256, (y - 255.5) / 256
        radius = math.hypot(dx, dy)
        core = math.exp(-radius * radius / .003)
        corona = .42 * math.exp(-radius * radius / .018)
        glow = .14 * math.exp(-radius * radius / .16)
        alpha = min(1, core + corona + glow)
        flare.putpixel((x, y), (round(150 + 105 * core), round(214 + 41 * core),
                               255, round(255 * alpha)))
flare.save(ROOT / "gamepad-entry-flash.png", optimize=True)
print("Horizon diagonal edge:", focus)
