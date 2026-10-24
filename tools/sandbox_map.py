"""The Sandbox test map (build-56): a plain, level, empty field for trying units out.

It is cut from the Bilbo's Bluffs source world, so the ground texture, the resources and every rule stay original. The
terrain is levelled and covered in the map's plain grass tiles (its primary all-grass variants, scattered) in one
even tint; every cell is walkable and buildable; scenery, water, editor decals, effects and places of power are gone;
and two start points face each other across open ground, each with one food well and one ore pile behind it.

python tools/prepare_prototype.py --sandbox
"""
from collections import Counter
import random
import statistics
import struct

import skirmish_map

SOURCE = skirmish_map.MAP_FILE
STEM = 'Sandbox'
TITLE = 'Sandbox'
GROUND = 0.0
# Start points in source metres (x, y), level with each other 80 m apart across the middle of the 200 m field.
STARTS = [(60.0, 100.0), (140.0, 100.0)]
# Each base's well and ore pile from its start point, behind it (away from the middle); mirrored for the second base.
RESOURCE_OFFSETS = {'food': (-20.0, 10.0), 'ore': (-20.0, -10.0)}
OPEN_CATEGORIES = ('PATHING_CATEGORY_WALKER', 'PATHING_CATEGORY_BUILDING')


def all_set(count):
    words = [0xFFFFFFFF] * ((count + 31) // 32)
    if count % 32:
        words[-1] = (1 << (count % 32)) - 1
    return {'count': count, 'set_count': count, 'words': words}


def plain_ground(texture_ids, resolver):
    """The source map's primary all-grass tiles (TerrainSetTextures/<set>/Primary/<set>_tttt_NN): transition tiles
    would repeat their dirt patches across the field. The commonest tile when the map has none."""
    used = Counter(i for i in texture_ids if i >= 0)
    plain = sorted(i for i in used if resolver and (entry := resolver.texture(i))
                   and '/Primary/' in entry['file'].replace('\\', '/') and '_tttt_' in entry['file'])
    return plain or [used.most_common(1)[0][0]]


def flatten(scene, resolver=None):
    """Rewrites a parsed, resolved source world in place into the Sandbox field; returns its start points."""
    world = scene['world']; terrain = world.metadata['terrain']; gx, gy = terrain['grid']
    rows = list(struct.iter_unpack('<f4I', world.vertices))
    # One tint for the whole field: the median of each byte of the source vertex colours on level ground (shaded slopes
    # would darken it). A vertex is level when its neighbours on either axis differ by under 0.2 m.
    h = [row[0] for row in rows]
    level = [rows[i][1] for i in (y * gx + x for y in range(1, gy - 1) for x in range(1, gx - 1))
             if abs(h[i + 1] - h[i - 1]) < 0.2 and abs(h[i + gx] - h[i - gx]) < 0.2] or [row[1] for row in rows]
    tint = sum(statistics.median_low((color >> shift) & 255 for color in level) << shift for shift in (24, 16, 8, 0))
    world.vertices = b''.join(struct.pack('<f4I', GROUND, tint, *row[2:]) for row in rows)
    world.heights = [GROUND] * len(rows)
    terrain['height_min'] = terrain['height_max'] = GROUND
    layer = scene['terrain']['layers'][0]
    variants, pick = plain_ground(layer['texture_ids'], resolver), random.Random(STEM)
    layer['texture_ids'] = [pick.choice(variants) for _ in layer['texture_ids']]
    objects, kept = scene['placements']['objects'], []
    for kind, (dx, dy) in RESOURCE_OFFSETS.items():
        sources = sorted((o for o in objects if skirmish_map.RESOURCE_OBJECTS.get(o.get('database_name')) == kind), key=lambda o: o['object_id'])
        if len(sources) < len(STARTS):
            raise ValueError('the source map has too few %s sources' % kind)
        for (x, y), side, obj in zip(STARTS, (1, -1), sources):
            obj['position'] = [x + side * dx, y + side * dy, GROUND] + list(obj['position'][3:])
            kept.append(obj)
    scene['placements']['objects'] = sorted(kept, key=lambda o: o['object_id'])
    scene['placements']['other_placeables'] = []
    scene['water']['lakes'] = []
    scene['tail']['decals'] = []
    for category in scene['pathing']['categories']:
        if category['name'] in OPEN_CATEGORIES and 'painted' in category:
            category['painted'] = all_set(category['painted']['count'])
    return [{'kind': skirmish_map.MARKER_START, 'name': 'Start Point %d' % team, 'position': [x, y, GROUND]}
            for team, (x, y) in enumerate(STARTS)]
