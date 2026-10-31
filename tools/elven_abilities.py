"""Sanctuary abilities, traced through the shipped technique and spell graphs."""
import math
from pathlib import PureWindowsPath
from hero_abilities import linked

DESCRIPTIONS = {
    'TECHNIQUE_EAGLE_FLETCHING': 'Increases Elven Archer missile range by 20%.',
    'TECHNIQUE_ELVEN_CLOAKS': 'Makes Elven Archers invisible to enemies, including while moving and fighting. Enemy detectors can reveal them.',
    'TECHNIQUE_NEGATE_MAGIC': 'Dispels harmful magic from allies and beneficial magic from enemies in a 7 m radius.',
    'TECHNIQUE_LIGHT_OF_LOTHLORIEN': 'Sears enemy units in a 5 m radius for 22 damage per second for 5 seconds.'}


def graph(db, spell, seen=None):
    seen = set() if seen is None else seen
    if spell.id in seen:
        return []
    seen.add(spell.id)
    result = []
    for event in linked(db['SpellAndEvents'], spell['FirstSpellAndEvent'], 'NextSpellAndEvent'):
        for action in linked(db['SpellAndEventAndActions'], event['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction'):
            result.append((event, action))
            if action['SpellType'] >= 0:
                result.extend(graph(db, db['Spells'].get(action['SpellType']), seen))
    return result


def export(resolver, unit, model, skeleton):
    db = resolver.db
    if unit.name == 'UNIT_G_ELVEN_ARCHER':
        tech = db['Techniques'].get('TECHNIQUE_ELVEN_CLOAKS')
        cloak = dict(Id=-1000-tech.id, Slot=tech['SpecificIndexForUVA'], Name='Elven Cloaks',
                     Icon=PureWindowsPath(db['UVAs'].get(tech['PortraitBorderless'])['Filename']).stem,
                     Passive=True, Kind=18, AlphaPpm=500000, RequiredTechnique=tech.name,
                     Description=DESCRIPTIONS[tech.name], Source={'Technique': tech.values,
                     'Adapter': 'IsVisible=0 applies continuously; no movement/attack reveal rule in this technique. Friendly alpha is an adapter.'})
        fletching = db['Techniques'].get('TECHNIQUE_EAGLE_FLETCHING')
        return [cloak, dict(Id=-1000-fletching.id, Slot=fletching['SpecificIndexForUVA'], Name='Eagle Fletching',
                           Icon=PureWindowsPath(db['UVAs'].get(fletching['PortraitBorderless'])['Filename']).stem,
                           Passive=True, Kind=22, RequiredTechnique=fletching.name,
                           BonusScale=round(fletching['MissileWeaponRangeModifier']*1_000_000),
                           Description=DESCRIPTIONS[fletching.name], Source={'Technique': fletching.values})]
    result = []
    for gear_name, kind, name, technique in [
            ('BATTLE_GEAR_ELVEN_PROTECTION', 19, 'Protection', None),
            ('BATTLE_GEAR_ELVEN_NEGATE_MAGIC', 20, 'Negate Magic', 'TECHNIQUE_NEGATE_MAGIC'),
            ('BATTLE_GEAR_ELVEN_LIGHT_OF_LOTHLORIEN', 21, 'Light of Lothlórien', 'TECHNIQUE_LIGHT_OF_LOTHLORIEN')]:
        gear = db['BattleGear'].get(gear_name)
        spell = db['Spells'].get(gear['SpellType'])
        target = db['SpellValidTargets'].get(spell['ValidTargetType'])
        actions = graph(db, spell)
        def typed(suffix):
            return [a for e, a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix)]
        animation = typed('_PLAY_ANIMATION')[0]
        state = next(s for s in model['animation_states'] if s['id'] == animation['UnitAnimType'])
        choice = next(c for c in state['choices'] if c['weight'] > 0)
        clip = next(c for c in skeleton.clips if c.name == choice['clip_name'])
        tool = next(e['ToolscriptType'] for e, a in actions if e['ToolscriptType'] >= 0)
        release = next(model['events'][i] for i in choice['event_indices'] if model['events'][i]['type'] == tool)
        sounds = [model['events'][i]['sound'] for i in choice['event_indices'] if model['events'][i].get('name') == 'sound']
        end_sounds = [a['SoundEventType'] for a in typed('_PLAY_SOUND_EVENT')]
        cast_ticks = math.ceil(clip.duration*20)
        ability = dict(Id=gear.id, Slot=gear['SpecificIndex'], Name=name,
                       Cursor=PureWindowsPath(db['Cursors'].get(spell['TargetCursor'])['Filename']).stem,
                       Icon=PureWindowsPath(db['UVAs'].get(gear['PortraitBorderless'])['Filename']).stem,
                       Passive=False, Kind=kind, RangeMm=round(target['MaxSpecifiedRange']*1000),
                       AreaRadiusMm=round(spell['MaxAreaOfEffectRange']*1000),
                       RechargeTicks=math.ceil(spell['RechargeTime']*20), CastClip=clip.name,
                       CastTicks=cast_ticks, EffectTick=min(cast_ticks-1, math.ceil(clip.duration*release['time']*20)),
                       SoundEvents=sounds, EndSoundEvents=end_sounds,
                       Description=DESCRIPTIONS.get(technique, 'Absorbs up to 150 damage on a friendly unit for 50 seconds. Recasting refreshes the shield.'),
                       Source={'Gear': gear.values, 'Spell': spell.values, 'Target': target.values,
                               'SoundEvents': sorted(set(sounds+end_sounds)),
                               'Actions': [{'Event': e.name, 'Type': db['SpellActions'].get(a['SpellActionType']).name, 'Values': a.values} for e, a in actions]})
        if technique:
            ability['RequiredTechnique'] = technique
        if kind == 19:
            ability.update(ShieldMilli=round(typed('_ADD_BONUS_HEALTH')[0]['Float_01']*1000),
                           DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20))
        if kind == 21:
            ability.update(DpsMilli=round(-typed('_UPDATE_HEALTH')[0]['Float_01']*1000),
                           DurationTicks=math.ceil(typed('_START_TIMER')[0]['Float_01']*20))
        result.append(ability)
    return result


def visuals(output, resolver, trace):
    from healing_visual import export_sprite_effect
    result = {}
    for i in [350,349,308,75,402,106,104]:
        data = export_sprite_effect(output, resolver, trace, resolver.db['Effects'].get(i).name, 'elven-effects/'+str(i))
        # Effects 75 and 402 share unused layer definitions but bind different emitters.
        active = {e['name'].removeprefix('E_').lower() for e in data['Source']['Data']['particle_emitters']}
        data['Layers'] = [layer for layer in data['Layers'] if layer['Name'].lower() in active]
        if not data['Layers']:
            raise ValueError('Elven effect has no bound sprite layers: '+str(i))
        data['DurationMs'] = max(data['DurationMs'], max(layer['DurationMs'] for layer in data['Layers']))
        result[str(i)] = data
    return result
