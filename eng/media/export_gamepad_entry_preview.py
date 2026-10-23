#!/usr/bin/env python3
"""Export native --native-gamepad-preview captures; requires Pillow and ffmpeg."""
import argparse
import json
import shutil
import subprocess
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("capture", type=Path)
parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[2] / "docs/previews/gamepad-entry")
args = parser.parse_args()
frames = json.loads((args.capture / "frames.json").read_text())
step = frames[1]["age"] - frames[0]["age"]
assert step == 20, "The GIF export uses a uniform 50fps clock."
assert all(b["age"] - a["age"] == step for a, b in zip(frames, frames[1:]))
args.output.mkdir(parents=True, exist_ok=True)


def composite(item):
    canvas = Image.new("RGBA", (1440, 900), (31, 34, 42, 255))
    with Image.open(args.capture / item["file"]) as bitmap:
        canvas.alpha_composite(bitmap.convert("RGBA"), (item["x"], item["y"]))
    return canvas.convert("RGB").resize((960, 600), Image.Resampling.LANCZOS)


# A shared palette prevents frame-to-frame color quantization from flickering.
samples = frames[::max(1, len(frames) // 24)] + [frames[-1]]
swatches = Image.new("RGB", (240, 150 * len(samples)))
for index, item in enumerate(samples):
    swatches.paste(composite(item).resize((240, 150), Image.Resampling.LANCZOS), (0, index * 150))
palette = swatches.quantize(colors=256)
images = [composite(item).quantize(palette=palette, dither=Image.Dither.NONE) for item in frames]
camera_start = next(i for i, frame in enumerate(frames) if frame["age"] >= 600)
for name, pictures in (("entry", images), ("camera", images[camera_start:])):
    path = args.output / f"{name}.gif"
    pictures[0].save(path, save_all=True, append_images=pictures[1:], duration=step,
                     loop=0, optimize=True, disposal=2)
    print(f"{path.name}: {len(pictures) * step}ms, 50fps source, {path.stat().st_size} bytes")

for phase in ("black", "flash", "corner", "pullback", "logo", "name", "lockup"):
    source = args.capture / f"entry-{phase}.png"
    target = args.output / source.name
    if target.exists():
        with Image.open(source) as new, Image.open(target) as old:
            if new.size == old.size and new.convert("RGBA").tobytes() == old.convert("RGBA").tobytes():
                continue
    shutil.copyfile(source, target)

metrics = json.loads((args.capture / "motion-metrics.json").read_text())
metrics["scope"] = "Linux native offscreen fixture; CPU NXML projection and renderer layout only; drawing and PNG encoding excluded"
(args.output / "motion-metrics.json").write_text(json.dumps(metrics, indent=2) + "\n")
subprocess.run([
    "ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-framerate", "50",
    "-start_number", str(camera_start), "-i", str(args.capture / "frame-%03d.png"),
    "-frames:v", str(len(frames) - camera_start), "-vf", "scale=1280:800:flags=lanczos,format=yuv420p",
    "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-threads", "2",
    "-movflags", "+faststart", str(args.output / "camera.mp4")
], check=True)
