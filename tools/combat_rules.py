"""Traced melee/ranged rules adapter for the four Godot demo types.

Source data is read-only. Runtime numbers are integers. See docs/COMBAT.md
for the explicitly approximate armor/range policies and unsupported rules.
"""
from decimal import Decimal, ROUND_HALF_UP, ROUND_CEILING
import hashlib
import json
import math
import struct
from pathlib import Path

import lsk
import lmd
import lpo
import lfx
from resolve_model import Resolver
import prototype_assets
import hero_abilities
import hero_experience
import healing_visual
import elven_abilities
import nature_abilities
from pcm_audio import decode_ms_adpcm

ROOT = Path(__file__).resolve().parent.parent
# The locomotion root joint: J_Position, or J_Positional on the Evil trolls.
ROOT_JOINTS = ('jposition', 'jpositional')
UNITS = ['UNIT_G_HERO_ARAGORN', 'UNIT_G_ELF_KNIGHT', 'UNIT_E_URUK_HAI', 'UNIT_G_ELVEN_ARCHER']
# Units using their source missile weapon, with source melee attacks at close range.
RANGED_UNITS = ['UNIT_G_ELVEN_ARCHER', 'UNIT_G_DWARVEN_AXETHROWER', 'UNIT_G_ELVEN_LIGHTBRINGER', 'UNIT_G_HERO_LEGOLAS', 'UNIT_G_GIANT_ENT',
                # Build-133, the Evil side: the Orc Bowman, the Troll Stonehurler (boulders) and Saleme, who uses the Bowman's bow.
                'UNIT_E_ORC_BOWMAN', 'UNIT_E_URUK_HAI', 'UNIT_E_TROLL_STONEHURLER', 'UNIT_E_HERO_QAMARA']


def type_matchups(damage_types):
    """User-specified percentages; cycle confirmed by the shipped addendum p7.

    Same types and hero targets are neutral. Unknown types are left to the
    runtime's neutral fallback rather than guessed from a unit/weapon name.
    """
    ids = {name: damage_types.get('DAMAGE_TYPE_' + name).id
           for name in ['CUTTING', 'PIERCING', 'BLUNT', 'HERO']}
    strong = {(ids['CUTTING'], ids['PIERCING']), (ids['PIERCING'], ids['BLUNT']),
              (ids['BLUNT'], ids['CUTTING'])}
    return [{'From': a, 'To': b, 'Scale': 1_250_000 if (a, b) in strong
             else 750_000 if (b, a) in strong else 1_000_000}
            for a in ids.values() for b in ids.values()]


def quantize(value, scale=1000):
    if not math.isfinite(value):
        raise ValueError('non-finite rules value')
    return int((Decimal(str(value))*scale).to_integral_value(rounding=ROUND_HALF_UP))


def trail_profile(track):
    """Narrow arrow-trail adapter; preserve the full source effect separately."""
    keys = sorted(track['keys'],key=lambda k: k['time'])
    if not keys or not 0 < track['duration_ms'] <= 5000:
        raise ValueError('invalid arrow trail')
    def width(key):
        value = struct.unpack('<f',struct.pack('<I',key['words'][3]))[0]
        if not math.isfinite(value) or not 0 <= value <= 1:
            raise ValueError('invalid arrow trail width')
        return quantize(value)
    return {'LifetimeMs': track['duration_ms'],'WidthMm': width(keys[0]),
            'EndWidthMm': width(keys[-1]),'StartColorArgb': keys[0]['words'][5],
            'EndColorArgb': keys[-1]['words'][5], 'GrayTintPpm': 700000}


def export_sounds(output, resolver, event_ids, trace, slots=3):
    sounds = []
    sound_dir = output/'combat/sounds'; sound_dir.mkdir(parents=True,exist_ok=True)
    for event_id in sorted(event_ids):
        event = resolver.db['SoundEvents'].get(event_id)
        for slot in range(1,slots+1):
            effect = resolver.db['SoundEffects'].get(event['SoundEffect%d' % slot])
            if effect is None: continue
            path = resolver.index.find('SoundEffects',effect['Filename'])
            if path is None: raise ValueError('missing combat sound: ' + effect.name)
            trace(path); filename = str(effect.id)+'.wav'
            (sound_dir/filename).write_bytes(decode_ms_adpcm(Path(path).read_bytes()))
            sounds.append({'File': 'combat/sounds/'+filename,'EventId': event.id,'EffectId': effect.id,
                'EventName': event.name,'EffectName': effect.name,'VolumePpm': quantize(event['MinVolume'],1_000_000),
                'MaxSimultaneous': event['MaxSimultaneous'],'Conversion': 'mono Microsoft ADPCM to 16-bit PCM'})
    return sounds


