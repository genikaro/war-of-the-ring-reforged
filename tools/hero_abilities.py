"""Small, traced Aragorn and Frodo ability adapters; no general spell VM or purchase economy."""
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
          'UNIT_G_HERO_FRODO': {7: 'The One Ring', 12: 'Heroic Aura', 50: 'Sting'}}


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
        if gear.id == 41:
            animation = find('_PLAY_ANIMATION')
            state = next(s for s in model['animation_states'] if s['id'] == animation['UnitAnimType'])
            choice = next(c for c in state['choices'] if c['weight'] > 0)
            duration = next(c.duration for c in skeleton.clips if c.name == choice['clip_name'])
            event = next(e for e in events if e['ToolscriptType'] >= 0)
            release = next(model['events'][i] for i in choice['event_indices']
                if model['events'][i]['type'] == event['ToolscriptType'])
            cast_ticks = math.ceil(duration * 20)
            definition.update(Kind=0, HealMilli=round(find('_APPLY_HEALTH')['Float_01'] * 1000),
                RechargeTicks=math.ceil(spell['RechargeTime'] * 20),
                RangeMm=round(target['MaxSpecifiedRange'] * 1000),
                MaxTargetHealthPpm=round(target['MaxTargetHealthPct'] * 1_000_000),
                CastClip=choice['clip_name'], CastTicks=cast_ticks,
                EffectTick=min(cast_ticks - 1, math.ceil(duration * release['time'] * 20)))
            definition['Source']['Animation'] = {'State': state['id'], 'Duration': duration,
                'Event': release, 'Toolscript': event['ToolscriptType']}
            definition['Source']['SoundEvents'] = [model['events'][i]['sound'] for i in choice['event_indices']
                if model['events'][i].get('name') == 'sound']
            definition['SoundEvents'] = definition['Source']['SoundEvents']
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
            child = db['Spells'].get(find('_SPAWN_CHILD_SPELL')['SpellType'])
            child_actions = [a for e in linked(db['SpellAndEvents'], child['FirstSpellAndEvent'], 'NextSpellAndEvent')
                             for a in linked(db['SpellAndEventAndActions'], e['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction')]
            recharge = next(a for a in child_actions if action_type(a).endswith('_SET_BATTLEGEAR_RECHARGE_MULTIPLIER'))
            definition.update(Kind=5, RangeMm=round(target['MaxSpecifiedRange'] * 1000),
                RechargeScalePpm=round(recharge['Float_01'] * 1_000_000))
            definition['Source']['Child'] = {'Spell': child.values, 'Actions': [{'Id': a.id, 'Type': action_type(a), 'Values': a.values} for a in child_actions]}
        abilities.append(definition)
    return sorted(abilities, key=lambda a: a['Slot'])
