"""Small, traced melee-rules adapter for the three Godot demo types.

Source data is read-only. Runtime numbers are integers. See prototype/COMBAT.md
for the explicitly approximate armor/range policies and unsupported rules.
"""
from decimal import Decimal, ROUND_HALF_UP, ROUND_CEILING
import hashlib
import json
import math
from pathlib import Path

import lsk
import lmd
from resolve_model import Resolver
import prototype_assets
from pcm_audio import decode_ms_adpcm

ROOT = Path(__file__).resolve().parent.parent
UNITS = ['UNIT_G_HERO_ARAGORN', 'UNIT_G_ELF_KNIGHT', 'UNIT_E_URUK_HAI']


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


def attack_choice(choice, events, duration):
    if not math.isfinite(duration) or duration <= 0 or choice['weight'] <= 0:
        raise ValueError('invalid melee clip')
    seconds = Decimal(str(duration))
    ticks = int((seconds*20).to_integral_value(rounding=ROUND_CEILING))
    hits = []
    for index in choice['event_indices']:
        event = events[index]
        if event.get('name') != 'melee_hit':
            continue
        time, scale = event['time'], event['damage_scale']
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


def export(output, resolver=None):
    output = Path(output).resolve()
    if any(output.is_relative_to(ROOT/p) for p in ['extracted','TLOTR - War of the Ring']):
        raise ValueError('rules cache must be outside original and extracted data')
    resolver = resolver or Resolver()
    sources = {}
    def trace(path):
        path = Path(path).resolve()
        relative = path.relative_to(ROOT).as_posix() if path.is_relative_to(ROOT) else path.name
        sources[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    units = []
    for name in UNITS:
        unit = resolver.db['Units'].get(name); weapon = resolver.db['Weapons'].get(unit['MeleeWeapon'])
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
        state = next(s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_ATTACK_MELEE_01')
        durations = {clip.name: clip.duration for clip in skeleton.clips}
        choices = [attack_choice(c,model['events'],durations[c['clip_name']]) for c in state['choices'] if c['weight'] > 0]
        ready = next(s for s in model['animation_states'] if s['name'] == 'UNIT_ANIM_STATE_ATTACK_READY')
        ready_clip = next(c['clip_name'] for c in ready['choices'] if c['weight'] > 0)
        damage_type = resolver.db['DamageTypes'].get(unit['DamageType'])
        units.append({'Id': unit.id,'Name': name,'DisplayName': unit['Name'],'MaxHealth': unit['MaxHealth'],
            'StartingHealthPercent': unit['StartingHealthPct'],'ArmorMilli': quantize(unit['ArmorPoints']),
            'DamageType': damage_type.id,'DamageTypeName': damage_type.name,'MaxAttackers': unit['MaxUnitsAttacking'],
            'Composition': unit['CompositionType'],'WeaponId': weapon.id,'WeaponName': weapon.name,
            'DpsMilli': quantize(weapon['CuttingDamagePerSecond']), 'SourceRangeMm': quantize(weapon['MaximumRange']),
            'ReachMm': 2000,'ReadyClip': ready_clip,'Attacks': choices,
            'WalkScale': walk_scale,'WalkClip': motion['Walk']['Clip'],'RunClip': motion['Run']['Clip'],
            'BodyRadiusMm': radius,'BodyHeightMm': body_height,'SelectionRadiusMm': quantize(unit['SelectionRingRadius']),
            **demo_body_tuning(name, radius, body_height),
            'AttackGapMm': quantize(weapon['MaximumRange']),
            'Source': {'Unit': unit.name,'ModelId': model['id'],'ModelFile': model['file'],
                       'StateId': state['id'],'Weapon': weapon.values,'Locomotion': motion,'Collision': body_source}})
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
    sounds = []
    sound_dir = output/'combat/sounds'; sound_dir.mkdir(parents=True,exist_ok=True)
    for event_id in sorted(sound_event_ids):
        event = resolver.db['SoundEvents'].get(event_id)
        # A small, deterministic subset of the traced impact event's variants.
        for slot in range(1,4):
            effect = resolver.db['SoundEffects'].get(event['SoundEffect%d' % slot])
            if effect is None:
                continue
            path = resolver.index.find('SoundEffects',effect['Filename'])
            if path is None:
                raise ValueError('missing impact sound: ' + effect.name)
            trace(path); filename = str(effect.id)+'.wav'
            (sound_dir/filename).write_bytes(decode_ms_adpcm(Path(path).read_bytes()))
            sounds.append({'File': 'combat/sounds/'+filename,'EventId': event.id,'EffectId': effect.id,
                'EventName': event.name,'EffectName': effect.name,'VolumePpm': quantize(event['MinVolume'],1_000_000),
                'MaxSimultaneous': event['MaxSimultaneous'], 'Conversion': 'mono Microsoft ADPCM to 16-bit PCM'})
    for name in ['Units','Models','BBoxes','Weapons','DamageTypes','UnitToUnitModifiers','WeaponEffects','WeaponImpactTypes','SoundEvents','SoundEffects']:
        trace(resolver.db.files[name.lower()])
    data = {'FormatVersion': 3,'TicksPerSecond': 20,'Units': units,'Pairs': pairs,'Sounds': sounds,
        'TypeMatchups': type_matchups(resolver.db['DamageTypes']),
        'TypeMatchupSource': {'Cycle': 'Shipped English manual addendum, PDF page 7; owner weapon-rating photograph.',
                            'Percentages': 'Owner request, 2026-10-19: advantage 125%, disadvantage 75%, heroes 100%.',
                            'NeutralPolicy': 'Same type and attacks against Hero: 100%.',
                            'Guide': 'https://gamefaqs.gamespot.com/pc/563555-the-lord-of-the-rings-war-of-the-ring/faqs/29079'},
        'Sources': sources,'Policy': {
            'Armor': 'Approximation: subtract armor points from DPS after type and explicit pair multipliers; clamp rate at zero.',
            'Damage': 'Distribute DPS * quantized cycle duration across hit events in proportion to source damage_scale.',
            'Ranges': 'Center reach is max(legacy 2000 mm, both body radii plus source weapon range as a surface gap). Original range-origin semantics remain unverified.',
            'Collision': 'Static terrain and enemies use full envelopes. Allies compress personal space to 50% with a solid non-overlapping core; resting formations retain full spacing. Aragorn presentation scales 112%, radius floor 800 mm, height follows scale. Owner-requested feel policy; raw source bounds retained in Source.Collision. Original response semantics remain unverified.',
            'Types': 'Apply TypeMatchups once, then explicit unit-pair multiplier once (absent pairs 1.0). Pair invincibility takes precedence. No explicit pairs exist among the three demo types. Stacking for other roster units remains unverified.',
            'Timing': 'ceil(source duration * event fraction * 20); cycle ceil(duration*20), final hit at most cycle-1.',
            'Movement': 'Retain accepted 3 m/s run speed. Walk speed uses the source J_Position XY displacement/duration ratio of first positive walk/run choices. Original world speed and stamina remain unverified.',
            'Unsupported': 'Upgrades, levels, regeneration, height bonuses, poison, knockback, ranged and critical deaths.'}}
    target = output/'combat/rules.json'; target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps(data,indent=2)+'\n')
    return data


def refresh(output):
    output = Path(output).resolve()
    data = export(output)
    import prototype_clearance
    prototype_clearance.refresh(output,data['Units'])
    manifest_path = output/'manifest.json'; manifest = json.loads(manifest_path.read_text())
    for model in manifest['models']:
        prototype_assets.restore_geometry(output/model['file'])
    manifest['combat_rules'] = 'combat/rules.json'
    for path in sorted((output/'combat').rglob('*')):
        if path.is_file():
            manifest['files'][path.relative_to(output).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    for model in manifest['models']:
        path = output/model['file']; manifest['files'][model['file']] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest['rules'] = 'Imported melee stats and fixed-tick clip hits; see combat/rules.json Policy for approximations.'
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    return data
