"""Build-152 map lighting block, on a synthetic world; runs without original assets."""
import struct
import unittest
from types import SimpleNamespace

import map_lighting


def word(value):
    return struct.unpack('<I', struct.pack('<f', value))[0]


def scene(time=0.7, clouds=True):
    tail = {'time_of_day': [0, 0xffffffd5, word(0.3), word(0.25), word(3600.0), word(0.75), word(time), word(1.0), 1],
            'sun_keyframes': [[0, 0xff8040c0, word(8.0), word(1.5), 0, 0, word(1.0), 0]],
            'grass': [889, 4, 0, 0, 0, 0, word(1.43), 0xff9eff12, 1],
            'final': [1, 0xffffffff, word(16.0), word(16.0), 0]}
    env = {'raw_words_a': [0, 0] + [word(v) for v in (16, 100, 69, 28, 80, 50)], 'raw_vector_a': [0.6, 0.8, 0.0, 1.0],
           'raw_words_c': [0x4c2d3728, word(0.38)], 'flag_b': clouds}
    world = SimpleNamespace(metadata={'width': 200.0, 'height': 200.0})
    return {'world': world, 'tail': tail, 'terrain': {'environment': env}}


class MapLightingTest(unittest.TestCase):
    def test_block_names_the_editor_settings(self):
        block = map_lighting.lighting(scene())
        self.assertEqual(block['time_of_day'], 0.7)
        self.assertEqual(block['ambient'], {'color': [1.0, 1.0, 0.8353], 'intensity': 0.3})
        self.assertEqual(block['sun']['color'], [0.502, 0.251, 0.7529])
        self.assertEqual(block['sun']['intensity'], 1.5)
        self.assertEqual(block['clouds']['humidity'], [16, 100, 69])
        self.assertEqual(block['clouds']['wind'], [28, 80, 50])
        self.assertEqual(block['clouds']['wind_direction'], [-0.6, 0.8])
        self.assertEqual(block['fog_of_war'], {'color': [0.1765, 0.2157, 0.1569], 'alpha': 0.38})
        self.assertEqual(block['grass'], {'enabled': True, 'color': [0.6196, 1.0, 0.0706], 'height': 1.43})
        self.assertTrue(block['sky']['skybox'])
        self.assertFalse(map_lighting.lighting(scene(clouds=False))['clouds']['enabled'])

    def test_sun_crosses_the_south_edge_from_east_to_west(self):
        # Rings.exe 0x53c80a: x = width at sunrise, the middle at noon, 0 at sunset; y = 0; z = the map's length.
        self.assertEqual(map_lighting.sun_position(0.25, 0.25, 0.75, 200, 100), (200, 0.0, 100))
        self.assertEqual(map_lighting.sun_position(0.5, 0.25, 0.75, 200, 100), (100, 0.0, 100))
        # At night the moon goes back across, starting from x = 0 at sunset.
        self.assertEqual(map_lighting.sun_position(0.75, 0.25, 0.75, 200, 100), (0, 0.0, 100))
        self.assertEqual(map_lighting.sun_position(0.0, 0.25, 0.75, 200, 100), (100, 0.0, 100))

    def test_noon_light_travels_north_and_down_in_godot_axes(self):
        x, y, z = map_lighting.sun_direction(0.5, 0.25, 0.75, 200, 200)
        self.assertAlmostEqual(x, 0.0)
        self.assertLess(y, 0)
        self.assertGreater(z, 0)
        self.assertAlmostEqual(x * x + y * y + z * z, 1.0, places=5)
        # Late afternoon: the sun is in the west (source -x), so the light travels east, which is Godot -x.
        self.assertLess(map_lighting.sun_direction(0.7, 0.25, 0.75, 200, 200)[0], 0)


if __name__ == '__main__':
    unittest.main()
