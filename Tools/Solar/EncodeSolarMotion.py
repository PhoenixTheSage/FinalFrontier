"""Encode Blender's lossless odd-frame PNG sequence at its real simulation rate.

Usage: python EncodeSolarMotion.py <Motion> [--ffmpeg <executable>]
Optional imageio-ffmpeg supplies the executable if it is not on PATH.
"""
import argparse
import json
import shutil
import subprocess
from pathlib import Path
from PIL import Image

parser=argparse.ArgumentParser()
parser.add_argument('directory',type=Path)
parser.add_argument('--ffmpeg')
args=parser.parse_args()
frames=sorted(args.directory.glob('solar-*.png'))
settings=json.loads((args.directory/'export.json').read_text())
count,fps=settings['frames'],settings['playback_fps']
expected=[f"solar-{settings['first']+i*settings['step']:04}.png" for i in range(count)]
if [p.name for p in frames] != expected: raise RuntimeError('Expected the complete sequence specified in export.json.')
executable=args.ffmpeg or shutil.which('ffmpeg')
if not executable:
    import imageio_ffmpeg
    executable=imageio_ffmpeg.get_ffmpeg_exe()
target=args.directory.parent/'SolarBlender-animation.mp4'
with Image.open(frames[0]) as first: width,height=first.size
# Raw frames avoid the concat demuxer's default 25fps timestamp rounding.
process=subprocess.Popen([executable,'-hide_banner','-loglevel','warning','-y','-f','rawvideo','-pix_fmt','rgb24',
                '-s',f'{width}x{height}','-r',str(fps),'-i','-','-frames:v',str(count),
                '-c:v','libx264','-crf','16','-preset','slow','-pix_fmt','yuv420p',
                '-movflags','+faststart',str(target)],stdin=subprocess.PIPE)
try:
    for path in frames:
        with Image.open(path) as frame:
            if frame.size != (width,height): raise RuntimeError('Motion frame dimensions changed.')
            process.stdin.write(frame.convert('RGB').tobytes())
finally:
    process.stdin.close()
code=process.wait()
if code: raise subprocess.CalledProcessError(code,process.args)
print(target)
