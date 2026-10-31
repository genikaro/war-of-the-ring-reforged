"""Evil research effects and unit gear, traced to Techniques, Weapons and spell graphs.

Assassin's Cloak has no table effect (IsVisible=0); GUIDE.txt describes the same
detector-revealable invisibility as Elven Cloaks. That one effect is an explicit adapter.
"""
import math
from pathlib import PureWindowsPath

from evil_abilities import walk, cast_clip

TECHNIQUES = {
    'UNIT_E_ORC_BOWMAN': ['TECHNIQUE_FLAME_ARROWS'],
    'UNIT_E_GOBLIN_SLAVE_MASTER': ['TECHNIQUE_SAVAGE_INFLUENCE'],
    'UNIT_E_HARADRIM_ASSASSIN': ['TECHNIQUE_POISON_DART', 'TECHNIQUE_ASSASSINS_CLOAK'],
    'UNIT_E_GIANT_SPIDER': ['TECHNIQUE_PARALYZE', 'TECHNIQUE_SPRAY_VENOM'],
    'UNIT_E_TROLL_BONECLEAVER': ['TECHNIQUE_SWEEPING_AXE'],
    'UNIT_E_WRAITH': ['TECHNIQUE_DARKNESS', 'TECHNIQUE_PERPETUAL_DARKNESS', 'TECHNIQUE_GREATER_PERCEPTION'],
    'UNIT_E_BONUS_BLACK_RIDER': ['TECHNIQUE_MORGUL_BLADE'],
}
SUPPORTED = {t for ts in TECHNIQUES.values() for t in ts} | {'TECHNIQUE_POISON_ARROWS', 'TECHNIQUE_GOBLIN_FRENZY'}
DESCRIPTIONS = {
    'TECHNIQUE_FLAME_ARROWS': 'Orc Bowman arrows set enemy buildings alight.',
    'TECHNIQUE_SAVAGE_INFLUENCE': 'Whips allies within 10 m for 30 health. For 10 seconds they deal 25% more damage. Recharges in 36 seconds.',
    'TECHNIQUE_POISON_DART': 'Fires a dart within 10 m: 5 poison damage per second for 25 seconds. Costs 25 food and 25 ore per shot; 8 second recharge.',
    'TECHNIQUE_ASSASSINS_CLOAK': 'Haradrim Slayers are invisible to enemies except detectors (guide-based adapter).',
    'TECHNIQUE_PARALYZE': 'Spider melee hits stun enemies for 2 seconds.',
    'TECHNIQUE_SPRAY_VENOM': 'Stuns enemies in a 10 m, 180-degree cone for 4 seconds. Suppresses Paralyze for 30 seconds.',
    'TECHNIQUE_SWEEPING_AXE': 'Deals 100 damage to enemies within 5 m. Recharges in 12 seconds.',
    'TECHNIQUE_DARKNESS': 'Slows enemies within 5 m to half movement and attack speed for 10 seconds.',
    'TECHNIQUE_PERPETUAL_DARKNESS': 'Replaces Darkness: slows and blinds enemies within 7 m for 10 seconds.',
    'TECHNIQUE_GREATER_PERCEPTION': 'Doubles Wraith sight and detection range.',
    'TECHNIQUE_MORGUL_BLADE': 'Black Rider melee hits poison enemies: 3 damage per second for 5 seconds.',
    'TECHNIQUE_POISON_ARROWS': 'Evil Towers use poison arrows: 14 damage per second, plus 2 poison damage per second for 10 seconds.',
    'TECHNIQUE_GOBLIN_FRENZY': 'Goblin Spearmen and Slavemasters deal 50% more base damage and move and attack 30% faster (source unit list).',
}


def poison_data(db, weapon):
    poison = db['Poisons'].get(weapon['PoisonType'])
    return dict(DpsMilli=round(poison['DamagePerSecond']*1000), DurationTicks=math.ceil(poison['Duration']/50),
                ExperiencePpm=round(poison['ExperienceModifier']*1_000_000), PoisonFatePpm=round(poison['FateModifier']*1_000_000))


