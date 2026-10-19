"""Bounds-checked WOR terrain, placement, passability and lake readers.

Extends wor's conservative container inventory. Unknown property words remain
integers; unsupported versions fail instead of scanning for plausible records.
See NOTES-world-scenes.md for executable evidence and rendering limits.
"""
from collections import Counter
import math
import struct
import zlib

import blast
import wor


def array(r, count, fmt='I'):
    size = struct.calcsize('<' + fmt)
    # Check before constructing a format string or allocating Python objects.
    if count < 0 or count > (len(r.data) - r.pos) // size:
        raise ValueError('invalid array size at 0x%x' % r.pos)
    return [v[0] if len(v) == 1 else list(v)
            for v in struct.iter_unpack('<' + fmt, r.read(count * size))]


def version(r, allowed):
    value = r.take('I')
    if value not in allowed:
        raise ValueError('unsupported section version %d at 0x%x' % (value, r.pos - 4))
    return value


def dimensions(x, y):
    if not 0 < x <= 65536 or not 0 < y <= 65536:
        raise ValueError('invalid section dimensions %s x %s' % (x, y))


def texture_grid(nx, ny, dx, dy):
    # Original loaders truncate a product with a stored float32 reciprocal,
    # then add one only when integer division has a remainder. For 24-cell
    # tiles the reciprocal rounds down: 456 cells therefore stores 18 tiles.
    # Replacing this with mathematical ceil misaligns two shipped lake grids.
    def axis(n, d):
        reciprocal = struct.unpack('<f', struct.pack('<f', 1.0 / d))[0]
        if reciprocal > 1.0 / d:  # Game's x87 mode rounds toward zero.
            bits = struct.unpack('<I', struct.pack('<f', reciprocal))[0] - 1
            reciprocal = struct.unpack('<f', struct.pack('<I', bits))[0]
        return int(n * reciprocal) + bool(n % d)
    return [axis(nx, dx), axis(ny, dy)]


