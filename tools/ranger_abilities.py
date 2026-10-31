"""Ranger techniques and innate Detection, traced to the original technique/spell graph."""
import math
from pathlib import PureWindowsPath

from hero_abilities import linked


DESCRIPTIONS = {
    'TECHNIQUE_CAMOUFLAGE': 'Hides the Ranger from enemies until he moves or attacks. Enemy detectors can reveal him.',
    'TECHNIQUE_EAGLE_EYE': 'Doubles sight and detection range from 20 m to 40 m.',
    'TECHNIQUE_DETECTION': 'Reveals invisible enemies within sight range. Cannot reveal the One Ring.'}


def export(resolver, unit, model, skeleton):
    db = resolver.db
    result = []
    for name, kind in (('TECHNIQUE_CAMOUFLAGE', 15), ('TECHNIQUE_EAGLE_EYE', 16), ('TECHNIQUE_DETECTION', 17)):
        technique = db['Techniques'].get(name)
        icon = db['UVAs'].get(technique['PortraitBorderless'])
        ability = dict(Id=-1000-technique.id, Slot=technique['SpecificIndexForUVA'],
                       Name=name.removeprefix('TECHNIQUE_').replace('_', ' ').title(),
                       Icon=PureWindowsPath(icon['Filename']).stem, Passive=kind != 15, Kind=kind,
                       Description=DESCRIPTIONS[name], Source={'Technique': technique.values})
        if not technique['IsInnate']:
            ability['RequiredTechnique'] = name
        if kind == 15:
            gear = db['BattleGear'].get(technique['AddBattleGear'])
            spell = db['Spells'].get(gear['SpellType'])
            events = linked(db['SpellAndEvents'], spell['FirstSpellAndEvent'], 'NextSpellAndEvent')
            actions = [a for e in events for a in linked(db['SpellAndEventAndActions'],
                       e['FirstSpellAndEventAndAction'], 'NextSpellAndEventAndAction')]
            animation = next(a for a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith('_PLAY_ANIMATION'))
            state = next(s for s in model['animation_states'] if s['id'] == animation['UnitAnimType'])
            choice = state['choices'][animation['UnitAnimVariant']-1]
            clip = next(c for c in skeleton.clips if c.name == choice['clip_name'])
            sounds = [a['SoundEventType'] for a in actions
                      if db['SpellActions'].get(a['SpellActionType']).name.endswith('_PLAY_SOUND_EVENT')]
            ability.update(Id=gear.id, RechargeTicks=math.ceil(spell['RechargeTime']*20),
                           CastClip=clip.name, CastTicks=math.ceil(clip.duration*20), AlphaPpm=500000, SoundEvents=sounds)
            ability['Source'].update(Gear=gear.values, Spell=spell.values,
                                    Events=[e.values for e in events], Actions=[a.values for a in actions],
                                    SoundEvents=sounds,
                                    Adapter='Idle variant is one-based. Friendly/detected camouflage uses 50% opacity; source SET_VISIBLE has no friendly alpha.')
        result.append(ability)
    return result
