"""Inspect shipped .wor level containers and inventory Levels.H2O.

Supported: world 125, metadata 102, terrain 104. Reads the fixed minimap,
pre-terrain records and CRC-checked terrain vertex blob sequentially. Locates
the interned name table by its repeated size and validated UTF-16 contents;
terrain layers, environment and object payloads remain explicitly opaque.
See NOTES-levels.md for reader addresses, evidence and remaining work.
"""
import argparse
from collections import Counter
from dataclasses import dataclass
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import struct
import sys
import zlib

import blast
from h2o import H2OArchive

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_LEVELS = ROOT / 'extracted' / 'Levels'
MINIMAP_SIZE = 128
MAX_TERRAIN_BYTES = 256 * 1024 * 1024
NAME_TABLE = re.compile(rb'(?=(.{4})(.{4})\1)', re.S)


class Reader:
    def __init__(self, data):
        self.data, self.pos = data, 0

    def read(self, size):
        if size < 0 or size > len(self.data) - self.pos:
            raise ValueError('truncated world data at 0x%x (need %d bytes)' % (self.pos, size))
        result = self.data[self.pos:self.pos + size]
        self.pos += size
        return result

    def take(self, fmt):
        values = struct.unpack('<' + fmt, self.read(struct.calcsize('<' + fmt)))
        return values[0] if len(values) == 1 else values

    def boolean(self):
        value = self.take('B')
        if value not in (0, 1):
            raise ValueError('invalid boolean at 0x%x' % (self.pos - 1))
        return bool(value)

    def string(self):
        if self.take('B') != 0:
            raise ValueError('invalid world string marker at 0x%x' % (self.pos - 1))
        count = self.take('I')
        raw = self.read(count * 2)
        if count and not raw.endswith(b'\0\0'):
            raise ValueError('unterminated world string at 0x%x' % (self.pos - len(raw)))
        return raw.decode('utf-16-le')[:-1] if count else ''


@dataclass
class World:
    metadata: dict
    minimap: bytes
    heights: list


def name_table(data, start):
    """Structural locator, NOT a sequential parse of the preceding terrain tail.

    World loader 0x56943f reads a byte size, followed by the string-table
    count and the same size again. Require the exact count and terminators,
    printable nonempty names and a unique candidate; never guess on ambiguity.
    """
    candidates = []
    for match in NAME_TABLE.finditer(data, start):
        offset = match.start()
        size, count = struct.unpack_from('<II', data, offset)
        end = offset + 4 + size
        if size < 10 or size % 2 or not 0 < count <= (size - 8) // 2 or end > len(data):
            continue
        try:
            text = data[offset + 12:end].decode('utf-16-le')
        except UnicodeDecodeError:
            continue
        names = text[:-1].split('\0') if text.endswith('\0') else []
        if len(names) == count and all(n and n.isprintable() for n in names):
            candidates.append({'offset': offset, 'byte_length': 4 + size, 'names': names})
    if len(candidates) != 1:
        raise ValueError('expected one validated world name table, found %d' % len(candidates))
    return candidates[0]


def section(name, start, end, decoded):
    return {'name': name, 'offset': start, 'byte_length': end - start, 'decoded': decoded}


