"""Nature's Haven abilities from the shipped unit, gear, animation and spell records."""
import math
from pathlib import PureWindowsPath

from elven_abilities import graph

DESCRIPTIONS = {
    'TECHNIQUE_BEAR_FORM': 'Transforms between human and bear form. Bear form has 420 base health, 17 attack and 2 armor, and cannot heal allies.',
    'TECHNIQUE_SAVAGE_BLOW': 'Deals 75 damage to enemies within a 4 m, 260-degree arc in front of the bear and knocks them back. Knockback-immune units still take damage.',
    'TECHNIQUE_TREEFORM': 'Roots the Huorn for 8 seconds, restoring 20 health per second. Enemies within 10 m when it takes root are dazed for 8 seconds.'}


def animation(model, skeleton, state_id):
    state = next(s for s in model['animation_states'] if s['id'] == state_id)
    choice = next(c for c in state['choices'] if c['weight'] > 0)
    duration = next(c.duration for c in skeleton.clips if c.name == choice['clip_name'])
    return choice['clip_name'], math.ceil(duration*20), duration, [model['events'][i] for i in choice['event_indices']]


def export(resolver, unit, model, skeleton):
    db = resolver.db
    result = []
    gears = {'UNIT_G_BEORNING': [(32,23,'Bear Form','TECHNIQUE_BEAR_FORM')],
             'UNIT_G_BEAR': [(31,23,'Human Form',None),(28,25,'Savage Blow','TECHNIQUE_SAVAGE_BLOW')],
             'UNIT_G_HUORN': [(33,26,'Treeform','TECHNIQUE_TREEFORM')]}
    for gear_id, kind, name, technique in gears[unit.name]:
        gear = db['BattleGear'].get(gear_id)
        spell = db['Spells'].get(gear['SpellType'])
        actions = graph(db, spell)
        def typed(suffix):
            return [a for e,a in actions if db['SpellActions'].get(a['SpellActionType']).name.endswith(suffix)]
        clip,ticks,duration,events = animation(model,skeleton,typed('_PLAY_ANIMATION')[0]['UnitAnimType'])
        tool_types = {e['ToolscriptType'] for e,a in actions if e['ToolscriptType'] >= 0}
        release = next((e['time'] for e in events if e['type'] in tool_types),0)
        sounds = [a['SoundEventType'] for a in typed('_PLAY_SOUND_EVENT')] + [e['sound'] for e in events if e.get('name') == 'sound']
        ability = dict(Id=gear.id,Slot=gear['SpecificIndex'],Name=name,
                       Icon=PureWindowsPath(db['UVAs'].get(gear['PortraitBorderless'])['Filename']).stem,
                       Kind=kind,Passive=False,RechargeTicks=math.ceil(spell['RechargeTime']*20),RangeMm=10000,
                       CastClip=clip,CastTicks=ticks,EffectTick=min(ticks-1,math.ceil(duration*release*20)),
                       SoundEvents=sounds,Description=DESCRIPTIONS.get(technique, 'Returns to human form, allowing the Beorning to heal wounded allies.'),
                       Source={'Gear':gear.values,'Spell':spell.values,'SoundEvents':sounds,
                               'Actions':[{'Event':e.name,'Type':db['SpellActions'].get(a['SpellActionType']).name,'Values':a.values} for e,a in actions]})
        if technique:
            ability['RequiredTechnique'] = technique
        if kind == 23:
            ability['MorphUnit'] = db['Units'].get(typed('_APPLY_MORPH')[0]['MorphUnitType']).name
            # APPLY_MORPH is on animation-finished, after the transformation particles.
        elif kind == 25:
            damage = typed('_APPLY_DAMAGE')[0]
            knock = typed('_APPLY_KNOCKBACK')[0]
            ability.update(HealMilli=damage['DamageAmount']*1000,AreaRadiusMm=round(damage['OccuranceRadius']*1000),
                           ArcDegrees=round(damage['OccuranceRadiusFacingAngle']),KnockbackSpeedMm=knock['KnockbackForce']*1000,
                           KnockbackAngle=round(knock['KnockbackAngle']))
        else:
            loop,_,_,_ = animation(model,skeleton,91)
            exit_clip,exit_ticks,_,exit_events = animation(model,skeleton,92)
            aura = next(a for a in typed('_SPAWN_CHILD_SPELL') if a['OccursAtAllUnitsInRadius'])
            ability.update(DurationTicks=math.ceil(typed('_START_TIMER')[-1]['Float_01']*20),
                           AreaRadiusMm=round(aura['OccuranceRadius']*1000),DpsMilli=round(typed('_HEAL')[0]['Float_01']*1000),
                           LoopClip=loop,ExitClip=exit_clip,ExitTicks=exit_ticks,
                           EndSoundEvents=[e['sound'] for e in exit_events if e.get('name')=='sound'])
            ability['Source']['SoundEvents'] += ability['EndSoundEvents']
            ability['SoundEvents'] = [e['sound'] for e in events if e.get('name')=='sound']
        result.append(ability)
    if unit.name == 'UNIT_G_BEORNING':
        gear = db['BattleGear'].get(4)
        clip,ticks,duration,events = animation(model,skeleton,91)
        release = next(e for e in events if e['type']==29)
        result.append(dict(Id=gear.id,Slot=gear['SpecificIndex'],Name='Healing Herbs',Kind=24,Passive=False,
                           Icon=PureWindowsPath(db['UVAs'].get(gear['PortraitBorderless'])['Filename']).stem,
                           Cursor='cursor_g_kingsfoil',HealMilli=round(unit['HealAmount']*1000),
                           RechargeTicks=math.ceil(unit['TimeBetweenHealing']*20),RangeMm=round(unit['MaxHealRange']*1000),
                           ResponseRangeMm=round(unit['HealResponseRange']*1000),CastClip=clip,CastTicks=ticks,
                           EffectTick=min(ticks-1,math.ceil(duration*release['time']*20)),
                           Description='Heals a wounded ally for 75 health every 6 seconds. Idle Beornings automatically tend nearby allies.',
                           Source={'Unit':unit.values,'Gear':gear.values,'InnateTechnique':db['Techniques'].get('TECHNIQUE_HEALING_HERBS').values}))
    return sorted(result,key=lambda a:a['Slot'])


def visuals(output,resolver,trace):
    from healing_visual import export_sprite_effect
    result = {}
    for i in [134,190,363]:
        data = export_sprite_effect(output,resolver,trace,resolver.db['Effects'].get(i).name,'nature-effects/'+str(i))
        # Shared LFX definitions contain unused tracks. Bind the named emitters only;
        # e.g. the 13 m white 'ground' sprite is not emitted by Entangling Roots.
        def emitter_name(name):
            name = name.lower().removeprefix('e_')
            return name[1:] if name.startswith('e') else name[:-1] if name.endswith('e') else name
        active = {emitter_name(e['name']) for e in data['Source']['Data']['particle_emitters']}
        data['Layers'] = [l for l in data['Layers'] if l['Name'].lower() in active]
        if i == 363:
            from lms2gltf import convert_model
            track = next(t for t in data['Source']['Data']['texture_tracks'] if t['name']=='shard_one')
            mesh = resolver.db['Models'].get(track['keys'][0]['texture'])
            filename = 'combat/nature-effects/roots.glb'
            convert_model(resolver,mesh.name,str(output/filename))
            data['RootModel'] = filename
        result[str(i)] = data
    return result
