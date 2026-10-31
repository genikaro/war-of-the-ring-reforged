"""Research-locked Dwarf passives, traced to Techniques, Weapons and the Shield Break spell graph."""
import math
from pathlib import PureWindowsPath

from hero_abilities import linked


TECHNIQUES = {'UNIT_G_DWARVEN_AXETHROWER': ('TECHNIQUE_RAZORED_EDGE', 'TECHNIQUE_FLAMING_AXE'),
              'UNIT_G_DWARVEN_SHIELDBREAKER': ('TECHNIQUE_SHIELD_BREAK',)}
DESCRIPTIONS = {
    'TECHNIQUE_RAZORED_EDGE': 'Thrown axes continue through one enemy behind the target within 5 m, dealing 70% damage.',
    'TECHNIQUE_FLAMING_AXE': 'Sets enemy buildings alight, dealing up to 7 fire damage per second for up to 6.25 seconds after the last hit.',
    'TECHNIQUE_SHIELD_BREAK': 'Each melee hit reduces enemy armor by 1 for 20 seconds. Stacks up to 5 times.'}


def export(resolver, unit):
    db = resolver.db
    result = []
    for name in TECHNIQUES.get(unit.name, ()):
        technique = db['Techniques'].get(name)
        icon = db['UVAs'].get(technique['PortraitBorderless'])
        ability = dict(Id=-1000-technique.id, Slot=technique['SpecificIndexForUVA'],
                       Name=name.removeprefix('TECHNIQUE_').replace('_', ' ').title(),
                       Icon=PureWindowsPath(icon['Filename']).stem, Passive=True, RequiredTechnique=name,
                       Description=DESCRIPTIONS[name], Source={'Technique': technique.values})
        if technique['AddBattleGear'] >= 0:
            gear = db['BattleGear'].get(technique['AddBattleGear'])
            spell = db['Spells'].get(gear['SpellType'])
            def actions(s):
                return [a for e in linked(db['SpellAndEvents'], s['FirstSpellAndEvent'], 'NextSpellAndEvent')
                        for a in linked(db['SpellAndEventAndActions'], e['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction')]
            parent = actions(spell)
            child = db['Spells'].get(next(a['SpellType'] for a in parent
                if db['SpellActions'].get(a['SpellActionType']).name.endswith('_SPAWN_CHILD_SPELL')))
            found = actions(child)
            def find(suffix):
                return next(a for a in found if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix))
            ability.update(Id=gear.id, Slot=gear['SpecificIndex'], Kind=12,
                           ArmorReductionMilli=round(-find('_SET_ARMOR')['Float_01']*1000),
                           DurationTicks=math.ceil(find('_START_TIMER')['Float_01']*20), MaxStacks=child['StackableMax'])
            ability['Source'].update(Gear=gear.values, Spell=spell.values, Child=child.values,
                                     Actions=[a.values for a in parent+found])
        else:
            weapon = db['Weapons'].get(technique['MissileWeapon'])
            ability.update(Kind=14 if weapon['FireDamagePerSecond'] > 0 else 13,
                           ChainTargets=weapon['MaxChainTargets'], RangeMm=round(weapon['MaxChainTargetDistance']*1000),
                           ChainStartRangeMm=round(weapon['MaxChainStartDistance']*1000),
                           BonusScale=round(weapon['ChainTargetDamageReduction']*1_000_000),
                           FirePpmPerSecond=round(weapon['FireDamagePerSecond']*1_000_000))
            ability['Source']['Weapon'] = weapon.values
        result.append(ability)
    return result
