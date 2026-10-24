"""Small, traced hero ability adapters (Aragorn, Frodo, Gimli's Siege Aura, Legolas's Guardian Wind); no general spell
VM or purchase economy. Every other ability of an exported unit is listed with its source slot, icon and an English
description, marked unsimulated (Kind 8), so the action grid shows the unit's full set."""
import math


def linked(table, first, next_field):
    result, seen = [], set()
    while first >= 0:
        if first in seen:
            raise ValueError('cycle in spell linkage')
        seen.add(first)
        record = table.get(first)
        if record is None:
            raise ValueError('missing spell linkage')
        result.append(record)
        first = record[next_field]
    return result


HEROES = {'UNIT_G_HERO_ARAGORN': {25: "Andúril's Fury", 41: 'Greater Kingsfoil', 44: 'Immunity'},
          'UNIT_G_HERO_FRODO': {7: 'The One Ring', 12: 'Heroic Aura', 50: 'Sting'},
          'UNIT_G_HERO_GIMLI': {10: 'Siege Aura', 27: 'Sunder', 45: 'Capture'},
          'UNIT_G_HERO_LEGOLAS': {0: 'Trueshot', 6: 'Elven Speed', 19: 'Guardian Wind'},
          'UNIT_G_HERO_GANDALF': {15: 'Blinding Light', 40: 'Negate Ability', 42: 'Flame Shield', 43: 'Rain of Fire'},
          'UNIT_G_ELVEN_LIGHTBRINGER': {24: 'Protection'}}
# English descriptions of the abilities not simulated yet, after the shipped manual (PDF pages 15-17) and the captured
# Russian tooltips (good-020 to -069).
UNSIMULATED = {27: 'Gimli slams the ground with his axe, knocking down and stunning nearby enemies.',
               45: 'Gimli takes an enemy tower, ejecting its garrison, and turns it against the enemy.',
               0: 'Legolas shoots an arrow that knocks the target back out of the fight for a moment.',
               6: 'Legolas and the troops around him move and attack much faster for a while.',
               15: 'A blinding flash from the staff stuns every enemy around Gandalf for a moment.',
               40: 'Gandalf stops an enemy from using its special abilities for a while.',
               42: 'A shield on an ally that reflects part of the damage it takes back at the attacker.',
               43: 'Fire rains on an area, burning enemy units and setting enemy buildings alight.',
               24: 'A protective layer of light around an ally absorbs damage done to it.'}


