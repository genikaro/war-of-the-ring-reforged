"""The front-end assets' font decoder and sky panorama (build-147), on synthetic data; runs without original assets."""
import struct
import unittest

from PIL import Image

import frontend_assets


def font(glyphs, size=12, space=5, spacing=0, width=4, height=2, layers=2, trailing=b''):
    """A one-size LIQDFONT: every pixel white, alpha counting up so each layer can be told apart."""
    head = b'LIQDFONT' + struct.pack('<f2I', 1.0, len(glyphs), 1) + struct.pack('<7I', size, 0, space, spacing, width, height, layers)
    pixels = b''.join(bytes([255, 255, 255, i % 256]) for i in range(width * height * layers))
    table = b''.join(struct.pack('<2I4f', code, advance, *uv) for code, advance, uv in glyphs)
    return head + pixels + table + trailing


class FontTests(unittest.TestCase):
    def test_layers_stack_and_codes_name_their_layer(self):
        sizes = frontend_assets.parse_font(font([(ord('A'), 3, (0, 0, .5, 1)), (0x10000 + ord('B'), 2, (.5, 0, 1, .5))]))
        self.assertEqual(len(sizes), 1)
        page = sizes[0]
        self.assertEqual((page['size'], page['space'], page['spacing']), (12, 5, 0))
        self.assertEqual(page['image'].size, (4, 4))
        self.assertEqual(page['image'].getpixel((0, 2))[3], 8)  # the second layer's first pixel
        a, b = page['glyphs']
        self.assertEqual((a['char'], a['advance'], a['x'], a['y'], a['width'], a['height']), (ord('A'), 3, 0, 0, 2, 2))
        self.assertEqual((b['char'], b['x'], b['y'], b['width'], b['height']), (ord('B'), 2, 2, 2, 1))

    def test_rejects_trailing_bytes_and_glyphs_outside_their_page(self):
        with self.assertRaises(ValueError):
            frontend_assets.parse_font(font([(ord('A'), 3, (0, 0, .5, 1))], trailing=b'\0'))
        with self.assertRaises(ValueError):
            frontend_assets.parse_font(font([(0x20000 + ord('A'), 3, (0, 0, .5, 1))]))
        with self.assertRaises(ValueError):
            frontend_assets.parse_font(b'LIQDTEXT' + bytes(40))


class SkyTests(unittest.TestCase):
    def test_panorama_puts_the_top_face_overhead_and_the_sides_round_the_horizon(self):
        side = Image.new('RGB', (8, 8), (200, 20, 20)); top = Image.new('RGB', (8, 8), (20, 20, 200))
        sky = frontend_assets.sky_panorama(side, top, 64, 32)
        self.assertEqual(sky.size, (64, 32))
        self.assertEqual(sky.getpixel((10, 0)), (20, 20, 200))
        self.assertEqual(sky.getpixel((10, 16)), (200, 20, 20))
        self.assertEqual(sky.getpixel((40, 31)), (200, 20, 20))


if __name__ == '__main__':
    unittest.main()
