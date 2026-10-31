"""Tier 1 and 2 construction rules, building upgrades, resource-site collision layers and open-ground placement footprints.

python tools/skirmish_construction.py --cache converted/skirmish
"""
import argparse
import hashlib
import json
from pathlib import Path

import lsk
import skirmish_map
import wor

BUILDINGS = {'food': 'BUILDING_G_FOOD_STRUCTURE', 'ore': 'BUILDING_G_BLACKSMITH'}
FACTION_BUILDINGS = {key: {'food': faction['food'], 'ore': faction['ore']} for key, faction in skirmish_map.FACTIONS.items()}
OBSTACLE_BASE = 20000
# Good worker construction panel order observed in local-009 (original 1.01.0011).
CONSTRUCTION_ORDER = [skirmish_map.CITADEL, BUILDINGS['food'], BUILDINGS['ore'], 'BUILDING_G_TOWER',
                      'BUILDING_G_WORKER_STRUCTURE', 'BUILDING_G_GONDORIAN_BARRACKS', 'BUILDING_G_DWARF_BARRACKS',
                      # Tiers 2 and 3: the second row of the full worker palette (good-018, 2026-10-23).
                      'BUILDING_G_SKILLS_STRUCTURE', 'BUILDING_G_RANGER_BARRACKS', 'BUILDING_G_ARMORY',
                      'BUILDING_G_STABLES', 'BUILDING_G_ELF_BARRACKS', 'BUILDING_G_HOSPITAL']
# The Evil worker palette (build-133): the Hovel first like the Citadel, then the resource buildings, the Fortress and
# the production, defense and research buildings in tier order. No capture of the original's Evil panel exists.
EVIL_CONSTRUCTION_ORDER = [skirmish_map.EVIL_TOWN_CENTER, FACTION_BUILDINGS['evil']['food'], FACTION_BUILDINGS['evil']['ore'],
                           skirmish_map.EVIL_FORTRESS, 'BUILDING_E_ORC_BARRACKS', 'BUILDING_E_STABLES', 'BUILDING_E_TOWER',
                           'BUILDING_E_ARMORY', 'BUILDING_E_HARADRIM_BARRACKS', 'BUILDING_E_BEAST_UPGRADES_STRUCTURE',
                           'BUILDING_E_TROLL_BARRACKS', 'BUILDING_E_HOSPITAL']
# Only a Slavemaster raises a War Post, which corrupts the ground around it (source: Slavemaster description, manual p.13).
SLAVEMASTER = 'UNIT_E_GOBLIN_SLAVE_MASTER'
PALETTES = {'UNIT_G_HUMAN_WORKER': CONSTRUCTION_ORDER, 'UNIT_E_GOBLIN_SLAVE': EVIL_CONSTRUCTION_ORDER,
            SLAVEMASTER: [skirmish_map.EVIL_WARPOST]}
RESOURCE_BUILDINGS = {name for faction in FACTION_BUILDINGS.values() for name in faction.values()}
# Corruption (build-133): the source stores no radius (Buildings.CreatesCorruption is a flag, WarpostPowerAcquisitionRate is
# per square metre). Adapter: a War Post corrupts the Good Citadel's 25 m building radius, and so does the start Hovel.
CORRUPTION_RADIUS_MM = 25000


def display_name(name, record):
    """The source names the Citadel levels "Stronghold Level N"; the game's tree and panels say Citadel N. Evil panels say
    Fortress N, from the source's "Fortress of Mordor Level N"."""
    if name.startswith('BUILDING_G_TOWN_CENTER_LEVEL_'):
        return 'Citadel ' + name[-1]
    return 'Fortress ' + name[-1] if name.startswith('BUILDING_E_TOWN_CENTER_LEVEL_') else record['Name']


