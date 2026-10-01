"""Rebuild Constellations.json edges from IAU / Alan MacRobert stick figures.

Source: https://github.com/dcf21/constellation-stick-figures
(constellation_lines_iau.dat — same figures as the IAU public constellation charts).
Educational charts such as go-astronomy follow these traditional Western patterns.
HIP endpoints missing from Assets/Celestial/Hipparcos.csv (no ICRS position in our
V<=8 snapshot) drop that edge only.
"""
import csv
import json
import re
from pathlib import Path
from urllib.request import urlopen

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Assets' / 'Celestial'
WANTED = {
    'Orion': 'Orion',
    'Ursa Major': 'UrsaMajor',
    'Cassiopeia': 'Cassiopeia',
    'Cygnus': 'Cygnus',
    'Crux': 'Crux',
    'Leo': 'Leo',
    'Taurus': 'Taurus',
    'Scorpius': 'Scorpius',
    'Centaurus': 'Centaurus',
}

def load_iau():
    text = urlopen(
        'https://raw.githubusercontent.com/dcf21/constellation-stick-figures/master/constellation_lines_iau.dat'
    ).read().decode()
    figures = {}
    cur = None
    for line in text.splitlines():
        line = line.strip()
        if line.startswith('* '):
            cur = line[2:].strip()
            figures[cur] = []
            continue
        if cur and line.startswith('['):
            hips = [int(re.sub(r'\D', '', tok)) for tok in re.findall(r'\d+\*?', line)]
            figures[cur].append(hips)
    return figures

def main():
    iau = load_iau()
    cat = {int(r['HIP']) for r in csv.DictReader((ASSETS / 'Hipparcos.csv').open(encoding='utf-8'))}
    figures = []
    for name, key in WANTED.items():
        edges = set()
        missing = []
        for chain in iau[key]:
            for a, b in zip(chain, chain[1:]):
                if a == b:
                    continue
                if a not in cat or b not in cat:
                    missing.append((a, b))
                    continue
                edges.add(tuple(sorted((a, b))))
        note = (
            'IAU / Alan MacRobert stick figure (constellation_lines_iau; same patterns as '
            'IAU public charts and traditional Western atlas figures such as go-astronomy).'
        )
        if missing:
            note += f' Dropped {len(missing)} edge(s) whose HIP lacks ICRS in the V<=8 catalogue snapshot.'
        figures.append({
            'name': name,
            'notes': note,
            'edges': [[a, b] for a, b in sorted(edges)],
        })
        print(f'{name}: {len(edges)} edges' + (f' (dropped {len(missing)})' if missing else ''))
    payload = {
        'credit': (
            'Stick figures: IAU / Alan MacRobert patterns via dcf21/constellation-stick-figures '
            '(constellation_lines_iau.dat). Star positions: ESA Hipparcos (CC BY-NC 3.0 IGO).'
        ),
        'figures': figures,
    }
    path = ASSETS / 'Constellations.json'
    path.write_text(json.dumps(payload, indent=2) + '\n', encoding='utf-8')
    print('wrote', path)

if __name__ == '__main__':
    main()