def parse(data):
    r = Reader(data)
    world_version, metadata_version = r.take('2I')
    if (world_version, metadata_version) != (125, 102):
        raise ValueError('unsupported world/metadata version %d/%d' % (world_version, metadata_version))
    name, description = r.string(), r.string()
    unknown0, unknown1, width, height, flags, players, preview_flag = r.take('2I3HIB')
    if not width or not height:
        raise ValueError('zero map dimension')
    minimap_start = r.pos
    minimap = r.read(MINIMAP_SIZE * MINIMAP_SIZE * 2)
    records_start = r.pos
    records_present = r.boolean()
    records_count = r.take('I') if records_present else 0
    r.read(records_count * 96)
    if not r.boolean():
        raise ValueError('world has no terrain (not supported by this inspector)')
    terrain_start = r.pos
    terrain_version = r.take('I')
    if terrain_version != 104:
        raise ValueError('unsupported terrain version %d' % terrain_version)
    spacing, terrain_width, terrain_height, *parameters = r.take('7f')
    if not all(math.isfinite(v) for v in (spacing, terrain_width, terrain_height, *parameters)) or spacing <= 0:
        raise ValueError('invalid terrain dimensions/parameters')
    if terrain_width != width or terrain_height != height:
        raise ValueError('terrain dimensions disagree with world metadata')
    cells = [v / spacing for v in (terrain_width, terrain_height)]
    if any(not math.isclose(v, round(v), abs_tol=1e-5) for v in cells):
        raise ValueError('terrain extent is not an integral number of cells')
    grid = [round(v) + 1 for v in cells]
    terrain_set = r.string()
    terrain_set_id, packed_value = r.take('2I')
    blob_start = r.pos
    compressed, raw, crc = r.take('3I')
    if raw != grid[0] * grid[1] * 20 or raw > MAX_TERRAIN_BYTES:
        raise ValueError('terrain vertex byte size does not match the bounded grid')
    payload = r.read(compressed)
    try:
        vertices = payload if compressed == raw else blast.explode(payload, raw)
    except blast.BlastError as error:
        raise ValueError('terrain decompression failed: %s' % error) from error
    if len(vertices) != raw or zlib.crc32(vertices) != crc:
        raise ValueError('terrain vertex size/CRC mismatch')
    heights = [row[0] for row in struct.iter_unpack('<f16x', vertices)]
    if not all(math.isfinite(v) for v in heights):
        raise ValueError('non-finite terrain elevation')
    terrain_blob_end = r.pos
    names = name_table(data, terrain_blob_end)
    names_end = names['offset'] + names['byte_length']
    metadata = {
        'format_version': 1, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest(),
        'world_version': world_version, 'metadata_version': metadata_version,
        'name': name, 'description': description, 'width': width, 'height': height,
        'header': {'unknown': [unknown0, unknown1], 'flags': flags,
                   'player_count_field': players, 'preview_flag': preview_flag},
        'minimap': {'width': MINIMAP_SIZE, 'height': MINIMAP_SIZE, 'encoding': 'RGB565_LE'},
        'pre_terrain_records': {'present': records_present, 'count': records_count, 'record_bytes': 96},
        'terrain': {'version': terrain_version, 'cell_size': spacing, 'grid': grid,
                    'terrain_set': terrain_set, 'terrain_set_id': terrain_set_id,
                    'raw_parameters': parameters, 'packed_value': packed_value,
                    'vertex_stride': 20, 'vertex_count': len(heights),
                    'compressed_bytes': compressed, 'raw_bytes': raw, 'crc32': '%08x' % crc,
                    'crc_verified': True, 'height_min': min(heights), 'height_max': max(heights)},
        'name_table': names,
        'name_hints': {
            'script_waypoints': [n for n in names['names'] if n.startswith(('WP_', 'CWP_'))],
            'camera_paths': [n for n in names['names'] if n.startswith(('Camera Path ', 'CP_'))],
        },
        'sections': [
            section('metadata', 0, minimap_start, True),
            section('minimap', minimap_start, records_start, True),
            section('pre_terrain_records', records_start, terrain_start, False),
            section('terrain_header', terrain_start, blob_start, True),
            section('terrain_vertices', blob_start, terrain_blob_end, True),
            section('terrain_layers_and_environment', terrain_blob_end, names['offset'], False),
            section('name_table', names['offset'], names_end, True),
            section('world_records_and_trailing_data', names_end, len(data), False),
        ],
    }
    return World(metadata, minimap, heights)


def relative_path(name):
    """Archive names become output paths only after checking Windows traversal."""
    path = PurePosixPath(name.replace('\\', '/'))
    if path.is_absolute() or '..' in path.parts or any(':' in p or p.endswith(('.', ' ')) for p in path.parts):
        raise ValueError('unsafe level path: %s' % name)
    return Path(*path.parts)


def sources(path):
    if path.is_dir():
        for file in sorted(path.rglob('*')):
            if file.is_file() and file.suffix.lower() == '.wor':
                yield file.relative_to(path).as_posix(), file.read_bytes()
    elif path.suffix.lower() == '.h2o':
        archive = H2OArchive(path)
        for entry in sorted(archive.entries, key=lambda e: e.path.lower()):
            if entry.path.lower().endswith('.wor'):
                yield entry.path.replace('\\', '/'), archive.read(entry, verify=True)
    else:
        yield path.name, path.read_bytes()


