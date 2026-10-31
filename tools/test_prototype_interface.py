"""HUD atlas rectangle and source protection checks without original assets."""
import unittest
import struct
import prototype_interface


class InterfaceTests(unittest.TestCase):
    def test_cursor_descriptor_preserves_arrow_tip_and_centered_hotspots(self):
        def descriptor(x,y):
            return b'LIQDCRSR'+struct.pack('<fi4f',1,951,.045,.06,x,y)
        self.assertEqual(prototype_interface.cursor_hotspot(descriptor(.0148,.0443),(64,64)),[1,3])
        self.assertEqual(prototype_interface.cursor_hotspot(descriptor(.5074,.4983),(64,64)),[32,32])
        self.assertEqual(prototype_interface.cursor_hotspot(descriptor(.0148,.0443),(128,128)),[2,6])
        for raw in [b'',descriptor(-.1,.5),descriptor(float('nan'),.5)]:
            with self.assertRaises(ValueError):
                prototype_interface.cursor_hotspot(raw,(64,64))

    def test_uv_crop_uses_top_left_and_width_height(self):
        self.assertEqual(prototype_interface.crop_rect([.25, .125, .5, .5], (256, 128)), (64, 16, 192, 80))

    def test_crop_rejects_atlas_overflow_and_empty_rectangles(self):
        for rect in [[-.1, 0, .5, .5], [0, 0, 1.1, 1], [0, 0, 0, 1], [0, .9, 1, .5]]:
            with self.subTest(rect=rect), self.assertRaises(ValueError):
                prototype_interface.crop_rect(rect, (256, 256))

    def test_export_cannot_write_into_original_assets(self):
        for folder in ['extracted/interface-test', 'TLOTR - War of the Ring/interface-test']:
            with self.subTest(folder=folder), self.assertRaisesRegex(ValueError, 'outside'):
                prototype_interface.export(prototype_interface.ROOT / folder)


if __name__ == '__main__':
    unittest.main()