def attack_choice(choice, events, duration, event_name='melee_hit'):
    if not math.isfinite(duration) or duration <= 0 or choice['weight'] <= 0:
        raise ValueError('invalid melee clip')
    seconds = Decimal(str(duration))
    ticks = int((seconds*20).to_integral_value(rounding=ROUND_CEILING))
    hits = []
    for index in choice['event_indices']:
        event = events[index]
        if event.get('name') != event_name:
            continue
        time, scale = event['time'], event.get('damage_scale', 1) if event_name == 'missile_release' else event['damage_scale']
        if not math.isfinite(time) or not 0 <= time <= 1 or not math.isfinite(scale) or scale <= 0:
            raise ValueError('invalid melee event')
        tick = int((seconds*Decimal(str(time))*20).to_integral_value(rounding=ROUND_CEILING))
        hits.append({'Tick': min(ticks-1,tick), 'Scale': quantize(scale,1_000_000),
                     'SourceEvent': index, 'SourceFraction': time, 'Joint': event.get('joint_name')})
    if not hits:
        raise ValueError('melee choice has no hit event')
    hits.sort(key=lambda h: (h['Tick'],h['SourceEvent']))
    return {'Clip': choice['clip_name'], 'Weight': choice['weight'], 'DurationTicks': ticks,
            'SourceDurationSeconds': duration, 'Hits': hits}


