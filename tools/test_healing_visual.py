import struct
import unittest
from healing_visual import layer_keys


class HealingVisualTests(unittest.TestCase):
    def key(self, time, size, color):
        bits = struct.unpack('<I', struct.pack('<f', size))[0]
        return {'time': time, 'words': [0, 0, 0, 0, bits, bits, 0, 0, 0, 0, color]}

    def test_key_sorting_keeps_size_and_color(self):
        keys = layer_keys({'keys': [self.key(1, 2, 0x00736cf8), self.key(0, 1, 0xff736cf8)]})
        self.assertEqual([(k['Time'], k['Width'], k['ColorArgb']) for k in keys], [(0, 1, 0xff736cf8), (1, 2, 0x00736cf8)])

    def test_invalid_effect_timeline_fails(self):
        for key in [self.key(-1, 1, 0), self.key(.5, 0, 0), self.key(.5, float('nan'), 0)]:
            with self.assertRaisesRegex(ValueError, 'invalid'):
                layer_keys({'keys': [key]})
        with self.assertRaisesRegex(ValueError, 'empty'):
            layer_keys({'keys': []})


if __name__ == '__main__':
    unittest.main()
