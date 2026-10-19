"""Cloth particle grids, springs, pins, textures and material (v100/102/103).

The original records include authoring heap padding. Keep flags and unnamed
configuration words bit-exact, without serializing NaN padding as JSON floats.
The initial particle surface can be exported independently of a cloth solver.
"""
import struct

from asset_io import Reader


def _configuration(r):
    data = r.take(112)
    width, height = struct.unpack_from('<HH', data, 96)
    if width < 2 or height < 2:
        raise ValueError('invalid cloth grid dimensions')
    pins = [r.read('2I') for _ in range(r.count(8))]
    return {'width': width, 'height': height, 'words': list(struct.unpack('<28I', data)), 'pins': pins}


def parse(data):
    r = Reader(data)
    version = r.read('I')
    if version not in (100, 102, 103):
        raise ValueError('unsupported cloth version %d' % version)
    config = _configuration(r) if version >= 102 else None
    render_counts, flag = r.read('2I'), r.read('B')
    particles = []
    for _ in range(r.count(79 if version >= 102 else 83)):
        item = {'id': r.read('H'), 'position': r.read('4f'), 'velocity': r.read('4f'),
                'force': r.read('4f'), 'normal': r.read('4f'), 'mass': r.read('f'),
                'flag': r.read('B'), 'color': r.read('I')}
        if version < 102:
            item['neighbors'] = [n if n != 65535 else -1 for n in r.read('4H')]
        else:
            item['legacy_neighbors'] = r.read('I')
        particles.append(item)
    springs = []
    for _ in range(r.count(20)):
        kind, a, b = r.read('IHH')
        springs.append({'type': kind, 'a': a, 'b': b, 'parameters': r.read('3f')})
    if config is None:
        config = _configuration(r)
    if len(particles) != config['width'] * config['height']:
        raise ValueError('cloth particle count does not match grid')
    for spring in springs:
        if max(spring['a'], spring['b']) >= len(particles):
            raise ValueError('cloth spring endpoint outside grid')
    textures = r.read('8i') if version >= 102 else [r.read('i')] + [-1] * 7
    material = r.read('i')
    r.finish()
    return {'version': version, 'configuration': config, 'render_counts': render_counts,
            'flag': flag, 'particles': particles, 'springs': springs,
            'uvas': textures, 'material': material if version >= 102 else None,
            'legacy_texture': material if version == 100 else None}


def surface(cloth):
    """Initial source-space grid: positions, UVs and triangles for an importer."""
    width, height = (cloth['configuration'][k] for k in ('width', 'height'))
    positions = [p['position'][:3] for p in cloth['particles']]
    uvs = [[x / (width - 1), y / (height - 1)] for y in range(height) for x in range(width)]
    triangles = []
    for y in range(height - 1):
        for x in range(width - 1):
            a = y * width + x
            triangles.extend([[a, a + 1, a + width], [a + 1, a + width + 1, a + width]])
    return positions, uvs, triangles