def material(r):
    if r.read(8) != b'LIQDMATL':
        raise ValueError('invalid material header at 0x%x' % (r.pos - 8))
    ver = r.take('f')
    sizes = {1.0: 0x204, 2.0: 0x264, 3.0: 0x938}
    if ver not in sizes:
        raise ValueError('unsupported material version %s' % ver)
    return {'version': ver, 'raw_words': array(r, sizes[ver] // 4)}


def blob(r, expected):
    compressed, raw, crc = r.take('3I')
    if raw != expected or raw > wor.MAX_TERRAIN_BYTES:
        raise ValueError('invalid surface vertex byte size')
    payload = r.read(compressed)
    try:
        result = payload if compressed == raw else blast.explode(payload, raw)
    except blast.BlastError as error:
        raise ValueError('surface decompression failed: %s' % error) from error
    if len(result) != raw or zlib.crc32(result) != crc:
        raise ValueError('surface vertex size/CRC mismatch')
    return result


def terrain(r, world):
    t = world.metadata['terrain']
    nx, ny = [v - 1 for v in t['grid']]
    palette = r.boolean()
    count, parameter = r.take('2I')
    if count > 32:
        raise ValueError('too many terrain layers')
    palette_ids = None
    if palette and count:
        version(r, [100])
        palette_ids = array(r, 256)
    layers = []
    for _ in range(count):
        ver = version(r, [100] if palette else [102])
        dx, dy = r.take('2I'); dimensions(dx, dy)
        flag, enabled = r.boolean(), r.boolean()
        gx, gy = texture_grid(nx, ny, dx, dy)
        ids = array(r, gx * gy, 'B' if palette else 'i')
        if palette:
            ids = [palette_ids[i] for i in ids]
        layers.append({'version': ver, 'tile_cells': [dx, dy], 'grid': [gx, gy],
                       'flag': flag, 'enabled': enabled, 'texture_ids': ids})
    coords = []
    for _ in range(r.take('I')):
        ver = version(r, [100, 101])
        dx, dy, channels, usage = r.take('4I'); dimensions(dx, dy)
        if channels not in range(1, 5):
            raise ValueError('invalid terrain coordinate channel count')
        values = array(r, (dx + 1) * (dy + 1), '%df' % channels)
        item = {'version': ver, 'tile_cells': [dx, dy], 'channels': channels,
                'usage': usage, 'values': values}
        if ver == 101 and r.boolean():
            item['vertex_values'] = [array(r, t['vertex_count'], 'f') for _ in range(2)]
        coords.append(item)
    ver = version(r, [100, 101])
    dx, dy = r.take('2I'); dimensions(dx, dy)
    gx, gy = math.ceil((nx + 1) / dx), math.ceil((ny + 1) / dy)
    compositions = array(r, (gx - (ver == 100)) * (gy - (ver == 100)), 'B')
    ver2 = version(r, [100])
    tx, ty = r.take('2I'); dimensions(tx, ty)
    tint_grid = [math.ceil(nx / tx), math.ceil(ny / ty)]
    tile_colors = array(r, math.prod(tint_grid))
    params = list(r.take('4I'))
    materials = []
    for _ in range(r.take('I')):
        bindings = array(r, r.take('I'), '2I')
        materials.append({'bindings': bindings, 'material': material(r)})
    texture_lookup = array(r, r.take('I'), '2I')
    # Fixed environment payload in world version 125 (0x5691d7).
    environment = {'raw_words_a': array(r, 8), 'raw_vector_a': array(r, 4, 'f'),
                   'raw_word_b': r.take('I'), 'raw_vectors_b': array(r, 2, '4f'),
                   'byte_a': r.take('B'), 'raw_words_c': array(r, 2), 'flag_b': r.boolean()}
    return {'layers': layers, 'layer_parameter': parameter, 'palette_ids': palette_ids,
            'coordinate_sets': coords,
            'compositions': {'version': ver, 'tile_cells': [dx, dy], 'grid': [gx, gy], 'values': compositions},
            'tile_colors': {'version': ver2, 'tile_cells': [tx, ty], 'grid': tint_grid, 'values': tile_colors},
            'parameters': params, 'materials': materials, 'texture_lookup': texture_lookup,
            'environment': environment}


def placements(r, names):
    players = array(r, r.take('I'), '20I') if r.boolean() else []
    objects = []
    count = r.take('I') if r.boolean() else 0
    for _ in range(count):
        offset = r.pos
        name, kind, ref, variant = r.take('4I')
        position, scale = list(r.take('4f')), list(r.take('4f'))
        angles = list(r.take('3f'))
        words = list(r.take('7I'))  # 0x3c..0x48, 0x54, 0x60, 0x64
        flag_a, flag_b = r.boolean(), r.boolean()
        object_id, flag_c = r.take('I'), r.boolean()
        pairs = array(r, words[4], '2I')
        if name >= len(names) or not all(math.isfinite(v) for v in position + scale + angles):
            raise ValueError('invalid placed object at 0x%x' % offset)
        objects.append({'offset': offset, 'name': names[name], 'name_index': name,
                        'kind': kind, 'reference': ref, 'model_variant': variant,
                        'owner': words[0], 'position': position,
                        'scale': scale, 'angles_degrees': angles, 'raw_words': words,
                        'flags': [flag_a, flag_b, flag_c], 'object_id': object_id, 'pairs': pairs})
    # Placed effects: the first word is uninitialised/opaque, followed by a
    # name-pool index. Effects references are at in-memory offsets 0x3c/0x40.
    other = []
    for _ in range(r.take('I') if r.boolean() else 0):
        refs = list(r.take('2I'))
        if refs[1] >= len(names):
            raise ValueError('placed effect name index is out of range')
        other.append({'references': refs, 'name': names[refs[1]], 'position': list(r.take('4f')),
                      'scale': list(r.take('4f')), 'raw_words': array(r, 8), 'flag': r.boolean()})
    lines, points = [], []
    if r.boolean():
        lines = array(r, r.take('I'), '8I')
        points = array(r, r.take('I'), '4f')
    return {'players': players, 'objects': objects, 'other_placeables': other,
            'lines': lines, 'line_points': points}


def bitset(r):
    set_count, count = r.take('2I')
    words = array(r, (count + 31) // 32)
    if set_count > count:
        raise ValueError('invalid bitset population')
    # The first field is the cached population count, not an opaque word.
    actual = sum(v.bit_count() for v in words)
    if words and count % 32:
        actual -= (words[-1] >> (count % 32)).bit_count()
    if actual != set_count:
        raise ValueError('bitset population mismatch at 0x%x' % r.pos)
    return {'count': count, 'set_count': set_count, 'words': words}


def path_graph(r):
    ver = version(r, [100, 101])
    x, y, n, m = r.take('4I'); dimensions(x, y)
    regions = []
    for _ in range(n):
        version(r, [100])
        a, b, count = r.take('3I')
        vector, words = list(r.take('4f')), array(r, 7)
        links = []
        for _ in range(count):
            header = list(r.take('3I'))
            links.append({'header': header, 'indices': array(r, header[2], 'H')})
        regions.append({'header': [a, b, count], 'vector': vector, 'raw_words': words,
                        'links': links, 'link_properties': array(r, count, '5I')})
    portals = []
    for _ in range(m):
        version(r, [100])
        a, count, b = r.take('3I')
        portals.append({'header': [a, count, b], 'indices': array(r, count)})
    matrices = [bitset(r), bitset(r)] if r.boolean() else []
    word, count = r.take('2I')
    sizes = array(r, count)
    tables = [r.read(size).hex() for size in sizes]
    return {'version': ver, 'grid': [x, y], 'regions': regions, 'portals': portals,
            'matrices': matrices, 'table_word': word, 'tables': tables}


def region_cells(graph):
    """Per-cell region IDs from the row tables; -1 marks cells without a region.

    Each row is a sequence of u16 (region, run length - 1) pairs, 0xffff for
    no region; the table word is the row width.
    """
    width, height = graph['grid']
    if graph['table_word'] != width or len(graph['tables']) != height:
        raise ValueError('region tables do not match the pathing grid')
    cells = []
    for row in graph['tables']:
        raw = bytes.fromhex(row)
        if len(raw) % 4:
            raise ValueError('incomplete region run')
        start = len(cells)
        for region, run in struct.iter_unpack('<2H', raw):
            if region != 0xffff and region >= len(graph['regions']):
                raise ValueError('region run index out of range')
            cells.extend([-1 if region == 0xffff else region] * (run + 1))
        if len(cells) - start != width:
            raise ValueError('region runs do not fill their row')
    return cells


def pathing(r):
    categories = []
    for _ in range(r.take('I') if r.boolean() else 0):
        flag, rid = r.boolean(), r.take('I')
        name, depth, slope = r.string(), r.take('f'), r.take('f')
        item = {'enabled': flag, 'id': rid, 'name': name, 'max_water_depth': depth, 'max_slope': slope}
        if r.boolean():
            item['grid'] = list(r.take('2I')); item['painted'] = bitset(r)
            if math.prod(item['grid']) != item['painted']['count']:
                raise ValueError('pathing grid size mismatch')
        for key in ['mask_b', 'mask_c']:
            if r.boolean():
                item[key] = bitset(r)
        if r.boolean():
            item['graph_id'] = r.take('I'); item['graph'] = path_graph(r)
        categories.append(item)
    lookup = array(r, r.take('I')) if r.boolean() else []
    return {'categories': categories, 'region_lookup': lookup}


def lake_surface(r):
    ver = version(r, [101])
    cell_size, multiplier, width, height = r.take('4f')
    nx, ny, gx, gy = r.take('4I'); dimensions(gx, gy)
    if gx != nx + 1 or gy != ny + 1 or cell_size <= 0:
        raise ValueError('invalid lake vertex grid')
    offset = list(r.take('4f'))
    vertices = blob(r, gx * gy * 20)
    layers = []
    count, param = r.take('2I')
    for _ in range(count):
        version(r, [102])
        channels, dx, dy = r.take('3I'); dimensions(dx, dy)
        if channels not in range(1, 5):
            raise ValueError('invalid lake UV channels')
        flags = [r.boolean(), r.boolean(), r.boolean()]
        grid = texture_grid(nx, ny, dx, dy)
        ids = array(r, math.prod(grid), 'i')
        uvs = array(r, (dx + 1) * (dy + 1), '%df' % channels)
        layers.append({'channels': channels, 'tile_cells': [dx, dy], 'flags': flags,
                       'grid': grid, 'texture_ids': ids, 'uvs': uvs})
    filters = []
    count, filter_param = r.take('2I')
    for _ in range(count):
        version(r, [101])
        filters.append({'word': r.take('I'), 'flags': [r.boolean(), r.boolean()],
                        'word_b': r.take('I'), 'vector': list(r.take('4f')), 'word_c': r.take('I')})
    v = version(r, [100, 101])
    grid_flags = {'version': v, 'enabled': r.boolean(), 'word': r.take('I')}
    if v == 101:
        grid_flags.update(word_b=r.take('I'), flag=r.boolean(), words=array(r, 4))
    extras = r.take('I')
    if extras:
        raise ValueError('unsupported lake ripple array at 0x%x' % r.pos)
    params = array(r, 10)
    mat = material(r)
    return {'version': ver, 'cell_size': cell_size, 'multiplier': multiplier,
            'width': width, 'height': height, 'grid': [gx, gy], 'offset': offset,
            'vertices': [list(row) for row in struct.iter_unpack('<f4I', vertices)],
            'layers': layers, 'layer_parameter': param, 'filters': filters,
            'filter_parameter': filter_param, 'grid_flags': grid_flags,
            'parameters': params, 'material': mat}


def lakes(r):
    result = []
    for _ in range(r.take('I') if r.boolean() else 0):
        rid, name = r.take('I'), r.string()
        height = r.take('f'); words_a = array(r, 6)
        enabled = r.boolean(); words_b = array(r, 11)
        surface = lake_surface(r) if r.boolean() else None
        result.append({'id': rid, 'name': name, 'height': height, 'raw_words_a': words_a,
                       'enabled': enabled, 'raw_words_b': words_b, 'surface': surface})
    selected = r.take('I') if result else None
    return {'lakes': result, 'selected': selected}


def curve(r, quaternion=False):
    """Keep spline keys as words until their interpolation is implemented."""
    version(r, [101])
    header = array(r, 2 if quaternion else 3)
    result = {'header': header}
    if not quaternion:
        result['flag'] = r.take('B')
        if r.boolean():
            rows, columns = r.take('2I')
            result['grid'] = [rows, columns]
            result['values'] = array(r, rows * columns)
    result['keys'] = array(r, r.take('I'), '24I' if quaternion else '21I')
    return result


def world_tail(r, world):
    """Sequential v125 tail, including the editor's placed-decal records."""
    waypoints = array(r, r.take('I'), '12I') if r.boolean() else []
    lights = []
    for _ in range(r.take('I') if r.boolean() else 0):
        item = {'header': list(r.take('I2B2I'))}
        item['curves'] = [curve(r), curve(r, True), curve(r)]
        item['keys'] = array(r, r.take('I'), '17I')
        lights.append(item)
    gx, gy = world.metadata['terrain']['grid']
    grid = array(r, ((gx >> 1) + 1) * ((gy >> 1) + 1))
    properties = list(r.take('B8I'))
    regions = array(r, properties[-1], '4IB3I')
    paths = []
    for _ in range(r.take('I') if r.boolean() else 0):
        head = list(r.take('4f3I'))
        paths.append({'header': head, 'keys': array(r, head[-1], '4IB3IB')})
    camera = list(r.take('8IB'))
    markers = array(r, r.take('I'), '5I') if r.boolean() else []
    decals = []
    for _ in range(r.take('I') if r.boolean() else 0):
        refs = list(r.take('2I'))
        position, scale = list(r.take('4f')), list(r.take('4f'))
        words = array(r, 5); flags = array(r, 3, 'B')
        angles = list(struct.unpack('<3f', struct.pack('<3I', *words[:3])))
        alpha = struct.unpack('<f', struct.pack('<I', words[4]))[0]
        if not all(math.isfinite(v) for v in position + scale + angles + [alpha]):
            raise ValueError('non-finite placed decal transform')
        decals.append({'object_id': refs[0], 'decal_id': refs[1], 'position': position,
                       'scale': scale, 'angles_degrees': angles, 'color_argb': words[3],
                       'alpha': alpha, 'priority': flags[0], 'flags': flags[1:]})
    fog = []
    for _ in range(r.take('I')):
        rid = r.take('I'); position = list(r.take('4f'))
        fog.append({'id': rid, 'position': position, 'data_hex': r.read(32768).hex()})
    final = list(r.take('B4I'))
    if r.pos != len(r.data):
        raise ValueError('trailing world records at 0x%x' % r.pos)
    return {'camera_waypoints': waypoints, 'lights': lights, 'grid': grid,
            'properties': properties, 'regions': regions, 'camera_paths': paths,
            'camera': camera, 'markers': markers, 'decals': decals, 'fog': fog, 'final': final}


def parse(data):
    world = wor.parse(data)
    r = wor.Reader(data)
    s = world.metadata['sections'][4]
    r.pos = s['offset'] + s['byte_length']
    start = r.pos
    terr = terrain(r, world)
    if r.pos != world.metadata['name_table']['offset']:
        raise ValueError('terrain/environment does not reach the validated name table: 0x%x' % r.pos)
    sections = [wor.section('terrain_layers_and_environment', start, r.pos, True)]
    r.pos += world.metadata['name_table']['byte_length']
    start = r.pos; placed = placements(r, world.metadata['name_table']['names'])
    sections.append(wor.section('placements', start, r.pos, True))
    start = r.pos; paths = pathing(r)
    sections.append(wor.section('pathing', start, r.pos, True))
    start = r.pos; water = lakes(r)
    sections.append(wor.section('lakes', start, r.pos, True))
    start = r.pos; tail = world_tail(r, world)
    sections.append(wor.section('world_tail', start, r.pos, True))
    return {'world': world, 'terrain': terr, 'placements': placed, 'pathing': paths,
            'water': water, 'tail': tail, 'sections': sections}


def resolve_objects(scene, db):
    """World object class 0/1/2 selects Units/Buildings/Objects (not model class)."""
    tables = {0: 'Units', 1: 'Buildings', 2: 'Objects'}
    for obj in scene['placements']['objects']:
        table = tables.get(obj['kind'])
        rec = db[table].get(obj['reference']) if table else None
        model_id = rec.get('Model') if rec else None
        if rec and table == 'Objects':
            event = db['ModelEvents'].get(rec['ModelEvent'])
            model_id = event.get('AssociatedModelResource%d' % obj['model_variant']) if event else None
            obj['model_event'] = event.name if event else None
        model = db['Models'].get(model_id) if model_id is not None else None
        obj['database_table'] = table
        obj['database_name'] = rec.name if rec else None
        obj['model'] = model.name if model else None
        obj['is_walkable'] = bool(rec.get('IsWalkable', 0)) if rec else False
        lookup = db['MeshPathLookups'].get(rec.get('MeshPathLookup', -1)) if rec else None
        obj['mesh_path_lookup'] = {'name': lookup.name, 'file': lookup['Filename']} if lookup else None
    for effect in scene['placements']['other_placeables']:
        rec = db['Effects'].get(effect['raw_words'][3])
        effect['effect'] = rec.name if rec else None
    return Counter(obj['model'] for obj in scene['placements']['objects'])


def resolve_decals(scene, db):
    """Static editor decals and persistent decals attached to placed objects."""
    result = []
    for item in scene['tail']['decals']:
        result.append(dict(item, source='editor'))
    for obj in scene['placements']['objects']:
        record = db[obj['database_table']].get(obj['reference'])
        rid = record.get('Decal', -1)
        if rid >= 0:
            result.append({'object_id': obj['object_id'], 'decal_id': rid, 'source': 'object',
                           'position': obj['position'], 'scale': obj['scale'],
                           'angles_degrees': obj['angles_degrees'], 'color_argb': 0xffffffff,
                           'alpha': 1.0})
    for item in result:
        record = db['Decals'].get(item['decal_id'])
        if not record:
            raise ValueError('missing decal definition %d' % item['decal_id'])
        item.update(name=record.name, texture_id=record['FirstStageTexture'],
                    width=record['StartWidth'] * item['scale'][0],
                    height=record['StartHeight'] * item['scale'][1],
                    definition_alpha=record['Alpha'],
                    blends_with_terrain=bool(record['BlendsWithTerrainDiffuse']))
    scene['decals'] = result
    return result
