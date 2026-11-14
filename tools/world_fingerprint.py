"""What world_scene.py makes of every level of an install (build-208), for tests/formats --python-worlds to check the C#
World reader (prototype/Formats/World.cs) against.

python tools/world_fingerprint.py "C:/Games/War of the Ring" worlds.json
python tools/world_fingerprint.py "C:/Games/War of the Ring" --dump Multiplayer/MP02_Sorrow.wor

Every .wor in Levels.H2O is parsed with world_scene.parse and its objects and effects resolved against the install's
database (world_scene.resolve_objects). The record is the part the game reads: the header and metadata, the terrain
vertices (by SHA-256), the terrain layers, coordinates and environment, placed objects and effects with what they
resolve to, the pathing categories with their bitsets and region graphs, the lakes with their surface vertices, and the
tail's time of day, Sun/Moon keys, grass, markers, decals and final record (lights and fog counted). It is written in
models_fingerprint.py's canonical form and hashed. A level that does not parse records the error. The install is
only read.
"""
import argparse
import hashlib
import json
from pathlib import Path

import h2o
import lqr
import models_fingerprint
import world_scene


def bitset(value):
    return None if value is None else {'count': value['count'], 'set_count': value['set_count'], 'words': value['words']}


def view(scene):
    world, meta = scene['world'], scene['world'].metadata
    terrain = scene['terrain']
    t = meta['terrain']
    metadata = {
        'name': meta['name'], 'description': meta['description'], 'width': meta['width'], 'height': meta['height'],
        'sha256': meta['sha256'], 'header': [*meta['header']['unknown'], meta['header']['flags'],
                                              meta['header']['player_count_field'], meta['header']['preview_flag']],
        'pre_terrain_records': [meta['pre_terrain_records']['present'], meta['pre_terrain_records']['count']],
        'terrain': [t['cell_size'], t['grid'], t['terrain_set'], t['terrain_set_id'], t['raw_parameters'], t['packed_value'],
                    t['compressed_bytes'], t['raw_bytes'], t['crc32'], t['height_min'], t['height_max']],
        'name_table': [meta['name_table']['offset'], meta['name_table']['byte_length'], meta['name_table']['names']],
        'sections': [[s['name'], s['offset'], s['byte_length']] for s in meta['sections'][:5]] + [[s['name'], s['offset'], s['byte_length']] for s in scene['sections']],
    }
    return {
        'metadata': metadata,
        'vertices': hashlib.sha256(world.vertices).hexdigest(),
        'terrain': {
            'layers': [[l['version'], l['tile_cells'], l['grid'], l['flag'], l['enabled'], l['texture_ids']] for l in terrain['layers']],
            'layer_parameter': terrain['layer_parameter'], 'palette_ids': terrain['palette_ids'],
            'coordinate_sets': [[c['version'], c['tile_cells'], c['channels'], c['usage'], c['values'], c.get('vertex_values')] for c in terrain['coordinate_sets']],
            'compositions': [terrain['compositions'][k] for k in ('version', 'tile_cells', 'grid', 'values')],
            'tile_colors': [terrain['tile_colors'][k] for k in ('version', 'tile_cells', 'grid', 'values')],
            'parameters': terrain['parameters'],
            'materials': [[m['bindings'], m['material']['version'], m['material']['raw_words']] for m in terrain['materials']],
            'texture_lookup': terrain['texture_lookup'],
            'environment': [terrain['environment'][k] for k in ('raw_words_a', 'raw_vector_a', 'raw_word_b', 'raw_vectors_b', 'byte_a', 'raw_words_c', 'flag_b')],
        },
        'placements': {
            'players': scene['placements']['players'],
            'objects': [[o['offset'], o['name'], o['name_index'], o['kind'], o['reference'], o['model_variant'], o['position'], o['scale'],
                         o['angles_degrees'], o['raw_words'], o['flags'], o['object_id'], o['pairs'], o['database_table'], o['database_name'],
                         o['model'], o.get('model_event'), o['is_walkable'], o['mesh_path_lookup']] for o in scene['placements']['objects']],
            'other_placeables': [[e['references'], e['name'], e['position'], e['scale'], e['raw_words'], e['flag'], e['effect']]
                                 for e in scene['placements']['other_placeables']],
            'lines': scene['placements']['lines'], 'line_points': scene['placements']['line_points'],
        },
        'pathing': {
            'categories': [[c['enabled'], c['id'], c['name'], c['max_water_depth'], c['max_slope'], c.get('grid'), bitset(c.get('painted')),
                            bitset(c.get('mask_b')), bitset(c.get('mask_c')), c.get('graph_id'), graph(c.get('graph'))] for c in scene['pathing']['categories']],
            'region_lookup': scene['pathing']['region_lookup'],
        },
        'water': {
            'lakes': [[l['id'], l['name'], l['height'], l['raw_words_a'], l['enabled'], l['raw_words_b'], surface(l['surface'])] for l in scene['water']['lakes']],
            'selected': scene['water']['selected'],
        },
        'tail': {k: scene['tail'][k] for k in ('time_of_day', 'sun_keyframes', 'grass', 'markers', 'final')}
                | {'decals': [[d['object_id'], d['decal_id'], d['position'], d['scale'], d['angles_degrees'], d['color_argb'], d['alpha'],
                               d['priority'], d['flags']] for d in scene['tail']['decals']],
                   'lights': len(scene['tail']['lights']), 'fog': len(scene['tail']['fog'])},
    }


def graph(value):
    if value is None:
        return None
    return [value['version'], value['grid'],
            [[r['header'], r['vector'], r['raw_words'], [[k['header'], k['indices']] for k in r['links']], r['link_properties']] for r in value['regions']],
            [[p['header'], p['indices']] for p in value['portals']], [bitset(m) for m in value['matrices']], value['table_word'], value['tables'],
            world_scene.region_cells(value)]


def surface(value):
    if value is None:
        return None
    return [value['cell_size'], value['multiplier'], value['width'], value['height'], value['grid'], value['offset'], value['vertices'],
            len(value['layers']), value['material']['version'], value['material']['raw_words']]


def levels(install):
    archive = h2o.H2OArchive(str(Path(install) / 'Archives' / 'Levels.H2O'))
    for entry in sorted(archive.entries, key=lambda e: e.path.lower()):
        if entry.path.lower().endswith('.wor'):
            yield entry.path.replace('\\', '/'), archive.read(entry, verify=True)


def record(data, db):
    scene = world_scene.parse(data)
    world_scene.resolve_objects(scene, db)
    return view(scene)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out', nargs='?')
    parser.add_argument('--dump', help='print the canonical form of one level (its path in Levels.H2O)')
    args = parser.parse_args()
    db = lqr.Database(str(Path(args.install) / 'Database'))
    if args.dump:
        data = next(d for p, d in levels(args.install) if p.lower() == args.dump.replace('\\', '/').lower())
        print(''.join(models_fingerprint.canonical(record(data, db), [])))
        return
    worlds = {}
    for path, data in levels(args.install):
        try:
            worlds[path] = hashlib.sha256(''.join(models_fingerprint.canonical(record(data, db), [])).encode('ascii')).hexdigest()
        except Exception as failure:  # noqa: BLE001 - the level's answer is that it does not parse
            worlds[path] = 'error: %s: %s' % (type(failure).__name__, failure)
    Path(args.out).write_text(json.dumps({'format_version': 1, 'worlds': worlds}, indent=1) + '\n', encoding='utf-8')
    print('%d levels, %d that do not parse' % (len(worlds), sum(v.startswith('error') for v in worlds.values())))


if __name__ == '__main__':
    main()
