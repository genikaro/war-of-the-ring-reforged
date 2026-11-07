"""Research (source Techniques) at the skirmish buildings: costs, chains, prerequisites and per-unit bonuses.

python tools/skirmish_research.py --cache converted/skirmish

Techniques whose effect the simulation covers are exported as researchable: unit damage and armor bonuses,
missile range, replaced missile weapons, building health, Shield Break, Camouflage and Ranger sight/detection.
The remaining techniques are listed as unavailable with the reason under 'unsimulated'.
"""
import argparse
import hashlib
import json
from pathlib import Path

import skirmish_map
import dwarf_abilities
import ranger_abilities
import elven_abilities
import nature_abilities
import evil_upgrades


# The English manual (hotkey table, PDF page 23) names the Evil armor chain Iron, Forged, Steel Fittings; the source chain
# runs TECHNIQUE_FORGED_FITTINGS, TECHNIQUE_IRON_FITTINGS, TECHNIQUE_STEEL_FITTINGS (+1, +2, +3).
DISPLAY_NAMES = {'TECHNIQUE_FORGED_FITTINGS': 'Iron Fittings', 'TECHNIQUE_IRON_FITTINGS': 'Forged Fittings',
                 'TECHNIQUE_ASSASSINS_CLOAK': "Assassin's Cloak"}


def display_name(name):
    """TECHNIQUE_IRON_BLADES -> Iron Blades, as the shipped English manual names them (hotkey table, PDF page 21);
    TECHNIQUE_STRENGTH_OF_THE_CLAW -> Strength of the Claw, TECHNIQUE_NATURES_SHIELD -> Nature's Shield."""
    if name in DISPLAY_NAMES:
        return DISPLAY_NAMES[name]
    words = name.removeprefix('TECHNIQUE_').lower().split('_')
    words = [w if i and w in ('of', 'the') else "Nature's" if w == 'natures' else w.capitalize() for i, w in enumerate(words)]
    return ' '.join(words).replace('Lothlorien', 'Lothlórien')


def unsupported(record):
    """Why a technique's effect is not simulated, or None when every part of it is: unit damage/armor bonuses, the
    missile range multiplier, a replaced missile weapon and a health multiplier on every building."""
    if record.name in evil_upgrades.SUPPORTED:
        return None
    if record['AddBattleGear'] >= 0 and record['AddBattleGear'] not in (14, 26, 9, 23, 32, 28, 33) or record['RemoveBattleGear'] >= 0:
        return 'grants an ability'
    if record['LineOfSightRadiusModifier'] != 1 and record.name != 'TECHNIQUE_EAGLE_EYE':
        return 'changes sight; there is no fog of war yet'
    if record['AffectedBuildingType1'] >= 0 or record['AffectedBuildingType2'] >= 0 \
            or record['AffectsAllBuildings'] and record['HitPointsModifier'] == 1:
        return 'affects some buildings'
    if record['TimeScaleModifier'] != 1 or record['IsDetector'] or record['MeleeWeapon'] >= 0 \
            or record['HitPointsModifier'] != 1 and not record['AffectsAllBuildings']:
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
        if record['MissileWeaponRangeModifier'] != 1:
            bonuses[unit.name]['range_ppm'] = round(record['MissileWeaponRangeModifier']*1_000_000)
        if record['LineOfSightRadiusModifier'] != 1:
            bonuses[unit.name]['sight_ppm'] = round(record['LineOfSightRadiusModifier']*1_000_000)
        if record['TimeScaleModifier'] != 1:
            bonuses[unit.name]['time_scale_ppm'] = round(record['TimeScaleModifier']*1_000_000)
        if record['MissileWeapon'] >= 0:
            # The unit's missile weapon is replaced: Razored Edge (weapon 48), then Flaming Axe (49), for the Axethrower.
            weapon = resolver.db['Weapons'].get(record['MissileWeapon'])
            bonuses[unit.name]['missile_dps_milli'] = round((weapon['CuttingDamagePerSecond']+weapon['FireDamagePerSecond'])*1000)
            bonuses[unit.name]['missile_weapon'] = weapon.name
            # Build-171: what the replacement weapon adds besides its DPS. Knockback Arrows (weapon 52) always throws its
            # target (AlwaysKnockbackTarget, KnockbackStrength 6, angle 25); Unstoppable Force (weapon 73) splashes 5 m.
            if weapon['AlwaysKnockbackTarget'] and weapon['KnockbackStrength'] > 0:
                bonuses[unit.name]['missile_knockback_mm'] = weapon['KnockbackStrength']*1000
                bonuses[unit.name]['missile_knockback_angle'] = round(weapon['KnockbackAngle'])
            if weapon['AreaOfEffectRange'] > 0:
                bonuses[unit.name]['missile_splash_radius_mm'] = round(weapon['AreaOfEffectRange']*1000)
                bonuses[unit.name]['missile_splash_dps_milli'] = round(weapon['AreaOfEffectDamage']*1000)
            projectile = weapon['AssociatedProjectile']
            if projectile >= 0 and projectile != resolver.db['Weapons'].get(unit['MissileWeapon'])['AssociatedProjectile']:
                bonuses[unit.name]['missile_projectile'] = projectile
    return bonuses