def locomotion(model, skeleton):
    """First positive state choice, matching the prototype's existing run clip.

    LSK uses XY ground / Z up. AIApproximateRunVelocity is not reliable for
    this purpose (Aragorn's value matches his walk clip in this installation).
    """
    joint = next(i for i, j in enumerate(skeleton.joints)
                 if j.name.replace('_', '').replace(' ', '').lower() in ROOT_JOINTS)
    result = {}
    for mode in ['Walk', 'Run']:
        # The Balrog has a Walk state and no Run state: it runs at its walking pace (the walk/run ratio is then 1).
        state = next((s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_' + mode.upper()), None)
        if state is None and mode == 'Run':
            result[mode] = dict(result['Walk'])
            continue
        state = state or next(s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_' + mode.upper())
        choice = next(c for c in state['choices'] if c['weight'] > 0)
        clip = next(c for c in skeleton.clips if c.name == choice['clip_name'])
        track = clip.tracks[joint]
        if not track.positions or not math.isfinite(clip.duration) or clip.duration <= 0:
            raise ValueError('invalid locomotion clip')
        first, last = track.positions[0], track.positions[-1]
        speed = math.hypot(last[0]-first[0], last[1]-first[1])/clip.duration
        if not math.isfinite(speed) or speed <= 0:
            raise ValueError('locomotion clip has no forward movement')
        result[mode] = {'Clip': clip.name, 'StateId': state['id'], 'Joint': skeleton.joints[joint].name,
                        'DurationSeconds': clip.duration, 'SourceSpeed': speed}
    scale = quantize(result['Walk']['SourceSpeed']/result['Run']['SourceSpeed'], 1_000_000)
    if not 0 < scale <= 1_000_000:
        raise ValueError('walk/run ratio outside supported range')
    return result, scale


# Run speeds keep the source ratios, scaled so this unit runs at the accepted 3 m/s (build-60).
RUN_REFERENCE = 'UNIT_G_GONDOR_SWORDSMAN'


def run_scale(motion, reference_speed):
    """A unit's run clip speed relative to the reference runner's, in parts per million."""
    scale = quantize(motion['Run']['SourceSpeed']/reference_speed, 1_000_000)
    if not 0 < scale <= 3_000_000:
        raise ValueError('run speed outside supported range')
    return scale


def collision_profile(model, skeleton, boxes):
    main = [b for b in boxes if b['layer'] == 0]
    if len(main) != 1:
        raise ValueError('expected one main collision box')
    box = main[0]; v = box['vectors']; scale = model['scale']
    if (box['flag'] != 0 or any(not math.isfinite(x) for x in v+scale)
            or min(v[:3]+scale) <= 0 or any(abs(a-b) > 1e-5 for a,b in zip(v[8:],[1,0,0,0]))):
        raise ValueError('unsupported main collision box')
    joint = skeleton.joints[box['joint']].name if box['joint'] >= 0 else None
    if joint and joint.replace('_','').replace(' ','').lower() not in ROOT_JOINTS:
        raise ValueError('main collision must use the locomotion root')
    radius = math.ceil(max((v[0]+abs(v[4]))*scale[0],(v[1]+abs(v[5]))*scale[1])*1000)
    height = math.ceil(max(2*v[2],v[6]+v[2])*scale[2]*1000)
    return radius, height, {'Layer': 'BBOX_LAYER_MAIN','Box': box,'Joint': joint,'ModelScale': scale,
        'Policy': 'Upright root-centered cylinder; max scaled XY half-extent and full Z height. Root locomotion removed; original response semantics unverified.'}


def demo_body_tuning(name, radius, height):
    """Owner-requested feel adjustments, separate from preserved source bounds."""
    scale = 1_120_000 if name == 'UNIT_G_HERO_ARAGORN' else 1_000_000
    return {'BodyRadiusMm': max(800, math.ceil(radius*scale/1_000_000)) if scale != 1_000_000 else radius,
            'BodyHeightMm': math.ceil(height*scale/1_000_000),
            'ModelScalePpm': scale, 'FriendlyCompressionPpm': 500_000}


# Build-139: Saleme's BATTLE_GEAR_QAMARA_MYSTIC_BLADES is innate (not purchasable) and its only action, SWITCH_MISSILE_WEAPON, swaps her
# bow (the Orc Bowman's weapon in her unit record) for weapon 64 at once, so her missile stats and projectile come from that weapon.
INNATE_MISSILE_WEAPONS = {'UNIT_E_HERO_QAMARA': 64}


def missile_weapon_id(resolver, unit_name):
    return INNATE_MISSILE_WEAPONS.get(unit_name, resolver.db['Units'].get(unit_name)['MissileWeapon'])


def projectile_presentation(output, resolver, unit_name, trace, projectile_id=None, include_sprite_effects=False):
    """Source projectile model, launch sounds and trail of one ranged unit's missile weapon, or of a given projectile."""
    from lms2gltf import convert_model, to_gltf_axes
    if projectile_id is None:
        projectile_id = resolver.db['Weapons'].get(missile_weapon_id(resolver, unit_name))['AssociatedProjectile']
    record = resolver.db['Projectiles'].get(projectile_id)
    path = Path(resolver.index.find('Projectiles',record['Filename']))
    projectile = lpo.parse(path.read_bytes())
    launch_sounds = export_sounds(output,resolver,lpo.launch_sound_events(projectile),trace,slots=6)
    # The first bound effect with a trail track draws the ribbon; the Lightbearer's bolt (effect 167) has none.
    trail, sprite_effects = None, []
    for effect_id in dict.fromkeys(b['source'] for key in projectile['resource_keys'] for b in key['bindings'] if b['source'] >= 0):
        effect_record = resolver.db['Effects'].get(effect_id)
        effect_path = Path(resolver.index.find('Effects',effect_record['Filename']))
        effect = lfx.parse(effect_path.read_bytes())
        trace(effect_path)
        if include_sprite_effects and effect['texture_tracks']:
            sprite_effects.append(healing_visual.export_sprite_effect(output,resolver,trace,effect_record.name,
                'projectile-effects/'+effect_record.name.lower()))
        if trail is None and effect['trail_tracks']:
            trail = trail_profile(effect['trail_tracks'][0])
            trail['Source'] = {'Id': effect_id,'Name': effect_record.name,'File': str(effect_path.relative_to(ROOT)), 'Data': effect}
    trace(resolver.db.files['effects'])
    model_id = next((k['id'] for k in projectile['resource_keys'] if k['kind'] == 0 and k['id'] >= 0), None)
    visual = 'combat/' + record.name.lower() + '.glb'
    try:
        if model_id is None:
            visual = None  # Sprite/trail-only projectiles, including the Haradrim poison dart.
        else:
            convert_model(resolver,str(model_id),str(output/visual),extras={'wotrProjectile':projectile})
    except ValueError:
        # The Lightbearer's bolt model holds no mesh (its look is the particle effect); the game draws a stand-in.
        visual = None
    trace(path); trace(resolver.db.files['projectiles'])
    if model_id is not None:
        trace(ROOT/resolver.resolve(str(model_id),recurse=False)['file'])
    spin = lpo.spin_parameters(projectile) if unit_name == 'UNIT_G_DWARVEN_AXETHROWER' else None
    spin = {'AxisPpm': [round(v*1_000_000) for v in to_gltf_axes(spin['Axis'])],
            'MilliDegreesPerSecond': round(spin['DegreesPerSecond']*1000)} if spin else None
    weapon = resolver.db['Weapons'].get(missile_weapon_id(resolver, unit_name)) if unit_name else None
    # Build-131: a missile meeting a composition its impact type does not list (an Elven arrow in a wooden
    # building) plays the impact type's default row.
    import blood_effects
    impact_sounds = {str(c): export_sounds(output,resolver,[row['SoundEvent']],trace)
                     for c, row in (blood_effects.weapon_effect_rows(resolver,weapon['WeaponImpactType']) if weapon else {}).items()
                     if row['SoundEvent'] >= 0}
    return {'Visual': visual,'LaunchSounds': launch_sounds,'ImpactSounds': impact_sounds,'Trail': trail,'SpriteEffects': sprite_effects,'Spin': spin,
            'Source': {'Id': record.id,'Name': record.name,'Data': projectile}}


def state_clip(model, state_id, durations):
    """The first positive choice of a source animation state: its clip and whole ticks, or ('', 0) without one."""
    clip = next((c['clip_name'] for s in model['animation_states'] if s['id'] == state_id
                 for c in s['choices'] if c['weight'] > 0), '')
    return clip, math.ceil(durations[clip]*20) if clip in durations else 0


# Our unit states and the source animation state whose regeneration rate each takes (build-52).
REGENERATION_STATES = {'Idle': 'UNIT_ANIM_STATE_IDLE', 'Walk': 'UNIT_ANIM_STATE_WALK', 'Run': 'UNIT_ANIM_STATE_RUN',
                       'Ready': 'UNIT_ANIM_STATE_ATTACK_READY', 'Attack': 'UNIT_ANIM_STATE_ATTACK_MELEE_01',
                       'Gather': 'UNIT_ANIM_STATE_RESOURCE_ORE_GATHER', 'Repair': 'UNIT_ANIM_STATE_REPAIR',
                       'Special': 'UNIT_ANIM_STATE_SPECIAL_01'}


def regeneration(states):
    """Source health regeneration by unit state, in milli-HP per second for ordinary units and for heroes: the
    UnitAnimStates HealthRegenerationRate and HealthRegenerationRateHero columns, read as HP per second (the place of
    power modifier that raises them is described as "the HP/second health regen rate")."""
    rates = {}
    for key, name in REGENERATION_STATES.items():
        record = states.get(name)
        rates[key+'Milli'] = quantize(record['HealthRegenerationRate'])
        rates[key+'HeroMilli'] = quantize(record['HealthRegenerationRateHero'])
    return rates


def export(output, resolver=None, roster=None):
    output = Path(output).resolve()
    if any(output.is_relative_to(ROOT/p) for p in ['extracted','TLOTR - War of the Ring']):
        raise ValueError('rules cache must be outside original and extracted data')
    resolver = resolver or Resolver()
    roster = list(roster or UNITS)
    sources = {}
    def trace(path):
        path = Path(path).resolve()
        relative = path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else path.name
        sources[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    reference_model = resolver.resolve(RUN_REFERENCE,recurse=False)
    reference_skeleton = ROOT/reference_model['skeletons'][0]['file']
    reference_speed = locomotion(reference_model,lsk.parse(reference_skeleton.read_bytes()))[0]['Run']['SourceSpeed']
    trace(ROOT/reference_model['file']); trace(reference_skeleton)
    units = []
    for name in roster:
        unit = resolver.db['Units'].get(name)
        ranged = name in RANGED_UNITS
        weapon = resolver.db['Weapons'].get(missile_weapon_id(resolver, name) if ranged else unit['MeleeWeapon'])
        model = resolver.resolve(name,recurse=False)
        skeleton_path = ROOT/model['skeletons'][0]['file']
        skeleton = lsk.parse(skeleton_path.read_bytes())
        motion, walk_scale = locomotion(model, skeleton)
        model_record = resolver.db['Models'].get(unit['Model'])
        collision_model = dict(model,scale=[model_record[k] for k in ['ScaleX','ScaleY','ScaleZ']])
        radius, body_height, body_source = collision_profile(collision_model,skeleton,lmd.parse((ROOT/model['file']).read_bytes()).bounding_boxes)
        body_source['SourceRadiusMm'] = radius
        body_source['SourceHeightMm'] = body_height
        trace(ROOT/model['file']); trace(skeleton_path)
        state = next(s for s in model['animation_states'] if s['name'] == ('UNIT_ANIM_STATE_ATTACK_MISSILE_01' if ranged else 'UNIT_ANIM_STATE_ATTACK_MELEE_01'))
        durations = {clip.name: clip.duration for clip in skeleton.clips}
        # The Axethrower's second throw releases through an undecoded event type (33, right-hand joint);
        # only clips with a decoded release are used, and the rest are recorded.
        released = [c for c in state['choices'] if c['weight'] > 0
                    and (not ranged or any(model['events'][i].get('name') == 'missile_release' for i in c['event_indices']))]
        skipped = [c['clip_name'] for c in state['choices'] if c['weight'] > 0 and c not in released]
        choices = [attack_choice(c,model['events'],durations[c['clip_name']], 'missile_release' if ranged else 'melee_hit') for c in released]
        melee_weapon = resolver.db['Weapons'].get(unit['MeleeWeapon'])
        melee_state = next(s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_ATTACK_MELEE_01')
        if ranged:
            choices += [dict(attack_choice(c,model['events'],durations[c['clip_name']]),Melee=True)
                        for c in melee_state['choices'] if c['weight'] > 0]
        ready = next((s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_ATTACK_READY'),
                     next(s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_IDLE'))
        ready_clip = next(c['clip_name'] for c in ready['choices'] if c['weight'] > 0)
        damage_type = resolver.db['DamageTypes'].get(unit['DamageType'])
        units.append({'Id': unit.id,'Name': name,'DisplayName': unit['Name'],'MaxHealth': unit['MaxHealth'],
            'StartingHealthPercent': unit['StartingHealthPct'],'ArmorMilli': quantize(unit['ArmorPoints']),
            'RegenerationPpm': quantize(unit['HealthRegenerationMultiplier'],1_000_000),
            'DamageType': damage_type.id,'DamageTypeName': damage_type.name,'MaxAttackers': unit['MaxUnitsAttacking'],
            'Composition': unit['CompositionType'],'WeaponId': weapon.id,'WeaponName': weapon.name,'Hero': bool(unit['IsHero']),
            'Experience': hero_experience.export(unit),
            'WeaponExperiencePpm': quantize(weapon['ExperienceModifier'],1_000_000),
            'MeleeWeaponExperiencePpm': quantize(melee_weapon['ExperienceModifier'],1_000_000),
            'DpsMilli': quantize(weapon['CuttingDamagePerSecond']), 'SourceRangeMm': quantize(weapon['MaximumRange']),
            'ReachMm': quantize(weapon['MaximumRange']) if ranged else 2000,'ReadyClip': ready_clip,'Attacks': choices,
            'MinimumRangeMm': quantize(weapon['MinimumRange']) if ranged else 0,
            'ProjectileSpeedMmPerSecond': 25000 if ranged else 0,
            'MeleeDpsMilli': quantize(melee_weapon['CuttingDamagePerSecond']) if ranged else 0,
            'MeleeReachMm': 2000,'MeleeGapMm': quantize(melee_weapon['MaximumRange']) if ranged else 0,
            'WalkScale': walk_scale,'RunScale': run_scale(motion,reference_speed),
            'WalkClip': motion['Walk']['Clip'],'RunClip': motion['Run']['Clip'],
            'BodyRadiusMm': radius,'BodyHeightMm': body_height,'SelectionRadiusMm': quantize(unit['SelectionRingRadius']),
            'Abilities': hero_abilities.export(resolver,unit,model,skeleton),
            'SightRangeMm': quantize(unit['SightRange']),
            'KnockbackImmune': bool(unit['ImmuneToKnockback']),
            'NativeImmunities': sum((1 << i) for i, key in enumerate(['IsImmuneToPoison','IsImmuneToStun','IsImmuneToBlind','IsImmuneToSlow']) if unit[key]),
            'DazedClip': next((c['clip_name'] for s in model['animation_states'] if s['id'] == 25 for c in s['choices'] if c['weight'] > 0), ''),
            'KnockbackClip': next((c['clip_name'] for s in model['animation_states'] if s['id'] == 37 for c in s['choices'] if c['weight'] > 0), ''),
            # Build-129: a knocked-back unit plays Knocked Back at its own pace, then Get Up (state 34) before it acts.
            'KnockbackTicks': state_clip(model,37,durations)[1],
            'GetUpClip': state_clip(model,34,durations)[0],'GetUpTicks': state_clip(model,34,durations)[1],
            # Build-130: a CanCriticallyKill weapon's killing blow bursts a CanBeCriticallyKilled unit into gore.
            'CriticallyKillable': bool(unit['CanBeCriticallyKilled']),'CriticalWeapon': bool(weapon['CanCriticallyKill']),
            'CriticalMeleeWeapon': bool(melee_weapon['CanCriticallyKill']),
            'DetectionMask': sum(1 << detector for detector in {unit['DetectorType1'], unit['DetectorType2']} if detector >= 0),
            # Source IsSiege weapons (the Huorn's) multiply their damage against buildings by SiegeMultiplier.
            'SiegePpm': quantize(weapon['SiegeMultiplier'],1_000_000) if weapon['IsSiege'] else 1_000_000,
            # Build-171: the main weapon's AreaOfEffectRange / AreaOfEffectDamage (the Balrog's Firesword 6 m / 25, the Giant
            # Ent's boulder 5 m / 15), read as a per-second rate like CuttingDamagePerSecond, and a melee weapon's fire input
            # (the Firesword's 2 a second) for the buildings it strikes.
            'SplashRadiusMm': quantize(weapon['AreaOfEffectRange']),'SplashDpsMilli': quantize(weapon['AreaOfEffectDamage']),
            'FirePpmPerSecond': 0 if ranged else quantize(melee_weapon['FireDamagePerSecond'],1_000_000),
            # Build-173: Rings.exe's garrison test (0x51bdfc) reads this flag, not the GarrisonProfiles lists.
            'CanGarrison': bool(unit['CanEnterWatchtower']),
            **demo_body_tuning(name, radius, body_height),
            'AttackGapMm': 0 if ranged else quantize(weapon['MaximumRange']),
            'Source': {'Unit': unit.name,'ModelId': model['id'],'ModelFile': model['file'],
                       'StateId': state['id'],'SkippedAttackClips': skipped,'Weapon': weapon.values,'Locomotion': motion,'Collision': body_source,
                       'MeleeWeaponId': melee_weapon.id,'MeleeWeaponName': melee_weapon.name,'MeleeWeapon': melee_weapon.values,'MeleeStateId': melee_state['id'],
                       'MaxTurnAngleDegrees': unit['MaxTurnAngle']}})
    ids = {u['Id'] for u in units}
    # The source repeats a few pairs (Lightbearer to Lightbearer twice, one row named ..._TO_ROHAN_RIDER); the repeats
    # carry the same multiplier, so the first row of each pair stands.
    pairs, seen = [], set()
    for p in resolver.db['UnitToUnitModifiers'].records:
        if p['FromUnit'] in ids and p['ToUnit'] in ids and (p['FromUnit'], p['ToUnit']) not in seen:
            seen.add((p['FromUnit'], p['ToUnit']))
            pairs.append({'From': p['FromUnit'],'To': p['ToUnit'],'Scale': quantize(p['DamageMultiplier'],1_000_000),
                          'Invincible': bool(p['IsInvinicible']),'SourceId': p.id,'SourceName': p.name})
    sound_event_ids = set()
    for unit in units:
        impact = unit['Source']['Weapon']['WeaponImpactType']
        for victim in units:
            effect = next((r for r in resolver.db['WeaponEffects'].records
                           if r['WeaponImpactType'] == impact and r['CompositionType'] == victim['Composition']),None)
            if effect and effect['SoundEvent'] >= 0:
                sound_event_ids.add(effect['SoundEvent'])
    sounds = export_sounds(output,resolver,sound_event_ids,trace)
    import global_powers
    powers = global_powers.export(resolver)
    ability_sound_ids = {event for unit in units for ability in unit['Abilities'] for event in ability['Source'].get('SoundEvents',[])}
    if any(unit['Hero'] for unit in units): ability_sound_ids.add(412)
    ability_sound_ids.update(event for power in powers for event in power['SoundEvents'])
    import places_of_power
    ability_sound_ids.update(event for obj in places_of_power.export(resolver)['objects'].values()
                             if obj['power'] for event in obj['power']['SoundEvents'])
    ability_sounds = export_sounds(output,resolver,ability_sound_ids,trace,slots=3)
    for name in ['Units','Buildings','Models','BBoxes','Weapons','DamageTypes','UnitToUnitModifiers','WeaponEffects','WeaponImpactTypes','SoundEvents','SoundEffects','UnitAnimStates',
                 'GlobalSpells','Cursors','BattleGear','Techniques','Poisons','Spells','SpellAndEvents','SpellAndEventAndActions','SpellValidTargets','SpellActions','SpellEvents','SpellUsageTypes','SpellRanges','ToolScriptCommands','UVAs']:
        trace(resolver.db.files[name.lower()])
    projectiles = {name: projectile_presentation(output,resolver,name,trace,
        include_sprite_effects=name == 'UNIT_G_ELVEN_LIGHTBRINGER') for name in roster if name in RANGED_UNITS}
    if 'UNIT_G_HERO_LEGOLAS' in roster:
        weapon = resolver.db['Weapons'].get(29)
        projectiles['hero-trueshot'] = projectile_presentation(output,resolver,'UNIT_G_HERO_LEGOLAS',trace,weapon['AssociatedProjectile'],include_sprite_effects=True)
    if 'UNIT_E_HERO_GRISHNAKH' in roster:
        # Build-140: Grishnakh's Torch (BATTLE_GEAR_GRISHNAKH_TORCH, weapon 54, projectile 31) flies at an enemy building.
        projectiles['hero-torch'] = projectile_presentation(output,resolver,None,trace,resolver.db['Weapons'].get(54)['AssociatedProjectile'],include_sprite_effects=True)
    if 'UNIT_G_DWARVEN_AXETHROWER' in roster:
        flame = resolver.db['Techniques'].get('TECHNIQUE_FLAMING_AXE')
        weapon = resolver.db['Weapons'].get(flame['MissileWeapon'])
        projectiles['dwarf-flaming-axe'] = projectile_presentation(output,resolver,'UNIT_G_DWARVEN_AXETHROWER',trace,weapon['AssociatedProjectile'],include_sprite_effects=True)
    if 'UNIT_E_HARADRIM_ASSASSIN' in roster:
        dart = resolver.db['Weapons'].get(45)
        projectiles['evil-poison-dart'] = projectile_presentation(output,resolver,'UNIT_E_HARADRIM_ASSASSIN',trace,dart['AssociatedProjectile'],include_sprite_effects=True)
    if 'UNIT_E_TROLL_STONEHURLER' in roster:
        # Build-171: Unstoppable Force's weapon 73 throws the larger boulder (projectile 35); the research names it by id.
        boulder = resolver.db['Weapons'].get(resolver.db['Techniques'].get('TECHNIQUE_UNSTOPPABLE_FORCE')['MissileWeapon'])['AssociatedProjectile']
        projectiles['projectile-%d' % boulder] = projectile_presentation(output,resolver,'UNIT_E_TROLL_STONEHURLER',trace,boulder)
    for key, weapon_id in [('evil-flame-arrows',30),('evil-poison-arrows',46)]:
        if 'UNIT_E_ORC_BOWMAN' in roster:
            weapon=resolver.db['Weapons'].get(weapon_id)
            projectiles[key]=projectile_presentation(output,resolver,None,trace,weapon['AssociatedProjectile'],include_sprite_effects=True)
    heal_visual = healing_visual.export(output,resolver,trace)
    fire_visual = healing_visual.export_sprite_effect(output,resolver,trace,'EFFECT_BUILDING_FIRE_TORCH','building-fire')
    building_damage_visuals = {
        side: healing_visual.export_sprite_effect(output,resolver,trace,'EFFECT_FX_BUILDING_DAMAGE_'+side.upper(),
                                                   'building-damage/'+side)
        for side in ('good', 'evil')}
    aura_visuals = {
        str(i): healing_visual.export_sprite_effect(output,resolver,trace,resolver.db['Effects'].get(i).name,'auras/'+str(i))
        for i in (147, 148, 338, 353, 354)}
    # Terror's target particle track has no bound emitter. The wind, whisps and ring do.
    aura_visuals['353']['Layers'] = [layer for layer in aura_visuals['353']['Layers'] if layer['Name'] != 'particle']
    elven_visuals = elven_abilities.visuals(output,resolver,trace) if 'UNIT_G_ELVEN_LIGHTBRINGER' in roster else {}
    nature_visuals = nature_abilities.visuals(output,resolver,trace) if 'UNIT_G_BEORNING' in roster else {}
    import hero_skills
    hero_visuals = hero_skills.visuals(output,resolver,trace) if any(unit['Hero'] for unit in units) else {}
    dropped_objects = {}
    if any(unit['Hero'] and unit['Name'].startswith('UNIT_E_') for unit in units):
        import evil_abilities
        hero_visuals = {**hero_visuals, **evil_abilities.visuals(output,resolver,trace)}
        dropped_objects = evil_abilities.dropped_objects(output,resolver)
    if any(power['Id'] == 9 for power in powers):
        dropped_objects = {**dropped_objects, **global_powers.dropped_objects(output,resolver)}
    global_visuals = global_powers.visuals(output,resolver,trace)
    data = {'GlobalPowers': powers, 'GlobalVisuals': global_visuals, 'FormatVersion': 3,'TicksPerSecond': 20,'Units': units,'Pairs': pairs,'Sounds': sounds,'HeroExperienceSource':hero_experience.SOURCE,
        'TypeMatchups': type_matchups(resolver.db['DamageTypes']),
        'TypeMatchupSource': {'Cycle': 'Shipped English manual addendum, PDF page 7; owner weapon-rating photograph.',
                            'Percentages': 'Owner request, 2026-10-19: advantage 125%, disadvantage 75%, heroes 100%.',
                            'NeutralPolicy': 'Same type and attacks against Hero: 100%.',
                            'Guide': 'https://gamefaqs.gamespot.com/pc/563555-the-lord-of-the-rings-war-of-the-ring/faqs/29079'},
        'Projectiles': projectiles,'AbilitySounds': ability_sounds,'HealVisual': heal_visual,'BuildingFireVisual': fire_visual,'ElvenVisuals':elven_visuals,'NatureVisuals':nature_visuals,'HeroVisuals':hero_visuals,
        'BuildingDamageVisuals': building_damage_visuals, 'AuraVisuals': aura_visuals,
        'DroppedObjects': dropped_objects,
        'BuildingCompositions': {b.name:b['CompositionType'] for b in resolver.db['Buildings'].records},
        'Regeneration': regeneration(resolver.db['UnitAnimStates']),'RegenerationSource': REGENERATION_STATES,
        'Sources': sources,'Policy': {
            'Armor': 'Approximation: subtract armor points from DPS after type and explicit pair multipliers; clamp rate at zero.',
            'Damage': 'Use one fixed base hit from the rounded HUD damage (weapon DPS times weighted mean seconds between hits), or an explicit per-hit weapon value. Every animation hit uses that value times adjusted DPS / base weapon DPS; variants and source damage_scale affect event timing only. Missile and contact weapons have separate cadences.',
            'Ranges': 'Melee center reach is max(legacy 2000 mm, both body radii plus source weapon range as a surface gap). Elven Archer uses source missile minimum/maximum as center distances. Original range-origin semantics remain unverified.',
            'Collision': 'Static terrain and enemies use full envelopes. Allies compress personal space to 50% with a solid non-overlapping core; resting formations retain full spacing. Aragorn presentation scales 112%, radius floor 800 mm, height follows scale. Owner-requested feel policy; raw source bounds retained in Source.Collision. Original response semantics remain unverified.',
            'Types': 'The source has no type multipliers; its unit-pair rows are the type cycle (150% advantage, 90% reverse, 85% against heroes). A source pair row replaces TypeMatchups; TypeMatchups apply only to pairs without a row (build-61, was stacked). Pair invincibility takes precedence.',
            'Timing': 'ceil(source duration * event fraction * 20); cycle ceil(duration*20), final hit at most cycle-1.',
            'Movement': 'Run speed keeps the source ratios between units, scaled so the Gondor Swordsman runs at the accepted 3 m/s: RunScale = run clip speed / Swordsman run clip speed, where clip speed is the J_Position XY displacement/duration of the first positive Run choice. Walk speed is the run speed times the same ratio of the first positive Walk/Run choices. Original absolute world speed, random run variants, model scale, slopes and stamina remain unverified.',
            'Ranged': 'Source missile weapons, including the Uruk-Hai crossbow, with source release events and minimum/maximum range in center meters. Target-following missiles use an approximate 25 m/s travel speed, snapshot damage at release and survive shooter death/order replacement. Conservative navigation line of fire at release; no flight collision. Switch to source melee weapon/animations at the same body-based reach as infantry; switch cancellation preserves cooldown. Exact switch threshold/timing and LPO motion semantics remain unverified.',
            'ArrowPresentation': 'Per ranged unit: the weapon projectile model, its launch sound event from LPO event 7/opcode 46; original PCM variants at release with source minimum volume and voice cap, and the trail of its first bound effect (Archer 12, Axethrower 362). Track duration interpreted as point lifetime, key word 3 as full width, word 5 as ARGB. Runtime uses a 70% gray tint, feathered ribbon and smooth age fade; exact legacy blending and trail semantics remain unverified. Presentation never drives hits.',
            'Abilities': 'Hero purchasable gear starts locked and spends its source PurchaseCostPower in Fate; innate gear ignores nominal prices; hero levels use HeroExperienceSource. Greater Kingsfoil heals 250 HP within 10 m at source toolscript tick 8 of its 21-tick clip, cooldown 340 ticks. Out-of-range targets are approached using normal navigation. Casts replace orders and may be interrupted; spent cooldown is retained. Fury replaces DPS with 30 and applies 150% versus source Wraith/Black Rider types. Immunity records poison/stun/blind/slow protection; no current demo weapon produces these statuses. HealVisual adapts original target-effect sprite layers; poison cleansing and other original particle/effect attachments remain unsupported. Exact legacy range origins and interruption remain unverified. Frodo starts with innate Heroic Aura; The One Ring and Sting are purchased separately. The One Ring toggles (Q): OnActivate sets 50% visibility, passive and undetectable (enemies cannot target him) and OnContinuous drains 2 HP per second; the drain stops at 1 HP (adapter, the source has no floor); its OnActivate/OnDeactivate actions disable and re-enable regeneration (SPELL_ACTION_ENABLE_REGENERATION). Sting multiplies damage by 1.35 against source Orc Slasher, Orc Bowman, Uruk-Hai and Goblin Spearman; its blade-glow attachment (flag 31) is not shown. Heroic Aura gives allied units within 15 m a 1.5x ability recharge rate; its ground particle effect 148 is not shown.',
            'Regeneration': 'Build-52: a living unit regains its state rate from UnitAnimStates (Idle, Walk, Run, Ready = attack stance, Attack = mid-swing, Gather, Repair = building or repairing, Special = casting; heroes take the Hero column) times its Units HealthRegenerationMultiplier, read as HP per second, every tick and never above its maximum. Nothing while the One Ring is on, inside a Mill or Foundry, or dead. Adapters: the HP-per-second reading of the rates (the base constant is in Rings.exe, unverified), the mapping of simulation states to animation states, and integer milli-HP per tick (fractions dropped). Places of power (Ancient Fountain +1 HP/s) are not simulated.',
            'Unsupported': 'Purchase economy, levels, places of power, height bonuses, poison/status-effect producers and cleansing, other original spell particles/attachments, knockback, other ranged units and critical deaths.'}}
    target = output/'combat/rules.json'; target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps(data,indent=2)+'\n')
    return data


def refresh(output):
    output = Path(output).resolve()
    manifest_path = output/'manifest.json'; manifest = json.loads(manifest_path.read_text())
    import content_pack
    content_pack.reject_slim_cache(manifest)
    data = export(output, roster=[m['unit'] for m in manifest['models']])
    import prototype_clearance
    prototype_clearance.refresh(output,data['Units'])
    manifest = json.loads(manifest_path.read_text())
    for model in manifest['models']:
        prototype_assets.restore_geometry(output/model['file'])
    manifest['combat_rules'] = 'combat/rules.json'
    for path in sorted((output/'combat').rglob('*')):
        if path.is_file():
            manifest['files'][path.relative_to(output).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    for model in manifest['models']:
        path = output/model['file']; manifest['files'][model['file']] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest['rules'] = 'Imported combat stats, fixed-tick melee hits and Elven Archer projectiles; see combat/rules.json Policy for approximations.'
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    return data
