"""Skirmish start layout of a multiplayer map (default MP02 Bilbo's Bluffs), decoded from the owner's install.

The editor markers sit in the world's "pre_terrain_records" block: a flag byte, a
u32 count, then fixed-size records (u32 type, u32 name index, u32 unknown, four
floats of which x, y, z follow a zero), 1 = start point, 2 = creature spawn area,
4 = script waypoint, 5 = water ripple generator (kinds named after their markers).
Everything here is a read-only decode; the runtime cache stores the results.
"""
import math
import struct
from pathlib import Path

import lmd
import prototype_obstacles
import wor

MAP_FILE = 'Multiplayer/MP02_Bilbos_Bluffs.wor'
MARKER_START, MARKER_CREATURE_SPAWN, MARKER_SCRIPT_WAYPOINT, MARKER_WATER_RIPPLE = 1, 2, 4, 5
MARKER_KINDS = (MARKER_START, MARKER_CREATURE_SPAWN, MARKER_SCRIPT_WAYPOINT, MARKER_WATER_RIPPLE)
RESOURCE_OBJECTS = {'OBJECT_RESOURCE_ORE_PILE': 'ore', 'OBJECT_RESOURCE_FOOD_WELL': 'food'}
CITADEL = 'BUILDING_G_TOWN_CENTER_LEVEL_1'
START_WORKERS = 4
SKIRMISH_UNITS = ['UNIT_G_HUMAN_WORKER', 'UNIT_G_GONDOR_SWORDSMAN', 'UNIT_G_DWARVEN_AXETHROWER', 'UNIT_G_HERO_FRODO',
                  # Tier 2: Barracks with Citadel 2, Dwarf Hall with a Forge, Ranger Post (captured unit tree, skirmish-012).
                  'UNIT_G_ROHAN_RIDER', 'UNIT_G_DWARVEN_SHIELDBREAKER', 'UNIT_G_RANGER']
TIER1_BUILDINGS = ['BUILDING_G_TOWN_CENTER_LEVEL_1', 'BUILDING_G_FOOD_STRUCTURE', 'BUILDING_G_BLACKSMITH',
                   'BUILDING_G_WORKER_STRUCTURE', 'BUILDING_G_GONDORIAN_BARRACKS', 'BUILDING_G_DWARF_BARRACKS',
                   'BUILDING_G_TOWER']
# Citadel level 2 opens these (captured Good building tree, local skirmish-013). Citadel 2 and the War Camp are upgrades
# of a Tier 1 building; the Wilderness Outpost, Ranger Post and Forge are placed by workers.
TIER2_BUILDINGS = ['BUILDING_G_TOWN_CENTER_LEVEL_2', 'BUILDING_G_POPULATION_STRUCTURE_UPGRADE', 'BUILDING_G_ARMORY',
                   'BUILDING_G_RANGER_BARRACKS', 'BUILDING_G_SKILLS_STRUCTURE']
SKIRMISH_BUILDINGS = TIER1_BUILDINGS + TIER2_BUILDINGS
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
        if name_index >= len(names) or not all(math.isfinite(v) for v in (x, y, z)) or kind not in MARKER_KINDS:
            raise ValueError('invalid marker record %d' % index)
        markers.append({'kind': kind, 'name': names[name_index], 'position': [x, y, z]})
    return markers


def map_file(name=None):
    """The multiplayer world for a map stem ("MP04_Wasteland") or file name; the default map when omitted."""
    if not name:
        return MAP_FILE
    stem = Path(name).stem
    if Path(name).name != name or Path(name).suffix not in ('', '.wor') or not stem.upper().startswith('MP'):
        raise ValueError('expected a multiplayer map stem such as MP04_Wasteland: ' + name)
    return 'Multiplayer/%s.wor' % stem


# File stems that drop an apostrophe the English title has.
TITLES = {'MP02_Bilbos_Bluffs': "Bilbo's Bluffs"}


def map_title(file):
    """English title from the file stem: MP04_Under_the_Canopy -> Under the Canopy (the localized names are Russian)."""
    stem = Path(file).stem
    return TITLES.get(stem) or stem.split('_', 1)[1].replace('_', ' ')


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


def placement_radii(resolver):
    """Source Objects.MinBuildingPlacementRadius of each resource kind in metres (ore pile 6, food well 7)."""
    return {kind: resolver.db['Objects'].get(name)['MinBuildingPlacementRadius'] for name, kind in RESOURCE_OBJECTS.items()}


def polygon_distance(point, polygon):
    """Distance from a point to a convex polygon, 0 inside it."""
    px, pz = point
    inside, best = True, math.inf
    for (ax, az), (bx, bz) in zip(polygon, polygon[1:] + polygon[:1]):
        dx, dz = bx-ax, bz-az
        if dx*(pz-az) - dz*(px-ax) < 0:
            inside = False
        t = max(0.0, min(1.0, ((px-ax)*dx + (pz-az)*dz) / (dx*dx + dz*dz)))
        best = min(best, math.hypot(px-ax-t*dx, pz-az-t*dz))
    return 0.0 if inside else best


START_SEARCH = (0.5, 20.0)


def start_position(polygon, centre, resources, radii, buildable, width, height, spacing, search=START_SEARCH):
    """Where a start Citadel goes: its marker unless its footprint comes within a resource's source
    MinBuildingPlacementRadius or covers ground that cannot be built on; then the nearest such spot on a fine
    lattice. The marker on several maps (MP02_Jungle_Fever, MP08_Crazy_Corners) puts a pile or well under the
    Citadel's walls. Returns the horizontal offset (dx, dz) to move the marker footprint by."""
    hull = [tuple(p[:2]) for p in polygon]
    if sum((b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0]) for a, b, c in zip(hull, hull[1:], hull[2:])) < 0:
        hull.reverse()
    xmin, xmax = min(p[0] for p in hull), max(p[0] for p in hull)
    zmin, zmax = min(p[1] for p in hull), max(p[1] for p in hull)

    def fits(dx, dz):
        shifted = [(x+dx, z+dz) for x, z in hull]
        if any(polygon_distance(r['position'][:2], shifted) < radii[r['kind']] for r in resources):
            return False
        for cz in range(math.floor((zmin+dz)/spacing), math.floor((zmax+dz)/spacing)+1):
            for cx in range(math.floor((xmin+dx)/spacing), math.floor((xmax+dx)/spacing)+1):
                if not (0 <= cx < width and 0 <= cz < height):
                    return False
                if not buildable[cz*width+cx] and polygon_distance(((cx+.5)*spacing, (cz+.5)*spacing), shifted) == 0:
                    return False
        return True

    step, reach = search
    rings = round(reach/step)
    offsets = sorted(((x*step, z*step) for z in range(-rings, rings+1) for x in range(-rings, rings+1)
                      if math.hypot(x, z) <= rings),
                     key=lambda o: (round(math.hypot(*o), 6), round(math.atan2(o[1], o[0]), 6)))
    found = next((o for o in offsets if fits(*o)), None)
    if found is None:
        raise ValueError('no room for a start Citadel within %.0f m of the start point at %.1f, %.1f' % (reach, *centre[:2]))
    return found


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