def research_data(resolver, factions=('good', 'evil')):
    buildings, techniques = resolver.db['Buildings'], resolver.db['Techniques']
    available, unavailable, unsimulated = {}, {}, {}
    for technique in techniques.records:
        if technique['ResearchableIn'] < 0 or buildings.get(technique['ResearchableIn']).name not in skirmish_map.SKIRMISH_BUILDINGS \
                or skirmish_map.faction_of(buildings.get(technique['ResearchableIn']).name) not in factions:
            continue
        required = buildings.get(technique['BuildingRequired']).name if technique['BuildingRequired'] >= 0 else None
        previous = technique['ChainPreviousTechnique']
        portrait = resolver.db['UVAs'].get(technique['PortraitBorder'])
        entry = {
            'display_name': display_name(technique.name), 'building': buildings.get(technique['ResearchableIn']).name,
            # SpecificIndexForPurchasing is the action-grid position: Q for the blades chain, W for plating (manual hotkeys).
            'slot': technique['SpecificIndexForPurchasing'], 'food_cost': technique['CostFood'], 'ore_cost': technique['CostOre'],
            'duration_ticks': (technique['BuildTime']+49)//50, 'prerequisite': required,
            'previous': techniques.get(previous).name if previous >= 0 else None,
            'portrait': Path(portrait['Filename'].replace('\\', '/')).stem}
        reason = 'requires ' + required if required and required not in skirmish_map.SKIRMISH_BUILDINGS else unsupported(technique)
        if reason:
            unavailable[technique.name] = reason
            unsimulated[technique.name] = {**entry, 'reason': reason}
            continue
        bonuses = unit_bonuses(resolver, technique)
        if technique.name not in evil_upgrades.SUPPORTED and technique.name != 'TECHNIQUE_ELVEN_CLOAKS' and not technique['AffectsAllBuildings'] and technique['AddBattleGear'] < 0 and not any(v for b in bonuses.values() for k, v in b.items() if k != 'source'):
            # Elven Cloaks: no source field carries its effect; the tooltip's cloaking lives in the game program.
            unavailable[technique.name] = 'effect not in the source tables'
            unsimulated[technique.name] = {**entry, 'reason': unavailable[technique.name]}
            continue
        available[technique.name] = {**entry, 'bonuses': bonuses}
        descriptions = {**dwarf_abilities.DESCRIPTIONS, **ranger_abilities.DESCRIPTIONS, **elven_abilities.DESCRIPTIONS, **nature_abilities.DESCRIPTIONS, **evil_upgrades.DESCRIPTIONS}
        if technique.name in descriptions:
            available[technique.name]['description'] = descriptions[technique.name]
        if technique['AffectsAllBuildings']:
            available[technique.name]['building_health_ppm'] = round(technique['HitPointsModifier']*1_000_000)
        if technique.name == 'TECHNIQUE_POISON_ARROWS':
            weapon=resolver.db['Weapons'].get(technique['MissileWeapon'])
            available[technique.name]['building_weapon'] = dict(Building='BUILDING_E_TOWER',
                DpsMilli=round(weapon['CuttingDamagePerSecond']*1000), RangeMm=round(weapon['MaximumRange']*1000),
                MinimumRangeMm=round(weapon['MinimumRange']*1000), FatePpm=round(weapon['FateModifier']*1_000_000),
                Poison=dict(Id=-2000-technique.id,Slot=0,Name='Poison Arrows',Icon=entry['portrait'],Passive=True,Kind=50,
                    **evil_upgrades.poison_data(resolver.db,weapon)))
    # A chain step whose predecessor is not offered cannot be reached either.
    for name in [n for n, t in available.items() if t['previous'] and t['previous'] not in available]:
        unavailable[name] = 'requires ' + available[name]['previous']
        unsimulated[name] = {**{k: v for k, v in available.pop(name).items() if k != 'bonuses'}, 'reason': unavailable[name]}
    fire = {b.name: {'MaxDpsMilli': round(b['MaxFireDamagePerSecond']*1000),
                    'ArmorPpm': round(b['FireArmor']*1_000_000),
                    'ExtinguishPpmPerSecond': round(b['FireExtinguishRate']*1_000_000)}
            for b in buildings.records if b.name in skirmish_map.SKIRMISH_BUILDINGS and skirmish_map.faction_of(b.name) in factions}
    return {'format_version': 1, 'ticks_per_second': 20, 'techniques': available, 'unavailable': unavailable,
            'building_fire': fire,
            'unsimulated': unsimulated,
            'adapter': 'BuildTime milliseconds round up to 20 Hz ticks. A building researches one technique at a time and '
                       'cannot research while upgrading; cancelling refunds the full cost (no source refund field). '
                       'Bonuses use the per-unit TechniqueUnitModifiers rows, added to weapon DPS and armor, which matches '
                       'the integer Attack/Armor ratings of the unit panel; the technique rows also carry a DamageModifier '
                       'multiplier (1.2 for Iron Blades) whose relation to them is unverified. A later tier of a chain '
                       'replaces the earlier one (+1, +2, +3, not a sum), as the Elven Archer panel showed after each Outpost '
                       'blade (good-035, -046, -056). A replaced missile weapon sets the missile DPS; its chain target and '
                       'projectile are handled by the Dwarf passive adapter. Fire damage adds normalized building fire intensity; '
                       'source extinguish rate decays it and MaxFireDamagePerSecond scales its damage (engine semantics unverified). '
                       'Eagle Eye doubles Ranger sight and the radius used to detect normal invisibility; terrain fog is not simulated. '
                       'Camouflage follows its spell graph: idle and hide until motion or attack, then start its three-second recharge. '
                       'The building health multiplier raises the maximum and the current '
                       'health of every building by the same share (unverified).'}


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