def save_previews(world, minimap_path, height_path):
    from PIL import Image
    pixels = []
    for (value,) in struct.iter_unpack('<H', world.minimap):
        red, green, blue = value >> 11, (value >> 5) & 63, value & 31
        pixels.append(((red << 3) | (red >> 2), (green << 2) | (green >> 4), (blue << 3) | (blue >> 2)))
    image = Image.new('RGB', (MINIMAP_SIZE, MINIMAP_SIZE))
    image.putdata(pixels)
    minimap_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(minimap_path)
    terrain = world.metadata['terrain']
    low, high = terrain['height_min'], terrain['height_max']
    image = Image.new('L', tuple(terrain['grid']))
    image.putdata([round((h - low) * 255 / (high - low)) if high > low else 0 for h in world.heights])
    height_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(height_path)


def inventory(source, output, previews=False):
    source, output = Path(source).resolve(), Path(output).resolve()
    original = (ROOT / 'TLOTR - War of the Ring').resolve()
    if output.is_relative_to(original) or (source.is_dir() and output.is_relative_to(source)):
        raise ValueError('output directory must be outside the original install and source level directory')
    output.mkdir(parents=True, exist_ok=True)
    entries, terrain_sets, total_bytes, total_names = [], Counter(), 0, 0
    for name, raw in sources(source):
        relative = relative_path(name)
        entry = {'source': name, 'bytes': len(raw)}
        try:
            world = parse(raw)
            metadata = world.metadata
            metadata['source'] = name
            target = Path('maps') / relative.with_suffix('.json')
            entry.update(file=target.as_posix(), name=metadata['name'], width=metadata['width'],
                         height=metadata['height'], terrain_set=metadata['terrain']['terrain_set'],
                         name_count=len(metadata['name_table']['names']))
            if previews:
                for key, folder in [('minimap_preview', 'minimaps'), ('height_preview', 'heights')]:
                    entry[key] = (Path(folder) / relative.with_suffix('.png')).as_posix()
                save_previews(world, output / entry['minimap_preview'], output / entry['height_preview'])
            (output / target).parent.mkdir(parents=True, exist_ok=True)
            (output / target).write_text(json.dumps(metadata, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')
            terrain_sets[entry['terrain_set']] += 1
            total_names += entry['name_count']
            print('%s: %dx%d, %s, %d names, terrain CRC OK' % (
                name, entry['width'], entry['height'], entry['terrain_set'], entry['name_count']), flush=True)
        except (ValueError, struct.error) as error:
            entry['error'] = str(error)
            print('%s: ERROR %s' % (name, error), flush=True)
        entries.append(entry)
        total_bytes += len(raw)
    report = {'format_version': 1, 'source': str(source), 'map_count': len(entries),
              'decoded_count': sum('file' in e for e in entries), 'bytes': total_bytes,
              'interned_names': total_names, 'terrain_sets': dict(sorted(terrain_sets.items())), 'maps': entries}
    (output / 'index.json').write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest='command', required=True)
    inspect = sub.add_parser('inspect', help='inspect one extracted .wor')
    inspect.add_argument('file', type=Path)
    batch = sub.add_parser('inventory', help='inventory a Levels directory or H2O archive')
    batch.add_argument('source', nargs='?', type=Path, default=DEFAULT_LEVELS)
    batch.add_argument('--out-dir', type=Path, required=True)
    batch.add_argument('--previews', action='store_true', help='write RGB565 minimap and elevation previews (Pillow)')
    args = parser.parse_args(argv)
    try:
        if args.command == 'inspect':
            print(json.dumps(parse(args.file.read_bytes()).metadata, ensure_ascii=False, indent=2, allow_nan=False))
            return 0
        report = inventory(args.source, args.out_dir, args.previews)
        print('%d/%d maps inventoried, %d names, %d bytes' % (
            report['decoded_count'], report['map_count'], report['interned_names'], report['bytes']))
        return 0 if report['decoded_count'] == report['map_count'] and report['map_count'] else 1
    except (OSError, ValueError, struct.error, blast.BlastError) as error:
        parser.error(str(error))


if __name__ == '__main__':
    sys.exit(main())
