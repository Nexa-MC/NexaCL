"""Generate original vector document icons; requires resvg_py 0.5.0 and Pillow."""
from pathlib import Path
import io
import struct
import resvg_py
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "src/app/Nexa.Desktop/Assets/FileTypes"
GLYPHS = {
    "N": "M0 28V0L20 28V0", "M": "M0 28V0L10 14L20 0V28",
    "P": "M0 28V0H13Q20 0 20 7Q20 14 13 14H0",
    "T": "M0 0H20M10 0V28", "X": "M0 0L20 28M20 0L0 28",
    "R": "M0 28V0H13Q20 0 20 7Q20 14 13 14H0M10 14L20 28",
}

def document(name, letters, color, dark):
    glyphs = ''.join(f'<path d="{GLYPHS[c]}" transform="translate({86+i*32} 190)"/>' for i, c in enumerate(letters))
    symbol = ('<path d="M103 143c-15 1-11 16-24 18 24 8 35-4 30-14Z" fill="white"/>'
              '<path d="m108 140 29-48q7-11 17-2t-1 17l-38 39Z" fill="white"/>') if name == "ntp" else (
              '<path d="m128 79 39 22-39 23-39-23Z" fill="white"/>'
              '<path d="m89 109 35 20v38l-35-21Z" fill="white" opacity=".78"/>'
              '<path d="m132 129 35-20v37l-35 21Z" fill="white" opacity=".95"/>')
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
<defs><linearGradient id="paper" x2="0" y2="1"><stop stop-color="#ffffff"/><stop offset="1" stop-color="#eaf0f8"/></linearGradient>
<linearGradient id="badge" x2="1" y2="1"><stop stop-color="{color}"/><stop offset="1" stop-color="{dark}"/></linearGradient></defs>
<path d="M58 20h100l50 50v150q0 20-20 20H58q-20 0-20-20V40q0-20 20-20Z" fill="#102038" opacity=".12"/>
<path d="M58 16h100l50 50v150q0 20-20 20H58q-20 0-20-20V36q0-20 20-20Z" fill="url(#paper)" stroke="#c8d3e2" stroke-width="2"/>
<path d="M158 17v31q0 18 18 18h31" fill="#d8e3f1"/>
<rect x="65" y="64" width="126" height="112" rx="28" fill="url(#badge)"/>{symbol}
<g fill="none" stroke="{dark}" stroke-width="5" stroke-linecap="round" stroke-linejoin="round">{glyphs}</g></svg>'''

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, code, color, dark in [
        ("nmp", "NMP", "#439aff", "#1759ce"), ("ntp", "NTP", "#ae73f5", "#7140c5"),
        ("mrpack", "MRP", "#27c99a", "#087c62"), ("nexapack", "NXP", "#35bdd7", "#156c9e"),
    ]:
        svg = document(name, code, color, dark)
        (OUT / f"{name}.svg").write_text(svg, encoding="utf-8")
        frames = [(size, resvg_py.svg_to_bytes(svg_string=svg, width=size, height=size, skip_system_fonts=True)) for size in (16,24,32,48,64,128,256)]
        offset = 6 + 16 * len(frames)
        directory = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
        for size, png in frames:
            directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(png), offset)
            offset += len(png)
        (OUT / f"{name}.ico").write_bytes(directory + b''.join(png for _, png in frames))
        (OUT / f"{name}.png").write_bytes(frames[-1][1])
        Image.open(io.BytesIO(resvg_py.svg_to_bytes(svg_string=svg, width=1024, height=1024))).save(OUT / f"{name}.icns")

if __name__ == "__main__":
    main()
