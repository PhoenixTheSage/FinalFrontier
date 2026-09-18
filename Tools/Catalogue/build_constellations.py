"""Bake constellation stick figures into GPU directions from Hipparcos.bin."""
import csv
import hashlib
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Assets' / 'Celestial'

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

def build():
    directions = load_directions()
    figures = json.loads((ASSETS / 'Constellations.json').read_text(encoding='utf-8'))['figures']
    edges = []
    members = set()
    for figure in figures:
        for a, b in figure['edges']:
            if a not in directions or b not in directions:
                raise ValueError(f"{figure['name']}: missing HIP {a if a not in directions else b}")
            edges.append((directions[a], directions[b]))
            members.add(a)
            members.add(b)
    member_dirs = sorted((hip, directions[hip]) for hip in members)
    records = [(len(edges), len(member_dirs), 0, 1)]
    for a, b in edges:
        records.append((*a, 0.0))
        records.append((*b, 0.0))
    for _, direction in member_dirs:
        records.append((*direction, 1.0))
    payload = struct.pack('<4sII', b'FFCG', 1, len(records))
    payload += b''.join(struct.pack('<4f', *r) for r in records)
    out = ASSETS / 'Constellations.bin'
    out.write_bytes(payload)
    manifest = {
        'figures': [f['name'] for f in figures],
        'edges': len(edges),
        'members': len(member_dirs),
        'binary_sha256': hashlib.sha256(payload).hexdigest(),
        'credit': 'Curated stick figures; star positions Credit: ESA Hipparcos',
    }
    (ASSETS / 'Constellations.manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(manifest, indent=2))

if __name__ == '__main__':
    build()
