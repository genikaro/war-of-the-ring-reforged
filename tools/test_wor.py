"""Synthetic level/archive tests; no game install or disassembler required."""
import contextlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

import wor


def pack(fmt, *values):
    return struct.pack('<' + fmt, *values)


def string(value):
    raw = (value + '\0').encode('utf-16-le')
    return b'\0' + pack('I', len(raw) // 2) + raw


def table(names):
    body = ''.join(n + '\0' for n in names).encode('utf-16-le')
    size = 8 + len(body)
    return pack('3I', size, len(names), size) + body


def world_bytes():
    data = pack('2I', 125, 102) + string('Test Map') + string('Описание')
    data += pack('2I3HIB', 0, 0, 2, 1, 0, 2, 1)
    data += pack('3H', 0xf800, 0x07e0, 0x001f) + bytes(32768 - 6)
    data += pack('BI', 1, 1) + bytes(96) + b'\1'
    data += pack('I7f', 104, 1, 2, 1, 0, 0, 0, 1) + string('Grasslands') + pack('2I', 0, 0xff3f3f3f)
    vertices = b''.join(pack('f4I', height, 0xffffffff, 0xff000000, 0, 32512) for height in [-2, 0, 3, 5, 9, 12])
    data += pack('3I', len(vertices), len(vertices), zlib.crc32(vertices)) + vertices
    # The inspector preserves this gap instead of inventing a terrain-layer decoder.
    data += b'\xff\xfe\xfd\xfc' * 4
    data += table(['Oak 0', 'WP_Start', 'CWP_Intro', 'CP_Flyover', 'Берёза 1'])
    return data + b'\x01\x02\x03\x04\x05'


def archive_bytes(world):
    def blob(payload):
        return pack('3I', len(payload), len(payload), zlib.crc32(payload)) + payload

    def strings(names):
        body = ''.join(n + '\0' for n in names).encode('utf-16-le')
        return blob(pack('2I', len(names), len(body)) + body)

    head = b'Synthetic H2O\r\n\x1a' + pack('IIQQ', 6, 1, len(world), len(world))
    tables = strings(['Multiplayer']) + strings(['Test.wor'])
    record = pack('IiiIIIQII', 0, 0, 0, 7, len(world), len(world), len(head) + 40 + len(tables), zlib.crc32(world), 0)
    return head + record + tables + world


def literal_dcl(data):
    # A literal-only DCL stream (lit=0, dictionary=4), terminated by raw size.
    bits = [bit for byte in data for bit in [0] + [(byte >> i) & 1 for i in range(8)]]
    bits += [0] * (-len(bits) % 8)
    return b'\0\4' + bytes(sum(bits[p + i] << i for i in range(8)) for p in range(0, len(bits), 8))


class WorldTests(unittest.TestCase):
    def test_metadata_elevations_and_interned_names(self):
        world = wor.parse(world_bytes())
        meta = world.metadata
        self.assertEqual((meta['name'], meta['description'], meta['width'], meta['height']), ('Test Map', 'Описание', 2, 1))
        self.assertEqual(meta['pre_terrain_records']['count'], 1)
        self.assertEqual(meta['terrain']['grid'], [3, 2])
        self.assertEqual(world.heights, [-2, 0, 3, 5, 9, 12])
        self.assertEqual((meta['terrain']['height_min'], meta['terrain']['height_max']), (-2, 12))
        self.assertTrue(meta['terrain']['crc_verified'])
        self.assertEqual(meta['name_table']['names'][4], 'Берёза 1')
        self.assertEqual(meta['name_hints'], {'script_waypoints': ['WP_Start', 'CWP_Intro'], 'camera_paths': ['CP_Flyover']})

    def test_sections_cover_every_byte_without_claiming_unknown_layouts(self):
        data = world_bytes()
        sections = wor.parse(data).metadata['sections']
        cursor = 0
        for section in sections:
            self.assertEqual(section['offset'], cursor)
            cursor += section['byte_length']
        self.assertEqual(cursor, len(data))
        self.assertFalse(next(s for s in sections if s['name'] == 'terrain_layers_and_environment')['decoded'])
        self.assertEqual(sections[-1]['byte_length'], 5)

    def test_bad_versions_grid_crc_and_truncation(self):
        data = world_bytes()
        meta = wor.parse(data).metadata
        terrain = next(s['offset'] for s in meta['sections'] if s['name'] == 'terrain_header')
        blob = next(s['offset'] for s in meta['sections'] if s['name'] == 'terrain_vertices')
        cases = [(0, pack('I', 124), 'world/metadata version'),
                 (terrain, pack('I', 103), 'terrain version'),
                 (terrain + 4, pack('f', 0), 'terrain dimensions'),
                 (terrain + 8, pack('f', 3), 'disagree'),
                 (blob + 4, pack('I', 0xfffffff0), 'bounded grid'),
                 (blob + 8, pack('I', 0), 'CRC mismatch')]
        for offset, value, message in cases:
            with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                wor.parse(data[:offset] + value + data[offset + len(value):])
        with self.assertRaisesRegex(ValueError, 'truncated'):
            wor.parse(data[:200])
        with self.assertRaisesRegex(ValueError, 'truncated'):
            wor.parse(data[:blob + 20])

    def test_bad_dcl_stream_is_a_parse_error(self):
        data = world_bytes()
        blob = next(s['offset'] for s in wor.parse(data).metadata['sections'] if s['name'] == 'terrain_vertices')
        data = data[:blob] + pack('3I', 2, 120, 0) + b'\xff\xff' + data[blob + 132:]
        with self.assertRaisesRegex(ValueError, 'decompression failed'):
            wor.parse(data)

    def test_compressed_vertex_blob_matches_the_stored_variant(self):
        data = world_bytes()
        expected = wor.parse(data)
        blob = next(s['offset'] for s in expected.metadata['sections'] if s['name'] == 'terrain_vertices')
        raw = data[blob + 12:blob + 132]
        compressed = literal_dcl(raw)
        changed = data[:blob] + pack('3I', len(compressed), len(raw), zlib.crc32(raw)) + compressed + data[blob + 132:]
        parsed = wor.parse(changed)
        self.assertEqual(parsed.heights, expected.heights)
        self.assertEqual(parsed.metadata['terrain']['crc32'], expected.metadata['terrain']['crc32'])

    def test_ambiguous_and_corrupt_name_tables_are_rejected(self):
        data = world_bytes()
        with self.assertRaisesRegex(ValueError, 'found 2'):
            wor.parse(data + table(['Another table']))
        start = wor.parse(data).metadata['name_table']['offset']
        data = data[:start + 4] + pack('I', 6) + data[start + 8:]
        with self.assertRaisesRegex(ValueError, 'found 0'):
            wor.parse(data)

    def test_archive_paths_cannot_escape_the_output_directory(self):
        for name in ['../outside.wor', '/absolute.wor', 'C:/outside.wor', '..\\outside.wor',
                     'maps/.. /outside.wor', 'maps/.../outside.wor']:
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'unsafe'):
                wor.relative_path(name)
        self.assertEqual(wor.relative_path('Multiplayer\\Map.wor'), Path('Multiplayer/Map.wor'))

    def test_directory_and_archive_inventories_match_and_preserve_sources(self):
        with tempfile.TemporaryDirectory() as temp, contextlib.redirect_stdout(io.StringIO()):
            root = Path(temp)
            source = root / 'input'
            (source / 'Multiplayer').mkdir(parents=True)
            data = world_bytes()
            (source / 'Multiplayer/Test.wor').write_bytes(data)
            archive = root / 'Levels.H2O'
            archive.write_bytes(archive_bytes(data))
            for src, dest in [(source, root / 'from-dir'), (archive, root / 'from-archive')]:
                report = wor.inventory(src, dest)
                self.assertEqual((report['map_count'], report['decoded_count'], report['interned_names']), (1, 1, 5))
                self.assertEqual(report['maps'][0]['file'], 'maps/Multiplayer/Test.json')
                self.assertEqual(json.loads((dest / 'index.json').read_text(encoding='utf-8')), report)
            self.assertEqual((root / 'from-dir/maps/Multiplayer/Test.json').read_bytes(),
                             (root / 'from-archive/maps/Multiplayer/Test.json').read_bytes())
            self.assertEqual((source / 'Multiplayer/Test.wor').read_bytes(), data)
            with self.assertRaisesRegex(ValueError, 'outside'):
                wor.inventory(source, source / 'generated')
            with self.assertRaisesRegex(ValueError, 'outside'):
                wor.inventory(source, wor.ROOT / 'TLOTR - War of the Ring/generated')

    def test_corrupt_archive_crc_fails_before_inspection(self):
        with tempfile.TemporaryDirectory() as temp:
            archive = Path(temp) / 'Levels.H2O'
            raw = archive_bytes(world_bytes())
            archive.write_bytes(raw[:-1] + bytes([raw[-1] ^ 1]))
            with self.assertRaisesRegex(ValueError, 'CRC mismatch'):
                list(wor.sources(archive))

    def test_inventory_reports_bad_map_without_losing_valid_maps(self):
        with tempfile.TemporaryDirectory() as temp, contextlib.redirect_stdout(io.StringIO()):
            root = Path(temp)
            (root / 'input').mkdir()
            (root / 'input/Bad.wor').write_bytes(b'bad')
            (root / 'input/Good.wor').write_bytes(world_bytes())
            report = wor.inventory(root / 'input', root / 'output')
            self.assertEqual((report['map_count'], report['decoded_count']), (2, 1))
            self.assertIn('error', report['maps'][0])
            self.assertTrue((root / 'output/maps/Good.json').exists())

    def test_preview_pixels_and_normalized_height_range(self):
        from PIL import Image
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            wor.save_previews(wor.parse(world_bytes()), root / 'minimap.png', root / 'height.png')
            with Image.open(root / 'minimap.png') as image:
                self.assertEqual([image.getpixel((x, 0)) for x in range(3)], [(255, 0, 0), (0, 255, 0), (0, 0, 255)])
            with Image.open(root / 'height.png') as image:
                self.assertEqual(image.size, (3, 2))
                self.assertEqual((image.getpixel((0, 0)), image.getpixel((2, 1))), (0, 255))


if __name__ == '__main__':
    unittest.main()
