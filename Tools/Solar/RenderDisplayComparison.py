"""Display actual GPU tone-map test output on SDR; does not simulate an HDR monitor.

python Tools/Solar/RenderDisplayComparison.py <SolarPreviewDirectory>
Run the celestial checks with -HdrRoot and -SolarPreviewDirectory first.
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

parser = argparse.ArgumentParser()
parser.add_argument("directory", type=Path)
args = parser.parse_args()
w, h = map(int, (args.directory / "display.txt").read_text().splitlines()[0].split())
names = ["display-sdr", "display-hdr-before", "display-hdr-after"]
labels = ["Keen SDR shoulder", "HDR: previous orange", "HDR: corrected gold"]
data = [np.fromfile(args.directory / (name + ".f32"), dtype="<f4").reshape(h, w, 4)[..., :3] for name in names]
# Use the SAME display scale on both HDR results; preserve their RGB ratios.
# A normal PNG cannot represent scRGB highlight luminance on an HDR display.
hdr_scale = max(float(np.max(data[1])), float(np.max(data[2])), 1e-6)
canvas = Image.new("RGB", (w * 3, h + 92), (12, 12, 12))
draw = ImageDraw.Draw(canvas)
try:
    font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 17)
    small = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 13)
except OSError:
    font = small = ImageFont.load_default()
for i, (rgb, label) in enumerate(zip(data, labels)):
    linear = np.clip(rgb / (1 if i == 0 else hdr_scale), 0, 1)
    srgb = np.where(linear <= .0031308, linear * 12.92, 1.055 * linear ** (1 / 2.4) - .055)
    canvas.paste(Image.fromarray(np.uint8(np.clip(srgb * 255, 0, 255))), (i * w, 36))
    draw.text((i * w + 12, 8), label, fill=(230, 230, 230), font=font)
draw.text((12, h + 46), "Actual shader output on D3D11 WARP. Neutral exposure/grade; bloom and native lens flares omitted.", fill=(185, 185, 185), font=small)
draw.text((12, h + 66), "HDR panels share one scale for this SDR PNG. Verify final brightness and colour on an HDR monitor in-game.", fill=(185, 185, 185), font=small)
output = args.directory.parent / "SolarDisplayComparison.png"
canvas.save(output)
print(output)
