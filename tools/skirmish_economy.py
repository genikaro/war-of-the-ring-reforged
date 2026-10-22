"""Import the gathering economy; refresh an existing cache without re-exporting models.

python tools/skirmish_economy.py --cache converted/skirmish
"""
import argparse
import json
from decimal import Decimal, ROUND_CEILING
from pathlib import Path

import lsk
from resolve_model import Resolver
from wor import ROOT

# Owner policies (build-17): a worker spends two seconds inside a Mill or Foundry, Warcraft III gold-mine style,
# and carries five ore from a raw ore pile, one per pickaxe hit.
INSIDE_TICKS = 40
RAW_ORE_LOAD = 5
WORKER_STATES = {
    'mine': 'UNIT_ANIM_STATE_RESOURCE_ORE_GATHER',
    'ore_pick_up': 'UNIT_ANIM_STATE_RESOURCE_ORE_PICK_UP',
    'ore_walk': 'UNIT_ANIM_STATE_RESOURCE_ORE_WALK',
    'ore_idle': 'UNIT_ANIM_STATE_RESOURCE_ORE_IDLE',
    'ore_put_down': 'UNIT_ANIM_STATE_RESOURCE_ORE_PUT_DOWN',
    'food_walk': 'UNIT_ANIM_STATE_RESOURCE_FOOD_WALK',
    'food_idle': 'UNIT_ANIM_STATE_RESOURCE_FOOD_IDLE',
    'food_put_down': 'UNIT_ANIM_STATE_RESOURCE_FOOD_PUT_DOWN',
    # Hammering, shown while a worker builds a foundation.
    'build': 'UNIT_ANIM_STATE_REPAIR'}


def ticks(seconds, fraction=1):
    return int((Decimal(str(seconds))*Decimal(str(fraction))*20).to_integral_value(rounding=ROUND_CEILING))


def gather_animations(model, durations):
    """Source resource and repair states of one worker model; durations in 20 Hz ticks, rounded up like attack clips."""
    states = {s['name']: s for s in model['animation_states']}
    result = {}
    for key, name in WORKER_STATES.items():
        choice = next(c for c in states[name]['choices'] if c['weight'] > 0)
        result[key] = {'state': name, 'clip': choice['clip_name'], 'duration_ticks': ticks(durations[choice['clip_name']])}
    # The pickaxe impact is the clip's supply-gather sound event, which coincides with the ore-mining effect key.
    mine = next(c for c in states[WORKER_STATES['mine']]['choices'] if c['weight'] > 0)
    hit = next(model['events'][i] for i in mine['event_indices'] if model['events'][i].get('name') == 'sound')
    result['mine']['hit_tick'] = min(result['mine']['duration_ticks']-1, ticks(durations[mine['clip_name']], hit['time']))
    result['mine']['hit_source_fraction'] = hit['time']
    if result['ore_put_down']['duration_ticks'] != result['food_put_down']['duration_ticks']:
        raise ValueError('food and ore put-down clips differ in length')
    return result


def worker_animations(resolver, name):
    model = resolver.resolve(name, recurse=False)
    skeleton = lsk.parse((ROOT/model['skeletons'][0]['file']).read_bytes())
    return gather_animations(model, {clip.name: clip.duration for clip in skeleton.clips})


def economy_data(resolver):
    worker = resolver.db['Units'].get('UNIT_G_HUMAN_WORKER')
    variables = resolver.db['WorldVariables'].records[0]
    sources = {}
    for name in ('OBJECT_RESOURCE_ORE_PILE', 'OBJECT_RESOURCE_FOOD_WELL'):
        record = resolver.db['Objects'].get(name)
        sources['ore' if name.endswith('ORE_PILE') else 'food'] = {
            'rate_milli_per_tick': round(record['ResourceGatherRateRaw'] * 1000 / 20),
            'raw_fraction_ppm': round(record['MaxGatherPercentageRaw'] * 1_000_000),
            'improved_rate_milli_per_tick': round(record['ResourceGatherRateWithBuilding'] * 1000 / 20),
            'improved_fraction_ppm': round(record['MaxGatherPercentageWithBuilding'] * 1_000_000),
            # No open-ground building within this distance of the resource (build-42).
            'building_clearance_mm': round(record['MinBuildingPlacementRadius'] * 1000)}
    return {'format_version': 2, 'ticks_per_second': 20,
            'workers': {'UNIT_G_HUMAN_WORKER': {'food_capacity': worker['MaxCarriedFood'], 'ore_capacity': worker['MaxCarriedOre'],
                                                'raw_ore_load': RAW_ORE_LOAD, 'inside_ticks': INSIDE_TICKS,
                                                'animations': worker_animations(resolver, 'UNIT_G_HUMAN_WORKER')}},
            'food_capacity': variables['DefaultInitialFoodCapacity'], 'ore_capacity': variables['DefaultInitialOreCapacity'],
            'sources': sources,
            'adapter': 'Owner-defined trips: a raw ore pile yields one ore per pickaxe hit (source gather clip and its '
                       'supply-gather event), five per trip, then the source pick-up clip. A completed owned Mill or Foundry '
                       'admits one worker at a time for two seconds, who leaves with a full load (MaxCarriedFood / '
                       'MaxCarriedOre). Loads are credited after the source put-down clip at an owned depot. Source gather '
                       'rates only gate availability; the max raw percentage is the recoverable fraction of initial stock. '
                       'Interaction radii use footprint corner distance plus worker radius and one navigation cell. '
                       'Food wells require a Mill.'}


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
