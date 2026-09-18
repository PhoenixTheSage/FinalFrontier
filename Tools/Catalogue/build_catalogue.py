"""Deterministic Hipparcos subset; Python standard library only. No runtime downloads."""
import csv
import hashlib
import json
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[2]
URL = 'https://vizier.cds.unistra.fr/viz-bin/asu-tsv?-source=I/239/hip_main&-out=HIP,Vmag,RAICRS,DEICRS,B-V&Vmag=%3C%3D8&-out.max=unlimited'
SHA256 = 'aca753e8c15054fb5e58254674c451da33fc2d82522d308fc4255aefd6de637e'
N = 32

def cell(direction):
    x, y, z = [max(0, min(N - 1, int((v + 1) * N / 2))) for v in direction]
    return x + N * (y + N * z)

def build():
    source = Path(__file__).parent / 'Hipparcos.tsv'
    if hashlib.sha256(source.read_bytes()).hexdigest() != SHA256:
        raise ValueError('Source snapshot hash changed')
    stars = []
    with source.open(encoding='utf-8') as stream:
        for row in csv.DictReader(stream, delimiter='\t'):
            if not row['RAICRS'] or not row['DEICRS']:
                continue
            hip, mag = int(row['HIP']), float(row['Vmag'])
            if mag > 8:
                continue
            ra, dec = map(math.radians, (float(row['RAICRS']), float(row['DEICRS'])))
            direction = (math.cos(dec)*math.cos(ra), math.sin(dec), math.cos(dec)*math.sin(ra))
            # Quantize before assigning cells: CPU and GPU must index the same float32 values.
            direction = struct.unpack('<3f', struct.pack('<3f', *direction))
            bv = float(row['B-V']) if row['B-V'] else 0.65
            # Deliberately mild artistic tint from observed B-V, not calibrated RGB photometry.
            t = max(0, min(1, (bv + 0.3) / 2.3))
            rgb = (0.72 + 0.28*t, 0.84 - 0.14*t, 1 - 0.6*t)
            lum = sum(a*b for a,b in zip(rgb, (0.2126,0.7152,0.0722)))
            stars.append((cell(direction), hip, direction, mag, tuple(v/lum for v in rgb), bv))
    stars.sort(key=lambda s: (s[0], s[1]))
    records = [(N, len(stars), 2, 1)] + [(0,0,0,0)] * (N**3)
    for c, hip, direction, mag, rgb, bv in stars:
        start, count, _, _ = records[1+c]
        records[1+c] = (start if count else len(records), count+1, 0, 0)
        records.extend([(*direction, 10**(-0.4*mag)), (*rgb, mag)])
    output = ROOT / 'Assets' / 'Celestial'
    payload = struct.pack('<4sII', b'FFSC', 1, len(records))
    payload += b''.join(struct.pack('<4f', *r) for r in records)
    (output / 'Hipparcos.bin').write_bytes(payload)
    with (output / 'Hipparcos.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.writer(stream, lineterminator='\n')
        writer.writerow(['record', 'HIP', 'Vmag', 'B-V'])
        for i, (_, hip, _, mag, _, bv) in enumerate(stars):
            writer.writerow([1+N**3+2*i, hip, mag, bv])
    manifest = dict(source=URL, source_sha256=SHA256, stars=len(stars), magnitude_limit=8,
                    frame='ICRS; epoch J1991.25; +X RA 0, +Y north, +Z RA 90',
                    grid=N, binary_sha256=hashlib.sha256(payload).hexdigest(),
                    credit='Credit: ESA', license='CC BY-NC 3.0 IGO')
    (output / 'Hipparcos.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(manifest, indent=2))

if __name__ == '__main__':
    build()
