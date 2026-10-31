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


def added_population(units, name):
    """Population a living unit adds (the Evil Slavemaster's 12); 0 for a record that has no such column."""
    try:
        return units.get(name)['AdditionalPopAdded']
    except KeyError:
        return 0


def production_data(resolver, roster, factions=('good', 'evil')):
    """Source UnitProduced order per skirmish producer, with each unit's source BuildingRequired list, its Fate cost
    (source PowerCost) and its hero group. Units needing a building beyond the skirmish set and units outside the
    exported roster are listed as unavailable with the reason."""
    units, buildings = resolver.db['Units'], resolver.db['Buildings']
    variables = resolver.db['WorldVariables'].records[0]
    producers, rules = {}, {}
    in_scope = [name for name in skirmish_map.SKIRMISH_BUILDINGS if skirmish_map.faction_of(name) in factions]
    for name in in_scope:
        record = buildings.get(name)
        if not record['IsUnitProducer']:
            continue
        producers[name] = {'units': [], 'unavailable': {}}
        for unit in [units.get(record['UnitProduced%d' % i]).name for i in range(1, 7) if record['UnitProduced%d' % i] >= 0]:
            source = units.get(unit)
            required = [buildings.get(source['BuildingRequired%d' % i]).name for i in range(1, 5) if source['BuildingRequired%d' % i] >= 0]
            missing = [building for building in required if building not in skirmish_map.SKIRMISH_BUILDINGS]
            reason = 'requires ' + ', '.join(missing) if missing else None if unit in roster else 'model and rules not exported'
            if reason:
                producers[name]['unavailable'][unit] = reason
                continue
            producers[name]['units'].append(unit)
            rules[unit] = {'display_name': source['Name'], 'food_cost': source['FoodCost'], 'ore_cost': source['OreCost'],
                           'fate_cost': source['PowerCost'],
                           'population': source['PopulationCost'], 'duration_ticks': (source['UnitTrainingTime']+49)//50,
                           'unique': bool(source['IsUnique']), 'unique_group': source['UniqueGroup'], 'prerequisites': required}
    # Citadel levels 2-5 hold 1, 2, 3 and 3 heroes of the source unique group 0 (Gimli, Aragorn, Legolas).
    capacity = {name: {'group': record['UniqueGroupBuildingAffects'], 'amount': record['AmountBuildingAffectsUniqueGroup']}
                for name in in_scope
                for record in [buildings.get(name)] if record['UniqueGroupBuildingAffects'] >= 0}
    return {'format_version': 1, 'ticks_per_second': 20, 'producers': producers, 'units': rules,
            'population_provided': {name: buildings.get(name)['AdditionalPopAdded'] for name in in_scope},
            # Build-133: the Goblin Slavemaster adds 12 population while it lives (source Units.AdditionalPopAdded), 15 at most.
            'population_by_unit': {name: {'added': units.get(name)['AdditionalPopAdded'], 'max': units.get(name)['MaxPopulation']}
                                   for name in roster if added_population(units, name) and skirmish_map.faction_of(name) in factions},
            'unique_group_capacity': capacity, 'max_fate': variables['MaxPowerPoints'], 'fate': fate_data(resolver, factions),
            'initial_population_capacity': variables['DefaultInitialPopulationCapacity'],
            'max_population': variables['MaxPopulationCap'], 'rally_banner': RALLY_BANNER_FILE,
            'adapter': 'UnitTrainingTime milliseconds round up to 20 Hz ticks. Cost, Fate included, is paid and population reserved '
                       'when a unit is queued. Cancellation refunds the full cost; the queue holds five units; units appear on the '
                       'nearest free cell to the building center. These three rules are not traced to source data. A unique unit '
                       '(source IsUnique) cannot be queued while one is alive or queued; the source HeroDeathRefreshTime is not applied. '
                       'Heroes of a unique group (Gimli, Aragorn, Legolas) are capped by the best standing building of the team that '
                       'holds that group (source AmountBuildingAffectsUniqueGroup: Citadel 2-5 hold 1, 2, 3, 3); whether several '
                       'Citadels add up is unverified. A team starts with no Fate and earns it in battle (see fate); the Sandbox map keeps it full.'}


def ppm(value):
    return round(value * 1_000_000)


def fate_data(resolver, factions=('good', 'evil')):
    """How battle earns Fate, as Rings.exe (SHA-256 7d9e30a7...) applies the source WorldVariables: damage a player's unit
    or building deals to an enemy (at most the victim's health) times PowerByDamageRate and the weapon's FateModifier is
    divided by (Fate held + Fate paid into training queues + 1) x PowerReductionScalar, then scaled by the attacker's
    distance from the centre of the player's town centres (1 there, MaxPowerDistanceMultiplier at the farthest map corner;
    that maximum with no town centre), by the Power Acquisition setting and by places of power (FATE_GATHER_RATE).
    PowerLevelPerPoint of that power makes a Fate point; MaxPowerPoints caps it. Training spends whole points and keeps
    the progress. Evil Warposts also earn power by corrupted area (WarpostPowerAcquisitionRate); Good has no Warposts."""
    variables = resolver.db['WorldVariables'].records[0]
    buildings = resolver.db['Buildings']
    return {'max_points': variables['MaxPowerPoints'], 'level_per_point': variables['PowerLevelPerPoint'],
            'by_damage_ppm': ppm(variables['PowerByDamageRate']), 'reduction_ppm': ppm(variables['PowerReductionScalar']),
            'max_distance_ppm': ppm(variables['MaxPowerDistanceMultiplier']),
            # Build-133: Evil War Posts earn power by the ground they corrupt, per square metre per second.
            'warpost_ppm': ppm(variables['WarpostPowerAcquisitionRate']),
            'acquisition_ppm': {'low': ppm(variables['PowerAcquisitionRateLow']), 'medium': ppm(variables['PowerAcquisitionRateMedium']),
                                'high': ppm(variables['PowerAcquisitionRateHigh'])},
            'town_centers': [name for name in skirmish_map.SKIRMISH_BUILDINGS if buildings.get(name)['IsTownCenter'] and skirmish_map.faction_of(name) in factions],
            'adapter': 'The Power Acquisition setting is Medium (no lobby choice yet); every Good unit weapon has FateModifier 1, so '
                       'only building defenses carry their own (fate_ppm); fixed-point millionths replace the source floats, and '
                       'the bots\' difficulty bonus to Fate (x0.85 to x1.5 in Rings.exe) is not applied.'}


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
    import content_pack
    content_pack.reject_slim_cache(manifest)
    manifest['skirmish']['production'] = export(output, resolver, [m['unit'] for m in manifest['models']])
    manifest['files'][RALLY_BANNER_FILE] = hashlib.sha256((output / RALLY_BANNER_FILE).read_bytes()).hexdigest()
    path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    refresh(parser.parse_args().cache)
