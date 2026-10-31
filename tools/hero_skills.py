"""Remaining Good hero skills, decoded from the shipped spell/action graphs.

Numeric kinds append to AbilityKind; existing cache values remain stable.
"""
import math
from pathlib import PureWindowsPath

from elven_abilities import graph

KINDS = {27: 27, 45: 28, 0: 29, 6: 30, 15: 31, 40: 32, 42: 33, 43: 34}


def export(resolver, gear, model, skeleton, name):
    db = resolver.db
    spell = db['Spells'].get(gear['SpellType'])
    target = db['SpellValidTargets'].get(spell['ValidTargetType'])
    actions = graph(db, spell)

    def typed(suffix):
        return [a for e, a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix)]

    ability = dict(Id=gear.id, Slot=gear['SpecificIndex'], Name=name, Passive=False, Kind=KINDS[gear.id],
                   Icon=PureWindowsPath(db['UVAs'].get(gear['PortraitBorderless'])['Filename']).stem,
                   Cursor=PureWindowsPath(db['Cursors'].get(spell['TargetCursor'])['Filename']).stem,
                   RechargeTicks=math.ceil(spell['RechargeTime']*20),
                   RangeMm=round(target['MaxSpecifiedRange']*1000),
                   AreaRadiusMm=round(spell['MaxAreaOfEffectRange']*1000))
    animations = typed('_PLAY_ANIMATION')
    sounds = [a['SoundEventType'] for a in typed('_PLAY_SOUND_EVENT')]
    if animations:
        state = next(s for s in model['animation_states'] if s['id'] == animations[0]['UnitAnimType'])
        choice = next(c for c in state['choices'] if c['weight'] > 0)
        clip = next(c for c in skeleton.clips if c.name == choice['clip_name'])
        tools = {e['ToolscriptType'] for e, a in actions if e['ToolscriptType'] >= 0}
        release = next(model['events'][i] for i in choice['event_indices'] if model['events'][i]['type'] in tools)
        ticks = math.ceil(clip.duration*20)
        ability.update(CastClip=clip.name, CastTicks=ticks, EffectTick=min(ticks-1, math.ceil(clip.duration*release['time']*20)))
        sounds += [model['events'][i]['sound'] for i in choice['event_indices'] if model['events'][i].get('name') == 'sound']
    ability['SoundEvents'] = sorted(set(sounds))
    ability['Source'] = {'Gear': gear.values, 'Spell': spell.values, 'Target': target.values,
                         'SoundEvents': ability['SoundEvents'],
                         'Actions': [{'Event': e.name, 'Type': db['SpellActions'].get(a['SpellActionType']).name,
                                      'Values': a.values} for e, a in actions]}
    if gear.id in (27, 15):
        ability.update(DurationTicks=math.ceil(typed('_SET_DAZED')[0]['Float_01']*20))
        ability['Description'] = 'Dazes enemies within %g m for %g seconds.' % (ability['AreaRadiusMm']/1000, ability['DurationTicks']/20)
    elif gear.id == 45:
        ability.update(CaptureBuildings=[db['Buildings'].get(target['SpecificBuildingType%d' % i]).name for i in range(1, 4)])
        ability['Description'] = 'Approaches and captures an enemy Watchtower or Ballista Tower.'
    elif gear.id == 0:
        weapon = db['Weapons'].get(typed('_SWITCH_MISSILE_WEAPON')[0]['WeaponType'])
        ability.update(HealMilli=round(weapon['CuttingDamagePerSecond']*1000), RangeMm=round(weapon['MaximumRange']*1000),
                       KnockbackSpeedMm=weapon['KnockbackStrength']*1000, KnockbackAngle=round(weapon['KnockbackAngle']))
        ability['Source']['Weapon'] = weapon.values
        ability['ExperiencePpm'] = round(weapon['ExperienceModifier']*1_000_000)
        ability['Description'] = 'Fires a powerful arrow for 125 damage that knocks its target back.'
    elif gear.id == 6:
        ability.update(BonusScale=round(typed('_APPLY_TIMESCALE_MULTIPLIER')[0]['Float_01']*1_000_000),
                       DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20))
        ability['Description'] = 'Allies within 12 m move and attack 20% faster for 15 seconds.'
    elif gear.id == 40:
        ability.update(DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20))
        ability['Description'] = 'Disables enemy active and passive abilities in a 13 m area for 15 seconds.'
    elif gear.id == 42:
        ability.update(DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20), AreaRadiusMm=3000,
                       DpsMilli=round(-typed('_UPDATE_HEALTH')[0]['Float_01']*1000))
        ability['Description'] = 'Surrounds an ally with fire for 25 seconds, burning enemies within 3 m for 9 damage per second.'
    elif gear.id == 43:
        weapon = db['Weapons'].get(typed('_APPLY_DAMAGE')[0]['WeaponType'])
        timers = typed('_START_TIMER')
        ability.update(FirePpmPerSecond=round(weapon['FireDamagePerSecond']*1_000_000), DelayTicks=math.ceil(timers[0]['Float_01']*20),
                       DurationTicks=math.ceil(timers[1]['Float_01']*20), DpsMilli=round(-typed('_UPDATE_HEALTH')[0]['Float_01']*1000))
        ability['Source']['Weapon'] = weapon.values
        ability['ExperiencePpm'] = round(spell['ExperienceModifier']*1_000_000)
        ability['Description'] = 'After 1.5 seconds, ignites enemy buildings in a 10 m area and burns enemy units for 18 damage per second for 5.5 seconds.'
    return ability


def visuals(output, resolver, trace):
    from healing_visual import export_sprite_effect
    result = {}
    for i in [145, 146, 253, 127, 328, 142, 143, 185, 186, 177, 178, 180, 182, 388, 176, 335]:
        data = export_sprite_effect(output, resolver, trace, resolver.db['Effects'].get(i).name, 'hero-effects/'+str(i))
        def normalized(name):
            return name.lower().replace(' ', '').replace('_', '')
        active = {normalized(e['name'][1:]) for e in data['Source']['Data']['particle_emitters']}
        data['Layers'] = [layer for layer in data['Layers'] if normalized(layer['Name']) in active
                          or normalized(layer['Name']).removeprefix('p') in active
                          or i == 182 and layer['Name'] == 'Pfire']
        if data['Layers']:
            data['DurationMs'] = max(data['DurationMs'], max(layer['DurationMs'] for layer in data['Layers']))
            result[str(i)] = data
    return result
