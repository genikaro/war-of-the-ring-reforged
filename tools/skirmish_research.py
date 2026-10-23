"""Research (source Techniques) at the skirmish buildings: costs, chains, prerequisites and per-unit bonuses.

python tools/skirmish_research.py --cache converted/skirmish

Only techniques whose whole effect is a damage or armor bonus to units are exported as researchable; the rest
(building health, sight, battle gear abilities) are listed as unavailable with the reason.
"""
import argparse
import hashlib
import json
from pathlib import Path

import skirmish_map


def display_name(name):
    """TECHNIQUE_IRON_BLADES -> Iron Blades, as the shipped English manual names them (hotkey table, PDF page 21)."""
    return name.removeprefix('TECHNIQUE_').replace('_', ' ').title()


def unsupported(record):
    """Why a technique's effect is not simulated, or None for a pure unit damage/armor bonus."""
    if record['AddBattleGear'] >= 0 or record['RemoveBattleGear'] >= 0:
        return 'grants an ability'
    if record['AffectsAllBuildings'] or record['AffectedBuildingType1'] >= 0 or record['AffectedBuildingType2'] >= 0:
        return 'affects buildings'
    if record['LineOfSightRadiusModifier'] != 1 or record['MissileWeaponRangeModifier'] != 1:
        return 'changes sight or range'
    if record['HitPointsModifier'] != 1 or record['TimeScaleModifier'] != 1 or record['IsDetector'] \
            or record['MeleeWeapon'] >= 0 or record['MissileWeapon'] >= 0:
        return 'effect not simulated'
    return None


def unit_bonuses(resolver, technique):
    """Per-unit bonus: the source TechniqueUnitModifiers row (DamageAddifier/ArmorAddifier, added to the unit's
    weapon DPS and armor) where one exists, else the technique's own DamageModifier on the unit's weapon DPS and
    its ArmorModifier."""
    units, record = resolver.db['Units'], technique
    rows = {row['UnitType']: row for row in resolver.db['TechniqueUnitModifiers'].records if row['TechniqueType'] == technique.id}
    bonuses = {}
    for i in range(1, 6):
        unit_id = record['AffectedUnitType%d' % i]
        if unit_id < 0:
            continue
        unit = units.get(unit_id)
        if unit_id in rows:
            damage, armor = rows[unit_id]['DamageAddifier'], rows[unit_id]['ArmorAddifier']
        else:
            weapon = resolver.db['Weapons'].get(unit['MeleeWeapon'])
            damage, armor = weapon['CuttingDamagePerSecond']*(record['DamageModifier']-1), record['ArmorModifier']
        bonuses[unit.name] = {'damage_milli': round(damage*1000), 'armor_milli': round(armor*1000),
                              'source': 'TechniqueUnitModifiers' if unit_id in rows else 'Techniques'}
    return bonuses


def research_data(resolver):
    buildings, techniques = resolver.db['Buildings'], resolver.db['Techniques']
    available, unavailable = {}, {}
    for technique in techniques.records:
        if technique['ResearchableIn'] < 0 or buildings.get(technique['ResearchableIn']).name not in skirmish_map.SKIRMISH_BUILDINGS:
            continue
        required = buildings.get(technique['BuildingRequired']).name if technique['BuildingRequired'] >= 0 else None
        reason = unsupported(technique) or (
            'requires ' + required if required and required not in skirmish_map.SKIRMISH_BUILDINGS else None)
        if reason:
            unavailable[technique.name] = reason
            continue
        previous = technique['ChainPreviousTechnique']
        portrait = resolver.db['UVAs'].get(technique['PortraitBorder'])
        available[technique.name] = {
            'display_name': display_name(technique.name), 'building': buildings.get(technique['ResearchableIn']).name,
            # SpecificIndexForPurchasing is the action-grid position: Q for the blades chain, W for plating (manual hotkeys).
            'slot': technique['SpecificIndexForPurchasing'], 'food_cost': technique['CostFood'], 'ore_cost': technique['CostOre'],
            'duration_ticks': (technique['BuildTime']+49)//50, 'prerequisite': required,
            'previous': techniques.get(previous).name if previous >= 0 else None,
            'portrait': Path(portrait['Filename'].replace('\\', '/')).stem, 'bonuses': unit_bonuses(resolver, technique)}
    # A chain step whose predecessor is not offered cannot be reached either.
    for name in [n for n, t in available.items() if t['previous'] and t['previous'] not in available]:
        unavailable[name] = 'requires ' + available.pop(name)['previous']
    return {'format_version': 1, 'ticks_per_second': 20, 'techniques': available, 'unavailable': unavailable,
            'adapter': 'BuildTime milliseconds round up to 20 Hz ticks. A building researches one technique at a time and '
                       'cannot research while upgrading; cancelling refunds the full cost (no source refund field). '
                       'Bonuses use the per-unit TechniqueUnitModifiers rows, added to weapon DPS and armor, which matches '
                       'the integer Attack/Armor ratings of the unit panel; the technique rows also carry a DamageModifier '
                       'multiplier (1.2 for Iron Blades) whose relation to them is unverified.'}


def refresh(output):
    import prepare_prototype
    import prototype_interface
    output = output.resolve()
    resolver = prepare_prototype.open_resolver(output)
    path = output / 'manifest.json'
    manifest = json.loads(path.read_text(encoding='utf-8'))
    if manifest.get('mode') != 'skirmish':
        raise ValueError('research refresh requires a skirmish cache')
    import content_pack
    content_pack.reject_slim_cache(manifest)
    manifest['skirmish']['research'] = research_data(resolver)
    prototype_interface.export(output, resolver)
    # The purchase icons join the interface art; the cache verifies every file it lists.
    for file in sorted((output / 'interface').glob('*')):
        manifest['files'][file.relative_to(output).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    refresh(parser.parse_args().cache)
