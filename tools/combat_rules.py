"""Traced melee/ranged rules adapter for the four Godot demo types.

Source data is read-only. Runtime numbers are integers. See prototype/COMBAT.md
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
import healing_visual
from pcm_audio import decode_ms_adpcm

ROOT = Path(__file__).resolve().parent.parent
UNITS = ['UNIT_G_HERO_ARAGORN', 'UNIT_G_ELF_KNIGHT', 'UNIT_E_URUK_HAI', 'UNIT_G_ELVEN_ARCHER']
# Units using their source missile weapon. The source Uruk-Hai also lists one;
# the accepted Helm's Deep demo keeps it melee.
RANGED_UNITS = ['UNIT_G_ELVEN_ARCHER', 'UNIT_G_DWARVEN_AXETHROWER']


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
                 if j.name.replace('_', '').replace(' ', '').lower() == 'jposition')
    result = {}
    for mode in ['Walk', 'Run']:
        state = next(s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_' + mode.upper())
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


def collision_profile(model, skeleton, boxes):
    main = [b for b in boxes if b['layer'] == 0]
    if len(main) != 1:
        raise ValueError('expected one main collision box')
    box = main[0]; v = box['vectors']; scale = model['scale']
    if (box['flag'] != 0 or any(not math.isfinite(x) for x in v+scale)
            or min(v[:3]+scale) <= 0 or any(abs(a-b) > 1e-5 for a,b in zip(v[8:],[1,0,0,0]))):
        raise ValueError('unsupported main collision box')
    joint = skeleton.joints[box['joint']].name if box['joint'] >= 0 else None
    if joint and joint.replace('_','').replace(' ','').lower() != 'jposition':
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


def projectile_presentation(output, resolver, unit_name, trace, projectile_id=None):
    """Source projectile model, launch sounds and trail of one ranged unit's missile weapon, or of a given projectile."""
    from lms2gltf import convert_model
    if projectile_id is None:
        projectile_id = resolver.db['Weapons'].get(resolver.db['Units'].get(unit_name)['MissileWeapon'])['AssociatedProjectile']
    record = resolver.db['Projectiles'].get(projectile_id)
    path = Path(resolver.index.find('Projectiles',record['Filename']))
    projectile = lpo.parse(path.read_bytes())
    launch_sounds = export_sounds(output,resolver,lpo.launch_sound_events(projectile),trace,slots=6)
    effect_id = next(b['source'] for key in projectile['resource_keys'] for b in key['bindings'] if b['source'] >= 0)
    effect_record = resolver.db['Effects'].get(effect_id)
    effect_path = Path(resolver.index.find('Effects',effect_record['Filename']))
    effect = lfx.parse(effect_path.read_bytes())
    trail = trail_profile(effect['trail_tracks'][0])
    trail['Source'] = {'Id': effect_id,'Name': effect_record.name,'File': str(effect_path.relative_to(ROOT)), 'Data': effect}
    trace(effect_path); trace(resolver.db.files['effects'])
    model_id = next(k['id'] for k in projectile['resource_keys'] if k['kind'] == 0 and k['id'] >= 0)
    visual = 'combat/' + record.name.lower() + '.glb'
    convert_model(resolver,str(model_id),str(output/visual),extras={'wotrProjectile':projectile})
    trace(path); trace(resolver.db.files['projectiles'])
    trace(ROOT/resolver.resolve(str(model_id),recurse=False)['file'])
    return {'Visual': visual,'LaunchSounds': launch_sounds,'Trail': trail,
            'Source': {'Id': record.id,'Name': record.name,'Data': projectile}}


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
    units = []
    for name in roster:
        unit = resolver.db['Units'].get(name)
        ranged = name in RANGED_UNITS
        weapon = resolver.db['Weapons'].get(unit['MissileWeapon'] if ranged else unit['MeleeWeapon'])
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
        ready = next(s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_ATTACK_READY')
        ready_clip = next(c['clip_name'] for c in ready['choices'] if c['weight'] > 0)
        damage_type = resolver.db['DamageTypes'].get(unit['DamageType'])
        units.append({'Id': unit.id,'Name': name,'DisplayName': unit['Name'],'MaxHealth': unit['MaxHealth'],
            'StartingHealthPercent': unit['StartingHealthPct'],'ArmorMilli': quantize(unit['ArmorPoints']),
            'RegenerationPpm': quantize(unit['HealthRegenerationMultiplier'],1_000_000),
            'DamageType': damage_type.id,'DamageTypeName': damage_type.name,'MaxAttackers': unit['MaxUnitsAttacking'],
            'Composition': unit['CompositionType'],'WeaponId': weapon.id,'WeaponName': weapon.name,'Hero': bool(unit['IsHero']),
            'DpsMilli': quantize(weapon['CuttingDamagePerSecond']), 'SourceRangeMm': quantize(weapon['MaximumRange']),
            'ReachMm': quantize(weapon['MaximumRange']) if ranged else 2000,'ReadyClip': ready_clip,'Attacks': choices,
            'MinimumRangeMm': quantize(weapon['MinimumRange']) if ranged else 0,
            'ProjectileSpeedMmPerSecond': 25000 if ranged else 0,
            'MeleeDpsMilli': quantize(melee_weapon['CuttingDamagePerSecond']) if ranged else 0,
            'MeleeReachMm': 2000,'MeleeGapMm': quantize(melee_weapon['MaximumRange']) if ranged else 0,
            'WalkScale': walk_scale,'WalkClip': motion['Walk']['Clip'],'RunClip': motion['Run']['Clip'],
            'BodyRadiusMm': radius,'BodyHeightMm': body_height,'SelectionRadiusMm': quantize(unit['SelectionRingRadius']),
            'Abilities': hero_abilities.export(resolver,unit,model,skeleton),
            **demo_body_tuning(name, radius, body_height),
            'AttackGapMm': 0 if ranged else quantize(weapon['MaximumRange']),
            'Source': {'Unit': unit.name,'ModelId': model['id'],'ModelFile': model['file'],
                       'StateId': state['id'],'SkippedAttackClips': skipped,'Weapon': weapon.values,'Locomotion': motion,'Collision': body_source,
                       'MeleeWeaponId': melee_weapon.id,'MeleeWeaponName': melee_weapon.name,'MeleeWeapon': melee_weapon.values,'MeleeStateId': melee_state['id'],
                       'MaxTurnAngleDegrees': unit['MaxTurnAngle']}})
    ids = {u['Id'] for u in units}
    pairs = [{'From': p['FromUnit'],'To': p['ToUnit'],'Scale': quantize(p['DamageMultiplier'],1_000_000),
              'Invincible': bool(p['IsInvinicible']),'SourceId': p.id,'SourceName': p.name}
             for p in resolver.db['UnitToUnitModifiers'].records if p['FromUnit'] in ids and p['ToUnit'] in ids]
    sound_event_ids = set()
    for unit in units:
        impact = unit['Source']['Weapon']['WeaponImpactType']
        for victim in units:
            effect = next((r for r in resolver.db['WeaponEffects'].records
                           if r['WeaponImpactType'] == impact and r['CompositionType'] == victim['Composition']),None)
            if effect and effect['SoundEvent'] >= 0:
                sound_event_ids.add(effect['SoundEvent'])
    sounds = export_sounds(output,resolver,sound_event_ids,trace)
    ability_sounds = export_sounds(output,resolver,{event for unit in units for ability in unit['Abilities']
        for event in ability['Source'].get('SoundEvents',[])},trace,slots=3)
    for name in ['Units','Models','BBoxes','Weapons','DamageTypes','UnitToUnitModifiers','WeaponEffects','WeaponImpactTypes','SoundEvents','SoundEffects','UnitAnimStates',
                 'BattleGear','Spells','SpellAndEvents','SpellAndEventAndActions','SpellValidTargets','SpellActions','SpellEvents','SpellUsageTypes','SpellRanges','ToolScriptCommands','UVAs']:
        trace(resolver.db.files[name.lower()])
    projectiles = {name: projectile_presentation(output,resolver,name,trace) for name in roster if name in RANGED_UNITS}
    heal_visual = healing_visual.export(output,resolver,trace)
    data = {'FormatVersion': 3,'TicksPerSecond': 20,'Units': units,'Pairs': pairs,'Sounds': sounds,
        'TypeMatchups': type_matchups(resolver.db['DamageTypes']),
        'TypeMatchupSource': {'Cycle': 'Shipped English manual addendum, PDF page 7; owner weapon-rating photograph.',
                            'Percentages': 'Owner request, 2026-10-19: advantage 125%, disadvantage 75%, heroes 100%.',
                            'NeutralPolicy': 'Same type and attacks against Hero: 100%.',
                            'Guide': 'https://gamefaqs.gamespot.com/pc/563555-the-lord-of-the-rings-war-of-the-ring/faqs/29079'},
        'Projectiles': projectiles,'AbilitySounds': ability_sounds,'HealVisual': heal_visual,
        'Regeneration': regeneration(resolver.db['UnitAnimStates']),'RegenerationSource': REGENERATION_STATES,
        'Sources': sources,'Policy': {
            'Armor': 'Approximation: subtract armor points from DPS after type and explicit pair multipliers; clamp rate at zero.',
            'Damage': 'Distribute DPS * quantized cycle duration across hit events in proportion to source damage_scale.',
            'Ranges': 'Melee center reach is max(legacy 2000 mm, both body radii plus source weapon range as a surface gap). Elven Archer uses source missile minimum/maximum as center distances. Original range-origin semantics remain unverified.',
            'Collision': 'Static terrain and enemies use full envelopes. Allies compress personal space to 50% with a solid non-overlapping core; resting formations retain full spacing. Aragorn presentation scales 112%, radius floor 800 mm, height follows scale. Owner-requested feel policy; raw source bounds retained in Source.Collision. Original response semantics remain unverified.',
            'Types': 'Apply TypeMatchups once, then explicit unit-pair multiplier once (absent pairs 1.0). Pair invincibility takes precedence. Stacking remains an adapter policy.',
            'Timing': 'ceil(source duration * event fraction * 20); cycle ceil(duration*20), final hit at most cycle-1.',
            'Movement': 'Retain accepted 3 m/s run speed. Walk speed uses the source J_Position XY displacement/duration ratio of first positive walk/run choices. Original world speed and stamina remain unverified.',
            'Ranged': 'Elven Archer and Dwarf Axethrower (the source Uruk-Hai missile weapon stays disabled). Source missile range in center meters; source release event. Target-following arrows use an approximate 25 m/s travel speed, snapshot damage at release and survive shooter death/order replacement. Conservative navigation line of fire at release; no flight collision. Switch to source melee weapon/animations at the same body-based reach as infantry; switch cancellation preserves cooldown. Exact switch threshold/timing and LPO motion semantics remain unverified.',
            'ArrowPresentation': 'Per ranged unit: the weapon projectile model, its launch sound event from LPO event 7/opcode 46; original PCM variants at release with source minimum volume and voice cap, and the trail of its first bound effect (Archer 12, Axethrower 362). Track duration interpreted as point lifetime, key word 3 as full width, word 5 as ARGB. Runtime uses a 70% gray tint, feathered ribbon and smooth age fade; exact legacy blending and trail semantics remain unverified. Presentation never drives hits.',
            'Abilities': 'Aragorn sandbox pre-equips three source abilities without purchase/level progression. Greater Kingsfoil heals 250 HP within 10 m at source toolscript tick 8 of its 21-tick clip, cooldown 340 ticks. Out-of-range targets are approached using normal navigation. Casts replace orders and may be interrupted; spent cooldown is retained. Fury replaces DPS with 30 and applies 150% versus source Wraith/Black Rider types. Immunity records poison/stun/blind/slow protection; no current demo weapon produces these statuses. HealVisual adapts original target-effect sprite layers; poison cleansing and other original particle/effect attachments remain unsupported. Exact legacy range origins, interruption and progression remain unverified. Frodo pre-equips his three source abilities. The One Ring toggles (Q): OnActivate sets 50% visibility, passive and undetectable (enemies cannot target him) and OnContinuous drains 2 HP per second; the drain stops at 1 HP (adapter, the source has no floor); its OnActivate/OnDeactivate actions disable and re-enable regeneration (SPELL_ACTION_ENABLE_REGENERATION). Sting multiplies damage by 1.35 against source Orc Slasher, Orc Bowman, Uruk-Hai and Goblin Spearman; its blade-glow attachment (flag 31) is not shown. Heroic Aura gives allied units within 15 m a 1.5x ability recharge rate; its ground particle effect 148 is not shown.',
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
