"""Import the bounded raw-resource economy; refresh an existing cache without re-exporting models.

python tools/skirmish_economy.py --cache converted/skirmish
"""
import argparse
import json
from pathlib import Path

from resolve_model import Resolver


def economy_data(resolver):
    worker = resolver.db['Units'].get('UNIT_G_HUMAN_WORKER')
    variables = resolver.db['WorldVariables'].records[0]
    sources = {}
    for name in ('OBJECT_RESOURCE_ORE_PILE', 'OBJECT_RESOURCE_FOOD_WELL'):
        record = resolver.db['Objects'].get(name)
        sources['ore' if name.endswith('ORE_PILE') else 'food'] = {
            'rate_milli_per_tick': round(record['ResourceGatherRateRaw'] * 1000 / 20),
            'raw_fraction_ppm': round(record['MaxGatherPercentageRaw'] * 1_000_000)}
    return {'format_version': 1, 'ticks_per_second': 20,
            'workers': {'UNIT_G_HUMAN_WORKER': {'food_capacity': worker['MaxCarriedFood'], 'ore_capacity': worker['MaxCarriedOre']}},
            'food_capacity': variables['DefaultInitialFoodCapacity'], 'ore_capacity': variables['DefaultInitialOreCapacity'],
            'sources': sources,
            'adapter': 'Raw rate interpreted as units/second; max raw percentage as recoverable fraction of initial stock. '
                       'Interaction radii use footprint corner distance plus worker radius and one navigation cell. '
                       'Food wells remain unavailable until Mill construction is implemented.'}


def refresh(cache):
    path = cache / 'manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    if manifest.get('mode') != 'skirmish':
        raise ValueError('economy refresh requires a skirmish cache')
    manifest['skirmish']['economy'] = economy_data(Resolver())
    path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    refresh(parser.parse_args().cache)
