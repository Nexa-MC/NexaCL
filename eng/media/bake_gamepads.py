#!/usr/bin/env python3
"""Bake the supplied, script-free SMIL drawings with Inkscape and Pillow.

No build-time dependency: the checked-in sheets are the runtime assets. Re-run
this script only when changing the editable SVGs. Timing is 48 frames / 2.4 s.
"""
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2] / "Nexa.Desktop/Assets/Gamepads"
NUMBER = re.compile(r"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?")
ET.register_namespace("", "http://www.w3.org/2000/svg")


def interpolate(first, second, fraction):
    a, b = list(NUMBER.finditer(first)), list(NUMBER.finditer(second))
    if len(a) != len(b) or NUMBER.sub("#", first) != NUMBER.sub("#", second):
        return first if fraction < .5 else second
    values = iter(float(x.group()) + (float(y.group()) - float(x.group())) * fraction
                  for x, y in zip(a, b))
    return NUMBER.sub(lambda _: f"{next(values):.4f}", first)


def frame(source, phase):
    root = ET.fromstring(source)
    for parent in list(root.iter()):
        for child in list(parent):
            if child.tag.rsplit("}", 1)[-1] != "animate":
                continue
            times = [float(v) for v in child.get("keyTimes").split(";")]
            values = child.get("values").split(";")
            index = next((i for i in range(len(times) - 1) if phase < times[i + 1]), len(times) - 2)
            fraction = (phase - times[index]) / (times[index + 1] - times[index])
            parent.set(child.get("attributeName"), interpolate(values[index], values[index + 1], fraction))
            parent.remove(child)
    return ET.tostring(root, encoding="unicode")


for source in sorted(ROOT.glob("*.svg")):
    with tempfile.TemporaryDirectory(prefix="nexacl-gamepad-") as temporary:
        paths = []
        for index in range(48):
            path = Path(temporary) / f"frame-{index:02}.svg"
            path.write_text(frame(source.read_text(), index / 48), encoding="utf-8")
            paths.append(str(path))
        subprocess.run(["inkscape", *paths, "--export-type=png", "--export-width=500", "--export-height=360"],
                       check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        # Each texture also fits the renderer's critical-memory admission budget.
        for group in range(8):
            sheet = Image.new("RGBA", (1500, 720))
            for frame_index in range(6):
                with Image.open(Path(paths[group * 6 + frame_index]).with_suffix(".png")) as image:
                    sheet.paste(image, (frame_index % 3 * 500, frame_index // 3 * 360))
            target = source.with_name(source.stem + f".{group:02}.png")
            sheet.save(target, optimize=True)
            print(target.name, target.stat().st_size)
