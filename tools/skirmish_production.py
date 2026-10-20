"""Unit training rules, population capacity and the rally banner; refresh an existing skirmish cache.

python tools/skirmish_production.py --cache converted/skirmish
"""
import argparse
import hashlib
import json
from pathlib import Path

import skirmish_map

RALLY_BANNER = 'MODEL_G_GATHER_POINT'
RALLY_BANNER_FILE = 'objects/' + RALLY_BANNER + '.glb'


def production_data(resolver, roster):
    """Source UnitProduced order per Tier 1 producer. Units beyond Tier 1, fate-point calls and units outside the
    exported roster are listed as unavailable with the reason."""
    units, buildings = resolver.db['Units'], resolver.db['Buildings']
    variables = resolver.db['WorldVariables'].records[0]
    producers, rules = {}, {}
    for name in skirmish_map.TIER1_BUILDINGS:
        record = buildings.get(name)
        if not record['IsUnitProducer']:
            continue
        producers[name] = {'units': [], 'unavailable': {}}
        for unit in [units.get(record['UnitProduced%d' % i]).name for i in range(1, 7) if record['UnitProduced%d' % i] >= 0]:
            source = units.get(unit)
            missing = [buildings.get(source['BuildingRequired%d' % i]).name for i in range(1, 5)
                       if source['BuildingRequired%d' % i] >= 0 and buildings.get(source['BuildingRequired%d' % i]).name not in skirmish_map.TIER1_BUILDINGS]
            reason = ('requires ' + ', '.join(missing) if missing else 'requires fate points' if source['PowerCost']
                      else None if unit in roster else 'model and rules not exported')
            if reason:
                producers[name]['unavailable'][unit] = reason
                continue
            producers[name]['units'].append(unit)
            rules[unit] = {'display_name': source['Name'], 'food_cost': source['FoodCost'], 'ore_cost': source['OreCost'],
                           'population': source['PopulationCost'], 'duration_ticks': (source['UnitTrainingTime']+49)//50}
    return {'format_version': 1, 'ticks_per_second': 20, 'producers': producers, 'units': rules,
            'population_provided': {name: buildings.get(name)['AdditionalPopAdded'] for name in skirmish_map.TIER1_BUILDINGS},
            'initial_population_capacity': variables['DefaultInitialPopulationCapacity'],
            'max_population': variables['MaxPopulationCap'], 'rally_banner': RALLY_BANNER_FILE,
            'adapter': 'UnitTrainingTime milliseconds round up to 20 Hz ticks. Cost is paid and population reserved when a unit is queued. '
                       'Cancellation refunds the full cost; the queue holds five units; units appear on the nearest free cell to the '
                       'building center. These three rules are not traced to source data.'}


def export(output, resolver, roster):
    from lms2gltf import convert_model
    target = output / RALLY_BANNER_FILE
    target.parent.mkdir(exist_ok=True)
    convert_model(resolver, RALLY_BANNER, str(target))
    return production_data(resolver, roster)


def refresh(output):
    import prepare_prototype
    output = output.resolve()
    resolver = prepare_prototype.open_resolver(output)
    path = output / 'manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    if manifest.get('mode') != 'skirmish':
        raise ValueError('production refresh requires a skirmish cache')
    manifest['skirmish']['production'] = export(output, resolver, [m['unit'] for m in manifest['models']])
    manifest['files'][RALLY_BANNER_FILE] = hashlib.sha256((output / RALLY_BANNER_FILE).read_bytes()).hexdigest()
    path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    refresh(parser.parse_args().cache)
