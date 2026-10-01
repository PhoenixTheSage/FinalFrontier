"""Bake transparent fantasy constellation art slices (glowing embroidery style).

Writes Assets/Celestial/Art/<Name>.png and .rgba (512x512 RGBA8) for Texture2DArray upload.
"""
from __future__ import annotations

import hashlib
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ART_DIR = ROOT / 'Assets' / 'Celestial' / 'Art'
SIZE = 512

# Local UV embroidery paths in [-1,1], upright (+V up). Neon outline style.
FIGURES = {
    'Orion': {
        'color': (0.35, 0.95, 0.55),
        'paths': [
            [(-0.05, 0.78), (0.05, 0.78), (0.12, 0.62), (-0.12, 0.62), (-0.05, 0.78)],
            [(-0.38, 0.48), (0.38, 0.48), (0.28, -0.02), (-0.28, -0.02), (-0.38, 0.48)],
            [(-0.18, -0.02), (-0.30, -0.72)], [(0.18, -0.02), (0.30, -0.72)],
            [(-0.16, 0.22), (0.16, 0.22)],
            [(0.38, 0.42), (0.72, 0.18), (0.58, -0.22), (0.28, -0.02)],
            [(-0.05, 0.55), (-0.02, 0.42), (0.05, 0.42), (0.08, 0.55)],
        ],
    },
    'Ursa Major': {
        'color': (0.45, 0.85, 1.0),
        'paths': [
            [(-0.72, 0.12), (0.35, 0.18), (0.58, 0.48), (0.82, 0.35), (0.72, 0.02), (0.35, -0.08), (-0.72, -0.12), (-0.72, 0.12)],
            [(0.58, 0.48), (0.52, 0.68)], [(0.82, 0.35), (0.92, 0.58)],
            [(-0.38, -0.12), (-0.48, -0.58)], [(-0.08, -0.1), (-0.02, -0.58)], [(0.18, -0.06), (0.24, -0.52)],
            [(0.72, 0.02), (0.95, -0.05)],
        ],
    },
    'Cassiopeia': {
        'color': (0.95, 0.55, 0.85),
        'paths': [
            [(-0.48, -0.22), (0.48, -0.22), (0.48, 0.38), (-0.48, 0.38), (-0.48, -0.22)],
            [(-0.22, 0.38), (-0.22, 0.72), (0.22, 0.72), (0.22, 0.38)],
            [(-0.38, 0.72), (-0.28, 0.92), (0.0, 0.98), (0.28, 0.92), (0.38, 0.72)],
            [(-0.16, 0.12), (0.16, 0.12), (0.16, -0.22), (-0.16, -0.22)],
        ],
    },
    'Cygnus': {
        'color': (0.55, 0.75, 1.0),
        'paths': [
            [(-0.18, -0.08), (0.48, -0.02), (0.72, 0.28), (0.88, 0.48), (0.78, 0.58)],
            [(-0.18, -0.08), (-0.58, 0.38), (-0.88, 0.18)],
            [(-0.18, -0.08), (-0.52, -0.48), (-0.82, -0.28)],
            [(0.22, -0.04), (0.38, -0.42)],
            [(0.05, 0.05), (0.15, 0.12)],
        ],
    },
    'Crux': {
        'color': (0.95, 0.75, 0.35),
        'paths': [
            [(0.0, 0.88), (0.0, -0.88)], [(-0.58, 0.05), (0.58, 0.05)],
            [(-0.22, 0.58), (0.22, 0.58)], [(-0.22, -0.48), (0.22, -0.48)],
            [(-0.38, 0.05), (0.0, 0.38), (0.38, 0.05), (0.0, -0.28), (-0.38, 0.05)],
        ],
    },
    'Leo': {
        'color': (1.0, 0.72, 0.28),
        'paths': [
            [(-0.12, 0.38), (0.38, 0.58), (0.68, 0.35), (0.58, 0.02), (0.35, -0.08), (-0.22, -0.12),
             (-0.58, 0.18), (-0.38, 0.48), (-0.12, 0.38)],
            [(0.52, 0.28), (0.78, 0.32)],
            [(-0.05, -0.12), (-0.18, -0.58)], [(0.22, -0.08), (0.28, -0.58)],
            [(-0.58, 0.18), (-0.92, -0.05), (-0.78, 0.22)],
            [(0.42, 0.45), (0.48, 0.62), (0.58, 0.55)],
        ],
    },
    'Taurus': {
        'color': (0.85, 0.55, 0.35),
        'paths': [
            [(-0.18, -0.12), (0.18, -0.12), (0.38, 0.28), (0.18, 0.38), (-0.18, 0.38), (-0.38, 0.28), (-0.18, -0.12)],
            [(-0.38, 0.28), (-0.88, 0.78)], [(0.38, 0.28), (0.88, 0.78)],
            [(-0.1, 0.05), (-0.02, 0.14)], [(0.02, 0.05), (0.1, 0.14)],
            [(-0.08, -0.05), (0.08, -0.05)],
        ],
    },
    'Scorpius': {
        'color': (0.95, 0.35, 0.45),
        'paths': [
            [(-0.72, 0.58), (-0.28, 0.22), (-0.05, 0.38), (0.05, 0.38), (0.28, 0.22), (0.72, 0.58)],
            [(-0.15, 0.18), (0.15, 0.18), (0.08, -0.32), (-0.12, -0.52), (0.12, -0.72), (0.48, -0.55),
             (0.68, -0.32), (0.88, -0.48)],
            [(-0.55, 0.45), (-0.42, 0.55)], [(0.55, 0.45), (0.42, 0.55)],
        ],
    },
    'Centaurus': {
        'color': (0.55, 0.95, 0.75),
        'paths': [
            [(-0.18, 0.78), (0.12, 0.78), (0.18, 0.55), (-0.22, 0.55), (-0.18, 0.78)],
            [(-0.18, 0.55), (-0.18, 0.18), (0.48, 0.05), (0.88, 0.18), (0.98, -0.12)],
            [(0.48, 0.05), (0.58, -0.48)], [(-0.08, 0.12), (-0.18, -0.52)], [(0.22, 0.1), (0.28, -0.52)],
            [(0.12, 0.48), (0.48, 0.68)],
            [(-0.05, 0.68), (0.0, 0.62), (0.08, 0.62)],
        ],
    },
}