def export(resolver, unit, model, skeleton):
    if unit.name not in HEROES:
        return []
    db = resolver.db
    abilities = []
    for gear_id in [unit['BattleGear%d' % i] for i in range(1, 5)]:
        if gear_id < 0:
            continue
        gear = db['BattleGear'].get(gear_id)
        spell = db['Spells'].get(gear['SpellType'])
        target = db['SpellValidTargets'].get(spell['ValidTargetType'])
        events = linked(db['SpellAndEvents'], spell['FirstSpellAndEvent'], 'NextSpellAndEvent')
        actions = [(event, action) for event in events for action in linked(
            db['SpellAndEventAndActions'], event['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction')]
        def action_type(a):
            return db['SpellActions'].get(a['SpellActionType']).name
        def find(suffix):
            return next(a for e, a in actions if action_type(a).endswith(suffix))
        icon = db['UVAs'].get(gear['PortraitBorderless'])
        definition = {'Id': gear.id, 'Slot': gear['SpecificIndex'],
            'Name': HEROES[unit.name][gear.id],
            'Icon': icon['Filename'].replace('\\', '/').rsplit('/', 1)[-1][:-4],
            'Passive': spell['UsageType'] == 0,
            'Source': {'Gear': gear.values, 'Spell': spell.values, 'Target': target.values if target else None,
                'Events': [{'Id': e.id, 'Name': e.name, 'Values': e.values} for e in events],
                'Actions': [{'Id': a.id, 'Name': a.name, 'Type': action_type(a), 'Values': a.values} for e, a in actions]}}
        def cast():
            """The cast clip of the spell's PLAY_ANIMATION and the tick of the toolscript event that applies it."""
            animation = find('_PLAY_ANIMATION')
            state = next(s for s in model['animation_states'] if s['id'] == animation['UnitAnimType'])
            choice = next(c for c in state['choices'] if c['weight'] > 0)
            duration = next(c.duration for c in skeleton.clips if c.name == choice['clip_name'])
            event = next(e for e in events if e['ToolscriptType'] >= 0)
            release = next(model['events'][i] for i in choice['event_indices']
                if model['events'][i]['type'] == event['ToolscriptType'])
            cast_ticks = math.ceil(duration * 20)
            definition.update(RechargeTicks=math.ceil(spell['RechargeTime'] * 20), CastClip=choice['clip_name'], CastTicks=cast_ticks,
                EffectTick=min(cast_ticks - 1, math.ceil(duration * release['time'] * 20)))
            definition['Source']['Animation'] = {'State': state['id'], 'Duration': duration,
                'Event': release, 'Toolscript': event['ToolscriptType']}
            definition['Source']['SoundEvents'] = [model['events'][i]['sound'] for i in choice['event_indices']
                if model['events'][i].get('name') == 'sound']
            definition['SoundEvents'] = definition['Source']['SoundEvents']
        def child_actions():
            child = db['Spells'].get(find('_SPAWN_CHILD_SPELL')['SpellType'])
            found = [a for e in linked(db['SpellAndEvents'], child['FirstSpellAndEvent'], 'NextSpellAndEvent')
                     for a in linked(db['SpellAndEventAndActions'], e['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction')]
            definition['Source']['Child'] = {'Spell': child.values, 'Actions': [{'Id': a.id, 'Type': action_type(a), 'Values': a.values} for a in found]}
            return found
        if gear.id == 41:
            cast()
            definition.update(Kind=0, HealMilli=round(find('_APPLY_HEALTH')['Float_01'] * 1000),
                RangeMm=round(target['MaxSpecifiedRange'] * 1000),
                MaxTargetHealthPpm=round(target['MaxTargetHealthPct'] * 1_000_000))
        elif gear.id == 25:
            weapon = db['Weapons'].get(find('_SWITCH_MELEE_WEAPON')['WeaponType'])
            definition.update(Kind=1, DpsMilli=round(weapon['CuttingDamagePerSecond'] * 1000),
                BonusScale=round(find('_APPLY_DAMAGE_MODIFIER_VERSUS_SPECIFIC_UNIT')['Float_01'] * 1_000_000),
                BonusTargets=[target['SpecificUnitType%d' % i] for i in range(1, 5) if target['SpecificUnitType%d' % i] >= 0])
            definition['Source']['Weapon'] = weapon.values
        elif gear.id == 44:
            flags = {'_SET_IMMUNE_TO_POISON': 1, '_SET_IMMUNE_TO_STUN': 2, '_SET_IMMUNE_TO_BLIND': 4, '_SET_IMMUNE_TO_SLOW': 8}
            definition.update(Kind=2, Immunities=sum(flag for suffix, flag in flags.items() if find(suffix)['Boolean_01']))
        elif gear.id == 7:
            # Toggle: OnActivate hides, pacifies and silences regeneration; OnContinuous drains health per second.
            on = next(e for e in events if e.name.endswith('_ONACTIVATE'))
            on_actions = linked(db['SpellAndEventAndActions'], on['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction')
            hide = next(a for a in on_actions if action_type(a).endswith('_SET_VISIBLE'))
            definition.update(Kind=3, DrainMilliPerTick=round(-find('_UPDATE_HEALTH')['Float_01'] * 1000 / 20),
                AlphaPpm=round(hide['Float_01'] * 1_000_000),
                SoundEvents=[a['SoundEventType'] for e, a in actions if action_type(a).endswith('_PLAY_SOUND_EVENT')])
            definition['Source']['SoundEvents'] = definition['SoundEvents']
        elif gear.id == 50:
            definition.update(Kind=4, BonusScale=round(find('_APPLY_DAMAGE_MODIFIER_VERSUS_SPECIFIC_UNIT')['Float_01'] * 1_000_000),
                BonusTargets=[target['SpecificUnitType%d' % i] for i in range(1, 5) if target['SpecificUnitType%d' % i] >= 0],
                RangeMm=round(target['MaxSpecifiedRange'] * 1000))
        elif gear.id == 12:
            recharge = next(a for a in child_actions() if action_type(a).endswith('_SET_BATTLEGEAR_RECHARGE_MULTIPLIER'))
            definition.update(Kind=5, RangeMm=round(target['MaxSpecifiedRange'] * 1000),
                RechargeScalePpm=round(recharge['Float_01'] * 1_000_000))
        elif gear.id == 10:
            # Allies entering the 10 m radius get the child spell: APPLY_BUILDING_DAMAGE_MODIFIER 1.5 (StackableType 3).
            modifier = next(a for a in child_actions() if action_type(a).endswith('_APPLY_BUILDING_DAMAGE_MODIFIER'))
            definition.update(Kind=6, RangeMm=round(target['MaxSpecifiedRange'] * 1000),
                BonusScale=round(modifier['Float_01'] * 1_000_000))
        elif gear.id == 19:
            # Cast without a target; at the clip's toolscript event every ally within 12 m takes the child spell's
            # APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER 0.01 until its 12 s timer.
            cast()
            found = child_actions()
            ward = next(a for a in found if action_type(a).endswith('_APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER'))
            timer = next(a for a in found if action_type(a).endswith('_START_TIMER'))
            definition.update(Kind=7, Passive=False, RangeMm=round(spell['MaxAreaOfEffectRange'] * 1000),
                BonusScale=round(ward['Float_01'] * 1_000_000), DurationTicks=math.ceil(timer['Float_01'] * 20))
        else:
            definition.update(Kind=8, RechargeTicks=math.ceil(spell['RechargeTime'] * 20), Description=UNSIMULATED[gear.id])
        abilities.append(definition)
    return sorted(abilities, key=lambda a: a['Slot'])
