"""Bake constellation stick figures (format 4).

Format 4 adds per-figure edge/member ranges and a precomputed plane normal
per edge so the GPU can draw stick lines without per-pixel Slerp/acos chains.
Art remains a Texture2DArray (unchanged).
"""
import csv
import hashlib
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Assets' / 'Celestial'
FIGURE_STRIDE = 8  # center, right+edgeStart, up+edgeCount, memberRange, name0-3

def load_directions():
    csv_path = ASSETS / 'Hipparcos.csv'
    hip_to_record = {int(r['HIP']): int(r['record']) for r in csv.DictReader(csv_path.open(encoding='utf-8'))}
    raw = (ASSETS / 'Hipparcos.bin').read_bytes()
    magic, version, records = struct.unpack_from('<4sII', raw, 0)
    if magic != b'FFSC' or version != 1:
        raise ValueError('Unexpected Hipparcos.bin header')
    floats = struct.unpack('<%df' % (records * 4), raw[12:])
    directions = {}
    for hip, record in hip_to_record.items():
        o = record * 4
        directions[hip] = (floats[o], floats[o + 1], floats[o + 2])
    return directions

def v_add(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def v_scale(a, s): return (a[0] * s, a[1] * s, a[2] * s)
def v_dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def v_cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def v_len(a): return math.sqrt(max(v_dot(a, a), 1e-20))
def v_norm(a):
    L = v_len(a)
    return (a[0] / L, a[1] / L, a[2] / L)

def pack_name(name):
    chars = []
    for ch in name.upper()[:16]:
        if 'A' <= ch <= 'Z':
            chars.append(float(ord(ch) - 64))  # 1..26
        elif ch == ' ':
            chars.append(27.0)
        else:
            chars.append(0.0)
    while len(chars) < 16:
        chars.append(0.0)
    return chars

def figure_frame(member_dirs):
    acc = (0.0, 0.0, 0.0)
    for d in member_dirs:
        acc = v_add(acc, d)
    center = v_norm(acc)
    north = (0.0, 1.0, 0.0)
    right = v_cross(north, center)
    if v_len(right) < 1e-4:
        right = v_cross((1.0, 0.0, 0.0), center)
    right = v_norm(right)
    up = v_norm(v_cross(center, right))
    radius = 0.0
    for d in member_dirs:
        c = max(-1.0, min(1.0, v_dot(center, d)))
        radius = max(radius, math.acos(c))
    radius = max(radius * 1.2, 0.04)
    return center, right, up, radius

def build():
    directions = load_directions()
    figures_json = json.loads((ASSETS / 'Constellations.json').read_text(encoding='utf-8'))['figures']
    edge_triples = []  # (a, b, n) per edge, grouped by figure
    member_dirs = []   # per-figure members (duplicates across figures OK)
    figure_meta = []
    for figure in figures_json:
        hips = set()
        edge_start = len(edge_triples)
        for a, b in figure['edges']:
            if a not in directions or b not in directions:
                raise ValueError(f"{figure['name']}: missing HIP {a if a not in directions else b}")
            da, db = directions[a], directions[b]
            n = v_cross(da, db)
            if v_len(n) < 1e-8:
                continue  # degenerate
            n = v_norm(n)
            edge_triples.append((da, db, n))
            hips.add(a)
            hips.add(b)
        edge_count = len(edge_triples) - edge_start
        member_start = len(member_dirs)
        dirs = []
        for h in sorted(hips):
            d = directions[h]
            member_dirs.append(d)
            dirs.append(d)
        member_count = len(member_dirs) - member_start
        center, right, up, radius = figure_frame(dirs)
        figure_meta.append({
            'name': figure['name'],
            'center': center, 'right': right, 'up': up, 'radius': radius,
            'edge_start': edge_start, 'edge_count': edge_count,
            'member_start': member_start, 'member_count': member_count,
            'chars': pack_name(figure['name']),
        })
    # header.w = format 4
    records = [(len(edge_triples), len(member_dirs), len(figure_meta), 4)]
    for a, b, n in edge_triples:
        records.append((*a, 0.0))
        records.append((*b, 0.0))
        records.append((*n, v_dot(a, b)))  # .w = cos(arc) for short-arc reject
    for direction in member_dirs:
        records.append((*direction, 1.0))
    for meta in figure_meta:
        records.append((*meta['center'], meta['radius']))
        records.append((*meta['right'], float(meta['edge_start'])))
        records.append((*meta['up'], float(meta['edge_count'])))
        records.append((float(meta['member_start']), float(meta['member_count']), 0.0, 0.0))
        c = meta['chars']
        records.append(tuple(c[0:4]))
        records.append(tuple(c[4:8]))
        records.append(tuple(c[8:12]))
        records.append(tuple(c[12:16]))
    payload = struct.pack('<4sII', b'FFCG', 1, len(records))
    payload += b''.join(struct.pack('<4f', *r) for r in records)
    (ASSETS / 'Constellations.bin').write_bytes(payload)
    manifest = {
        'format': 4,
        'figures': [f['name'] for f in figure_meta],
        'edges': len(edge_triples),
        'members': len(member_dirs),
        'figure_records': len(figure_meta),
        'figure_stride': FIGURE_STRIDE,
        'edge_stride': 3,
        'strokes': 0,
        'binary_sha256': hashlib.sha256(payload).hexdigest(),
        'credit': 'IAU/MacRobert stick figures via dcf21; fantasy art overlays are Texture2DArray slices; star positions Credit: ESA Hipparcos',
    }
    (ASSETS / 'Constellations.manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest, indent=2))

if __name__ == '__main__':
    build()