def export(resolver, unit, model, skeleton):
    db, result = resolver.db, []
    for name in TECHNIQUES.get(unit.name, ()):
        technique = db['Techniques'].get(name)
        gear = db['BattleGear'].get(technique['AddBattleGear'])
        icon = db['UVAs'].get(gear['PortraitBorderless'] if gear else technique['PortraitBorderless'])
        a = dict(Id=gear.id if gear else -1000-technique.id,
                 Slot=gear['SpecificIndex'] if gear else technique['SpecificIndexForUVA'],
                 Name=name.removeprefix('TECHNIQUE_').replace('_', ' ').title().replace('Assassins', "Assassin's"),
                 Icon=PureWindowsPath(icon['Filename']).stem, Passive=True, RequiredTechnique=name,
                 Description=DESCRIPTIONS[name], Source={'Technique': technique.values})
        if gear:
            spell = db['Spells'].get(gear['SpellType'])
            actions = walk(db, spell)
            def typed(suffix):
                return [action for event, action in actions if db['SpellActions'].get(action['SpellActionType']).name.endswith(suffix)]
            a['Source'].update(Gear=gear.values, Spell=spell.values, Actions=[action.values for _, action in actions])
            cursor=db['Cursors'].get(spell['TargetCursor'])
            a['Cursor']=PureWindowsPath(cursor['Filename']).stem if cursor else ''
        if name == 'TECHNIQUE_FLAME_ARROWS':
            weapon = db['Weapons'].get(technique['MissileWeapon'])
            a.update(Kind=57, FirePpmPerSecond=round(weapon['FireDamagePerSecond']*1_000_000))
        elif name == 'TECHNIQUE_ASSASSINS_CLOAK':
            a.update(Kind=18, AlphaPpm=500000)
            a['Source']['Adapter'] = 'GUIDE.txt: same invisibility as Elven Cloaks; no effect in the hidden technique row.'
        elif name == 'TECHNIQUE_GREATER_PERCEPTION':
            a.update(Kind=17)
        elif name == 'TECHNIQUE_MORGUL_BLADE':
            weapon = db['Weapons'].get(typed('_SWITCH_MELEE_WEAPON')[0]['WeaponType'])
            a.update(Kind=50, **poison_data(db, weapon))
        elif name == 'TECHNIQUE_PARALYZE':
            weapon = db['Weapons'].get(technique['MeleeWeapon'])
            base=db['Weapons'].get(unit['MeleeWeapon'])
            a.update(Kind=56, DurationTicks=math.ceil(weapon['StunDuration']*20),RangeMm=round((weapon['MaximumRange']-base['MaximumRange'])*1000))
        else:
            a.update(Passive=False, RechargeTicks=math.ceil(spell['RechargeTime']*20), RangeMm=10000)
            a.update(cast_clip(db, actions, model, skeleton))
            if name == 'TECHNIQUE_POISON_DART':
                weapon=db['Weapons'].get(typed('_SWITCH_MISSILE_WEAPON')[0]['WeaponType'])
                target=db['SpellValidTargets'].get(spell['ValidTargetType'])
                a.update(Kind=51, RangeMm=round(target['MaxSpecifiedRange']*1000),
                         ActivateFoodCost=gear['ActivateCostFood'], ActivateOreCost=gear['ActivateCostOre'], **poison_data(db, weapon))
            elif name == 'TECHNIQUE_SAVAGE_INFLUENCE':
                area=typed('_SPAWN_CHILD_SPELL')[0]
                a.update(Kind=52, AreaRadiusMm=round(area['OccuranceRadius']*1000), HealMilli=round(-typed('_APPLY_HEALTH')[0]['Float_01']*1000),
                         BonusScale=round(typed('_APPLY_DAMAGE_MULTIPLIER')[0]['Float_01']*1_000_000), DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20))
            elif name in ('TECHNIQUE_DARKNESS','TECHNIQUE_PERPETUAL_DARKNESS'):
                area=typed('_SPAWN_CHILD_SPELL')[0]
                a.update(Kind=53, AreaRadiusMm=round(area['OccuranceRadius']*1000),
                         BonusScale=round(typed('_APPLY_TIMESCALE_MULTIPLIER')[0]['Float_01']*1_000_000), DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20),
                         Blinds=bool(typed('_SET_BLIND')))
                if name == 'TECHNIQUE_DARKNESS': a['ReplacedByTechnique']='TECHNIQUE_PERPETUAL_DARKNESS'
            elif name == 'TECHNIQUE_SPRAY_VENOM':
                stun=typed('_SET_DAZED')[0]
                a.update(Kind=54, AreaRadiusMm=round(stun['OccuranceRadius']*1000), ArcDegrees=round(stun['OccuranceRadiusFacingAngle']),
                         DurationTicks=math.ceil(stun['Float_01']*20), DelayTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20))
            elif name == 'TECHNIQUE_SWEEPING_AXE':
                damage=typed('_APPLY_DAMAGE')[0]
                a.update(Kind=55, AreaRadiusMm=round(damage['OccuranceRadius']*1000), HealMilli=damage['DamageAmount']*1000)
        result.append(a)
    return result
