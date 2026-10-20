"""Skirmish start layout for MP02 Bilbo's Bluffs, decoded from the owner's install.

The editor markers sit in the world's "pre_terrain_records" block: a flag byte, a
u32 count, then fixed-size records (u32 type, u32 name index, u32 unknown, four
floats of which x, y, z follow a zero), 1 = start point, 2 = creature spawn area.
Everything here is a read-only decode; the runtime cache stores the results.
"""
import math
import struct
from pathlib import Path

import lmd
import prototype_obstacles
import wor

MAP_FILE = 'Multiplayer/MP02_Bilbos_Bluffs.wor'
MARKER_START, MARKER_CREATURE_SPAWN = 1, 2
RESOURCE_OBJECTS = {'OBJECT_RESOURCE_ORE_PILE': 'ore', 'OBJECT_RESOURCE_FOOD_WELL': 'food'}
CITADEL = 'BUILDING_G_TOWN_CENTER_LEVEL_1'
START_WORKERS = 4
SKIRMISH_UNITS = ['UNIT_G_HUMAN_WORKER', 'UNIT_G_GONDOR_SWORDSMAN', 'UNIT_G_DWARVEN_AXETHROWER']
TIER1_BUILDINGS = ['BUILDING_G_TOWN_CENTER_LEVEL_1', 'BUILDING_G_FOOD_STRUCTURE', 'BUILDING_G_BLACKSMITH',
                   'BUILDING_G_WORKER_STRUCTURE', 'BUILDING_G_GONDORIAN_BARRACKS', 'BUILDING_G_DWARF_BARRACKS',
                   'BUILDING_G_TOWER']
WORKER_RING = (9.0, 13.0)


def parse_markers(data, metadata, names):
    """Decode the editor marker records of one world; reject any inconsistency."""
    section = next((s for s in metadata['sections'] if s['name'] == 'pre_terrain_records'), None)
    summary = metadata.get('pre_terrain_records') or {}
    if section is None or not summary.get('present'):
        return []
    size, count, offset = summary['record_bytes'], summary['count'], section['offset']
    if size != 96 or 5 + count*size > section['byte_length'] or offset + section['byte_length'] > len(data):
        raise ValueError('unsupported marker block layout')
    if data[offset] != 1 or struct.unpack_from('<I', data, offset+1)[0] != count:
        raise ValueError('marker block header does not match the world metadata')
    markers = []
    for index in range(count):
        base = offset + 5 + index*size
        kind, name_index = struct.unpack_from('<II', data, base)
        x, y, z = struct.unpack_from('<3f', data, base+16)
        if name_index >= len(names) or not all(math.isfinite(v) for v in (x, y, z)) or kind not in (1, 2):
            raise ValueError('invalid marker record %d' % index)
        markers.append({'kind': kind, 'name': names[name_index], 'position': [x, y, z]})
    return markers


def start_points(markers):
    points = [m for m in markers if m['kind'] == MARKER_START]
    if not points or any(not m['name'].startswith('Start Point ') for m in points):
        raise ValueError('skirmish map has no usable start points')
    points.sort(key=lambda m: int(m['name'].rsplit(' ', 1)[1]))
    return points


def resource_nodes(scene):
    """Ore piles and food wells as placed, with their source amounts."""
    nodes = []
    for obj in scene['placements']['objects']:
        kind = RESOURCE_OBJECTS.get(obj.get('database_name'))
        if kind:
            nodes.append({'id': obj['object_id'], 'name': obj['name'], 'kind': kind, 'model': obj['model'],
                          'position': obj['position'][:3], 'angles_degrees': obj['angles_degrees'],
                          'initial': obj['raw_words'][-1]})
    nodes.sort(key=lambda n: n['id'])
    return nodes


def selectable_objects(scene, resolver):
    """Source-selectable neutral objects, excluding scenery and click-through objects."""
    definitions, placements = {}, []
    for obj in scene['placements']['objects']:
        if obj.get('database_table') != 'Objects':
            continue
        record = resolver.db['Objects'].get(obj['reference'])
        if not record['IsSelectable'] or record['IgnoresMouseClicks']:
            continue
        if record.name not in definitions:
            portrait = resolver.db['UVAs'].get(record['Portrait'])
            definitions[record.name] = {'display_name': record['Name'],
                'portrait': Path(portrait['Filename'].replace('\\', '/')).stem if portrait else '',
                'has_health': bool(record['HasHealth']),
                'max_health': record['MaxHealth'] if record['HasHealth'] else 0,
                'armor': record['ArmorValue']}
        placements.append({'id': obj['object_id'], 'name': obj['name'], 'type': record.name})
    return {'definitions': definitions, 'placements': sorted(placements, key=lambda p: p['id'])}


def assign_resources(nodes, points):
    """Give each start point its nearest ore pile and food well."""
    result = []
    for point in points:
        near = {}
        for kind in ('ore', 'food'):
            candidates = [n for n in nodes if n['kind'] == kind]
            if not candidates:
                raise ValueError('map has no %s source' % kind)
            near[kind] = min(candidates, key=lambda n: (math.dist(n['position'][:2], point['position'][:2]), n['id']))['id']
        result.append(near)
    return result


def building_footprint(resolver, building, position, object_id, angle=0.0):
    """Oriented-collision footprint of a building (the existing obstacle policy)."""
    record = resolver.db['Buildings'].get(building)
    resolved = resolver.resolve(resolver.db['Models'].get(record['Model']).name, recurse=False)
    layer = resolver.db['BBoxes'].by_name['BBOX_LAYER_ORIENTED_COLLISION'].id
    boxes = [b for b in lmd.parse((wor.ROOT / resolved['file']).read_bytes()).bounding_boxes if b['layer'] == layer]
    if len(boxes) != 1:
        raise ValueError('expected one oriented collision box for ' + building)
    obj = {'object_id': object_id, 'name': record['Name'], 'position': list(position[:3]), 'scale': [1, 1, 1],
           'angles_degrees': [0.0, 0.0, angle]}
    return prototype_obstacles.footprint(boxes[0], obj, resolved['scale']), resolved['scale']


def worker_cells(allowed, width, height, spacing, centre, count=START_WORKERS, ring=WORKER_RING):
    """Deterministic start-worker cells spread around a building on connected ground."""
    cx, cz = centre[0]/spacing, centre[1]/spacing
    taken, cells = [], []
    for step in range(count):
        want = math.pi/4 + step*2*math.pi/count
        best = None
        for cell, ok in enumerate(allowed):
            if not ok:
                continue
            x, z = cell % width + .5, cell // width + .5
            distance = math.hypot(x-cx, z-cz)
            if not ring[0]/spacing <= distance <= ring[1]/spacing:
                continue
            if any(math.hypot(x-tx, z-tz) < 2.5 for tx, tz in taken):
                continue
            turn = abs((math.atan2(z-cz, x-cx)-want+math.pi) % (2*math.pi)-math.pi)
            key = (round(turn, 6), round(distance, 6), cell)
            if best is None or key < best[0]:
                best = (key, cell, (x, z))
        if best is None:
            raise ValueError('no free ground for start worker %d' % step)
        cells.append(best[1]); taken.append(best[2])
    return cells
