"""Validate real astrometry and spatial-query completeness, independently of rendering."""
import csv
import math
import random
import struct
import unittest
from build_catalogue import ROOT, N

class CatalogueTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        raw = (ROOT / 'Assets/Celestial/Hipparcos.bin').read_bytes()
        cls.records = list(struct.iter_unpack('<4f', raw[12:]))
        cls.stars = cls.records[1+N**3::2]
        with (ROOT / 'Assets/Celestial/Hipparcos.csv').open() as stream:
            cls.ids = {int(r['HIP']): int(r['record']) for r in csv.DictReader(stream)}

    def test_known_astrometry(self):
        sirius = self.records[self.ids[32349]]
        betelgeuse = self.records[self.ids[27989]]
        rigel = self.records[self.ids[24436]]
        self.assertEqual(len(self.ids), len(self.stars))
        self.assertAlmostEqual(math.degrees(math.atan2(sirius[2], sirius[0])) % 360, 101.287, places=2)
        self.assertAlmostEqual(math.degrees(math.asin(sirius[1])), -16.716, places=2)
        separation = math.degrees(math.acos(sum(a*b for a,b in zip(betelgeuse[:3],rigel[:3]))))
        self.assertTrue(18 < separation < 19)  # recognizable Orion geometry

    def test_queries_include_every_nearby_star(self):
        rng = random.Random(1789)
        for _ in range(80):
            direction = [rng.uniform(-1,1) for _ in range(3)]
            length = math.sqrt(sum(x*x for x in direction))
            direction = [x/length for x in direction]
            radius = rng.choice([1e-7, 0.001, 0.02, 0.1])
            lower = [max(0,min(31,math.floor((v-radius-2e-6+1)*16))) for v in direction]
            upper = [max(0,min(31,math.floor((v+radius+2e-6+1)*16))) for v in direction]
            candidates = set()
            for z in range(lower[2],upper[2]+1):
                for y in range(lower[1],upper[1]+1):
                    for x in range(lower[0],upper[0]+1):
                        start,count,_,_ = self.records[1+x+32*(y+32*z)]
                        candidates.update(range(int(start),int(start+2*count),2))
            for i,star in enumerate(self.stars):
                if sum((a-b)**2 for a,b in zip(direction,star[:3])) <= radius*radius:
                    self.assertIn(32769+2*i,candidates)

if __name__ == '__main__':
    unittest.main()
