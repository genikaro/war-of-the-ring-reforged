"""Mill/Foundry rules and precomputed resource-site collision layers.

python tools/skirmish_construction.py --cache converted/skirmish
"""
import argparse
import hashlib
import json
from pathlib import Path

import skirmish_map
import wor

BUILDINGS = {'food': 'BUILDING_G_FOOD_STRUCTURE', 'ore': 'BUILDING_G_BLACKSMITH'}
OBSTACLE_BASE = 20000


def construction_data(resolver, resources):
    variables = resolver.db['WorldVariables'].records[0]
    definitions = {}
    for kind, name in BUILDINGS.items():
        record = resolver.db['Buildings'].get(name)
        definitions[kind] = {'name': name, 'display_name': record['Name'], 'food_cost': record['CostFood'],
            'ore_cost': record['CostOre'], 'duration_ticks': (record['BuildTime']+49)//50,
            'max_health': record['MaxHealth'], 'food_refund_ppm': round(record['RefundFoodPct']*1_000_000),
            'ore_refund_ppm': round(record['RefundOrePct']*1_000_000),
            'extra_worker_ppm': round(variables['AddlWorkerBuildFactor']*1_000_000),
            'initial_health_ppm': round(variables['BldgInitialHealthPct']*1_000_000),
            'prerequisites': [resolver.db['Buildings'].get(record['BuildingRequired%d' % i]).name
                              for i in range(1, 4) if record['BuildingRequired%d' % i] >= 0]}
    sites, footprints = [], []
    for node in resources:
        obstacle = OBSTACLE_BASE + node['id']
        footprint, _ = skirmish_map.building_footprint(resolver, BUILDINGS[node['kind']], node['position'], obstacle)
        footprint['name'] = 'Resource construction %d' % node['id']
        footprints.append(footprint)
        sites.append({'resource': node['id'], 'kind': node['kind'], 'obstacle': obstacle})
    buildings = {}
    for name in [skirmish_map.CITADEL, *BUILDINGS.values()]:
        record = resolver.db['Buildings'].get(name)
        portrait = resolver.db['UVAs'].get(record['Portrait'])
        buildings[name] = {'display_name': 'Citadel 1' if name == skirmish_map.CITADEL else record['Name'],
            'max_health': record['MaxHealth'], 'armor': record['ArmorPoints'],
            'portrait': Path(portrait['Filename'].replace('\\', '/')).stem,
            'selection_radius_mm': round(record['SelectionRingRadius']*1000)}
    return {'definitions': definitions, 'sites': sites, 'buildings': buildings}, footprints


def refresh(output):
    import prepare_prototype
    import prototype_clearance
    import prototype_interface
    import prototype_assets
    import world_scene
    output = output.resolve()
    if any(output.is_relative_to(wor.ROOT / p) for p in ['extracted', 'TLOTR - War of the Ring']):
        raise ValueError('construction cache must be outside original and extracted data')
    resolver = prepare_prototype.open_resolver(output)
    path = output / 'manifest.json'
    manifest = json.loads(path.read_text())
    if manifest.get('mode') != 'skirmish':
        raise ValueError('construction refresh requires a skirmish cache')
    data = manifest['skirmish']
    building_paths = [output / data['building_files'][name] for name in BUILDINGS.values()]
    for building_path in building_paths:
        prototype_assets.restore_building(building_path)
    construction, footprints = construction_data(resolver, data['resources'])
    bases = [skirmish_map.building_footprint(resolver, skirmish_map.CITADEL, base['position'], base['footprint_object'])[0]
             for base in data['bases']]
    for base, footprint in zip(data['bases'], bases):
        footprint['name'] = 'Start Citadel %d' % base['team']
    scene = world_scene.parse((wor.ROOT / 'extracted/Levels' / skirmish_map.MAP_FILE).read_bytes())
    world_scene.resolve_objects(scene, resolver.db)
    prepare_prototype.build_navigation(scene, resolver, output, bases+footprints, [s['obstacle'] for s in construction['sites']], precise=True)
    prototype_clearance.refresh(output)
    prototype_interface.export(output, resolver)
    import skirmish_economy
    data['economy'] = skirmish_economy.economy_data(resolver)
    data['construction'] = construction
    data['selectable_objects'] = skirmish_map.selectable_objects(scene, resolver)
    for file in [output/'navigation.json', output/'heights.bin', *building_paths, *sorted((output/'interface').glob('*'))]:
        manifest['files'][file.relative_to(output).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    path.write_text(json.dumps(manifest, indent=2)+'\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    refresh(parser.parse_args().cache)