def building_rules(resolver, name):
    variables = resolver.db['WorldVariables'].records[0]
    record = resolver.db['Buildings'].get(name)
    return {'name': name, 'display_name': display_name(name, record), 'food_cost': record['CostFood'],
            'ore_cost': record['CostOre'], 'duration_ticks': max(1, (record['BuildTime']+49)//50),
            'max_health': record['MaxHealth'], 'food_refund_ppm': round(record['RefundFoodPct']*1_000_000),
            'ore_refund_ppm': round(record['RefundOrePct']*1_000_000),
            'extra_worker_ppm': round(variables['AddlWorkerBuildFactor']*1_000_000),
            'initial_health_ppm': round(variables['BldgInitialHealthPct']*1_000_000),
            'prerequisites': [resolver.db['Buildings'].get(record['BuildingRequired%d' % i]).name
                              for i in range(1, 4) if record['BuildingRequired%d' % i] >= 0]}


def upgrade_data(resolver, factions=('good', 'evil')):
    """Buildings that turn into another in place (source PromotesTo: Citadel 1 to 2, Camp to War Camp), keyed by the
    building upgraded: the promoted building's rules, with its source UpgradeTime as the duration."""
    upgrades = {}
    for name in skirmish_map.SKIRMISH_BUILDINGS:
        record = resolver.db['Buildings'].get(name)
        if record['PromotesTo'] < 0 or skirmish_map.faction_of(name) not in factions:
            continue
        promoted = resolver.db['Buildings'].get(record['PromotesTo'])
        if promoted.name in skirmish_map.SKIRMISH_BUILDINGS:
            upgrades[name] = {**building_rules(resolver, promoted.name), 'duration_ticks': (promoted['UpgradeTime']+49)//50}
    return upgrades


def placement_data(resolver, factions=('good', 'evil')):
    """Open-ground buildings of the given factions: source rules plus the oriented-collision footprint relative to the
    origin, the worker palettes, and the buildings that stand only on corrupted ground (Evil)."""
    order = [name for name in CONSTRUCTION_ORDER + EVIL_CONSTRUCTION_ORDER + [skirmish_map.EVIL_WARPOST]
             if skirmish_map.faction_of(name) in factions]
    placeable = {}
    for name in order:
        if name in RESOURCE_BUILDINGS:
            continue
        record = resolver.db['Buildings'].get(name)
        footprint, _ = skirmish_map.building_footprint(resolver, name, (0, 0, 0), 0)
        placeable[name] = {**building_rules(resolver, name),
            'footprint': {'polygon_mm': [[round(x*1000), round(z*1000)] for x, z in footprint['polygon']],
                          'min_height_mm': round(footprint['y_min']*1000), 'max_height_mm': round(footprint['y_max']*1000)},
            'must_be_near': bool(record['MustBePlacedNearOtherBuildings']), 'drop_off': bool(record['IsResourceDropOffBuilding'])}
        if record['CanOnlyBePlacedOnCorruption']:
            placeable[name]['corrupted_only'] = True
        if record['CreatesCorruption']:
            placeable[name]['creates_corruption'] = True
    in_scope = [name for name in skirmish_map.SKIRMISH_BUILDINGS if skirmish_map.faction_of(name) in factions]
    corruption = {'creators': {name: CORRUPTION_RADIUS_MM for name in in_scope if resolver.db['Buildings'].get(name)['CreatesCorruption']},
                  'start_creators': {skirmish_map.EVIL_TOWN_CENTER: CORRUPTION_RADIUS_MM},
                  'only': [name for name in in_scope if resolver.db['Buildings'].get(name)['CanOnlyBePlacedOnCorruption']],
                  'adapter': 'The source flags the buildings that create corruption (War Post) and those that need it, and stores no '
                             'radius; the radius is the Good Citadel building radius (25 m). The start Goblin Hovel (start_creators, only while it is the '
                             'building the map started with) corrupts the same radius, since nothing else could be built at the start. A '
                             'building needs its origin cell on corrupted ground.'}
    palettes = {unit: [name for name in names if skirmish_map.faction_of(name) in factions] for unit, names in PALETTES.items()
                if skirmish_map.faction_of(unit) in factions}
    return {'order': order, 'palettes': palettes, 'corruption': corruption if 'evil' in factions else {},
            'placeable': placeable, 'upgrades': upgrade_data(resolver, factions),
            'placement_radius_mm': {name: max(0, round(resolver.db['Buildings'].get(name)['MaxRadiusAllowingNewBuildings']*1000))
                                    for name in in_scope},
            'placement_adapter': 'Footprints use source building passability (PATHING_CATEGORY_BUILDING) and current walker '
                                 'clearance under the oriented-collision box, no rotation. MustBePlacedNearOtherBuildings uses '
                                 'center distance to completed buildings within their MaxRadiusAllowingNewBuildings. '
                                 'An upgraded building keeps the footprint it was placed with (Citadel levels share one box; '
                                 'the War Camp box is 1 cm wider than the Camp).'}


def projectile_joint_mm(resolver, building, flag):
    """Model-space offset of the skeleton joint carrying a JointFlags bit (bit = 1 << flag id), at the model's
    scale, in simulation axes: x across, y up, z the source y."""
    resolved = resolver.resolve(resolver.db['Models'].get(resolver.db['Buildings'].get(building)['Model']).name, recurse=False)
    for skeleton in resolved['skeletons']:
        for joint in lsk.parse((wor.ROOT / skeleton['file']).read_bytes()).joints:
            if joint.flags & (1 << flag):
                (x, y, z), scale = joint.bind_position, resolved['scale'][0]
                return [round(x*scale*1000), round(z*scale*1000), round(y*scale*1000)]
    raise ValueError('no joint with flag %d on %s' % (flag, building))


def defense_data(output, resolver, factions=('good', 'evil')):
    """Source building defenses of every skirmish building (the Watchtower's arrows; the Ballista Tower's arrows and
    bolt): per building, each BuildingDefenses entry's weapon, rates of fire by garrison size, the projectile joint and
    the projectile's model, launch sounds and trail."""
    import combat_rules
    defenses = {}
    rates = ['BaseRateOfFire', 'RateOfFireWithOneUnitGarrisoned', 'RateOfFireWithTwoUnitsGarrisoned',
             'RateOfFireWithThreeUnitsGarrisoned', 'RateOfFireWithFourUnitsGarrisoned']
    for name in [n for n in skirmish_map.SKIRMISH_BUILDINGS if skirmish_map.faction_of(n) in factions]:
        record = resolver.db['Buildings'].get(name)
        for slot in range(1, 4):
            if record['BuildingDefenses%d' % slot] < 0:
                continue
            defense = resolver.db['BuildingDefenses'].get(record['BuildingDefenses%d' % slot])
            weapon = resolver.db['Weapons'].get(defense['WeaponType'])
            sources = {}
            def trace(path):
                path = Path(path).resolve()
                sources[path.relative_to(wor.ROOT).as_posix() if path.is_relative_to(wor.ROOT) else path.name] = \
                    hashlib.sha256(path.read_bytes()).hexdigest()
            defenses.setdefault(name, []).append({'defense': defense.name, 'weapon': weapon.name,
                'dps_milli': round((weapon['CuttingDamagePerSecond']+weapon['FireDamagePerSecond'])*1000),
                'fate_ppm': round(weapon['FateModifier']*1_000_000),
                # Build-130: the Ballista bolt (WEAPON_BUILDING_G_SUPERTOWER_BOLT) can critically kill.
                'critical_kill': bool(weapon['CanCriticallyKill']),
                'minimum_range_mm': round(weapon['MinimumRange']*1000), 'range_mm': round(weapon['MaximumRange']*1000),
                'rates_milli': [round(defense[rate]*1000) for rate in rates],
                'max_garrison': record['MaxGarrisonedUnits'],
                'origin_mm': projectile_joint_mm(resolver, name, defense['ProjectileSourceJointFlag']),
                'projectile': combat_rules.projectile_presentation(output, resolver, None, trace, weapon['AssociatedProjectile']),
                'sources': sources})
    return defenses


def construction_data(resolver, resources):
    definitions = {kind: building_rules(resolver, name) for kind, name in BUILDINGS.items()}
    # Build-133: the same two kinds per faction; the sites above stay the Good Mill and Foundry footprints for both.
    faction_definitions = {key: {kind: building_rules(resolver, name) for kind, name in names.items()} for key, names in FACTION_BUILDINGS.items()}
    sites, footprints = [], []
    for node in resources:
        obstacle = OBSTACLE_BASE + node['id']
        footprint, _ = skirmish_map.building_footprint(resolver, BUILDINGS[node['kind']], node['position'], obstacle)
        footprint['name'] = 'Resource construction %d' % node['id']
        footprints.append(footprint)
        sites.append({'resource': node['id'], 'kind': node['kind'], 'obstacle': obstacle})
    buildings = {}
    for name in skirmish_map.SKIRMISH_BUILDINGS:
        record = resolver.db['Buildings'].get(name)
        portrait = resolver.db['UVAs'].get(record['Portrait'])
        buildings[name] = {'display_name': display_name(name, record),
            'max_health': record['MaxHealth'], 'armor': record['ArmorPoints'],
            'portrait': Path(portrait['Filename'].replace('\\', '/')).stem,
            'selection_radius_mm': round(record['SelectionRingRadius']*1000)}
    return {'definitions': definitions, 'faction_definitions': faction_definitions, 'sites': sites, 'buildings': buildings}, footprints


def export_buildings(output, resolver):
    """Every skirmish building model, restored with its scaffold stages; returns building name to cache file."""
    from lms2gltf import convert_model
    import prototype_assets
    building_dir = output / 'buildings'
    building_dir.mkdir(exist_ok=True)
    files = {}
    for name in skirmish_map.SKIRMISH_BUILDINGS:
        model = resolver.db['Models'].get(resolver.db['Buildings'].get(name)['Model'])
        target = building_dir / (model.name + '.glb')
        convert_model(resolver, model.name, str(target))
        # Scaffold stages and the healthy state are selected at runtime.
        prototype_assets.restore_building(target)
        files[name] = 'buildings/' + target.name
    return files


def refresh(output):
    import prepare_prototype
    import prototype_clearance
    import prototype_interface
    import world_scene
    output = output.resolve()
    if any(output.is_relative_to(wor.ROOT / p) for p in ['extracted', 'TLOTR - War of the Ring']):
        raise ValueError('construction cache must be outside original and extracted data')
    resolver = prepare_prototype.open_resolver(output)
    path = output / 'manifest.json'
    manifest = json.loads(path.read_text())
    if manifest.get('mode') != 'skirmish':
        raise ValueError('construction refresh requires a skirmish cache')
    import content_pack
    content_pack.reject_slim_cache(manifest)
    data = manifest['skirmish']
    data['building_files'] = export_buildings(output, resolver)
    building_paths = [output / file for file in sorted(set(data['building_files'].values()))]
    construction, footprints = construction_data(resolver, data['resources'])
    bases = [skirmish_map.building_footprint(resolver, skirmish_map.CITADEL, base['position'], base['footprint_object'])[0]
             for base in data['bases']]
    for base, footprint in zip(data['bases'], bases):
        footprint['name'] = 'Start Citadel %d' % base['team']
    scene = world_scene.parse((wor.ROOT / 'extracted/Levels' / manifest.get('map_file', skirmish_map.MAP_FILE)).read_bytes())
    world_scene.resolve_objects(scene, resolver.db)
    prepare_prototype.build_navigation(scene, resolver, output, bases+footprints, [s['obstacle'] for s in construction['sites']], precise=True)
    prototype_clearance.refresh(output)
    prototype_interface.export(output, resolver)
    import skirmish_economy
    import skirmish_production
    data['economy'] = skirmish_economy.economy_data(resolver)
    data['production'] = skirmish_production.export(output, resolver, [m['unit'] for m in manifest['models']])
    data['construction'] = {**construction, **placement_data(resolver), 'defenses': defense_data(output, resolver)}
    data['selectable_objects'] = skirmish_map.selectable_objects(scene, resolver)
    for file in [output/'navigation.json', output/'heights.bin', *building_paths, output/skirmish_production.RALLY_BANNER_FILE,
                 *sorted((output/'interface').glob('*')), *[p for p in sorted((output/'combat').rglob('*')) if p.is_file()]]:
        manifest['files'][file.relative_to(output).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    path.write_text(json.dumps(manifest, indent=2)+'\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    refresh(parser.parse_args().cache)