def lerp(a, b, t):
    return a + (b - a) * t


def stamp_glow(buf, x, y, rgb, alpha, radius=2.2):
    r = int(radius) + 2
    ix, iy = int(x), int(y)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            px, py = ix + dx, iy + dy
            if px < 0 or py < 0 or px >= SIZE or py >= SIZE:
                continue
            d = math.hypot(px - x, py - y)
            if d > radius:
                continue
            w = (1.0 - d / radius) ** 1.6 * alpha
            o = (py * SIZE + px) * 4
            for c in range(3):
                buf[o + c] = min(255, int(buf[o + c] + rgb[c] * 255 * w))
            buf[o + 3] = min(255, int(buf[o + 3] + 255 * w * 0.95))


def draw_path(buf, path, rgb):
    for i in range(len(path) - 1):
        x0, y0 = path[i]
        x1, y1 = path[i + 1]
        steps = max(8, int(math.hypot(x1 - x0, y1 - y0) * SIZE * 0.55))
        for s in range(steps + 1):
            t = s / steps
            u = lerp(x0, x1, t)
            v = lerp(y0, y1, t)
            # UV: +U right, +V up → pixel y flips
            px = (u * 0.5 + 0.5) * (SIZE - 1)
            py = (1.0 - (v * 0.5 + 0.5)) * (SIZE - 1)
            stamp_glow(buf, px, py, rgb, 0.55, radius=2.8)
            stamp_glow(buf, px, py, rgb, 1.0, radius=1.15)


def write_png(path: Path, buf: bytearray):
    # Minimal RGBA PNG via zlib (stdlib only).
    import zlib
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    raw = b''.join(b'\x00' + bytes(buf[y * SIZE * 4:(y + 1) * SIZE * 4]) for y in range(SIZE))
    ihdr = struct.pack('>IIBBBBB', SIZE, SIZE, 8, 6, 0, 0, 0)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    path.write_bytes(png)


def bake():
    ART_DIR.mkdir(parents=True, exist_ok=True)
    figures = list(json.loads((ROOT / 'Assets' / 'Celestial' / 'Constellations.json').read_text(encoding='utf-8'))['figures'])
    names = [f['name'] for f in figures]
    files = {}
    for name in names:
        spec = FIGURES[name]
        buf = bytearray(SIZE * SIZE * 4)
        for path in spec['paths']:
            draw_path(buf, path, spec['color'])
        stem = name.replace(' ', '_')
        rgba_path = ART_DIR / f'{stem}.rgba'
        png_path = ART_DIR / f'{stem}.png'
        rgba_path.write_bytes(bytes(buf))
        write_png(png_path, buf)
        files[stem] = {
            'png': png_path.name,
            'rgba': rgba_path.name,
            'sha256': hashlib.sha256(bytes(buf)).hexdigest(),
        }
        print(f'baked {stem}')
    manifest = {
        'resolution': SIZE,
        'format': 'rgba8',
        'figures': names,
        'files': files,
        'style': 'glowing embroidery fantasy outlines on transparent; sample with flipped figure right',
    }
    (ART_DIR / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'figures': len(names), 'dir': str(ART_DIR)}, indent=2))


if __name__ == '__main__':
    bake()
