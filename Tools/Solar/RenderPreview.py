"""Tone-map the actual WARP shader frames to a PNG/GIF; does not edit the reference.

python Tools/Solar/RenderPreview.py <SolarPreviewDirectory> [--exposure 1.15]
Requires numpy and Pillow. Preview uses fixed exposure, not Keen's post-processing.
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("directory", type=Path)
parser.add_argument("--exposure", type=float, default=1.15)
parser.add_argument("--name", default="SolarPlasma")
args = parser.parse_args()
width, height, count = map(int, (args.directory / "preview.txt").read_text().splitlines()[0].split())
frames = []
for index in range(count):
    rgba = np.fromfile(args.directory / f"solar-{index:03}.f32", dtype="<f4").reshape(height, width, 4)
    rgb = np.maximum(rgba[:, :, :3], 0) / 10.0
    mapped = np.power(1.0 - np.exp(-rgb * args.exposure), 1.0 / 2.2)
    frames.append(Image.fromarray(np.uint8(np.clip(mapped * 255.0, 0, 255))))
output = args.directory.parent
frames[0].save(output / f"{args.name}.png")
frames[len(frames) // 2].save(output / f"{args.name}-midpoint.png")
# Use one palette for all frames to avoid artificial colour flicker.
palette = frames[len(frames) // 2].quantize(colors=256, dither=Image.Dither.NONE)
gif = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in frames]
if len(gif) > 1:
    gif[0].save(output / f"{args.name}.gif", save_all=True, append_images=gif[1:], duration=125, loop=0, optimize=False)
print(output / f"{args.name}.png")
