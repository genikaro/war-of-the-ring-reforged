"""Original hit blood, weapon impact flashes and death blood pools for the prototype (build-129).

python tools/blood_effects.py                      # refresh converted/content/good in place
python tools/blood_effects.py --pack <content pack>

Sources, all named by the designers' own column comments:
- Units.BloodEffectType: "the particle effect that will play on the unit when he/she is hit" (red blood 10, orc
  blood 378, Huorn splinters 379; -1 for wraiths, the Balrog and the Nazgul).
- WeaponEffects.Effect: "the effect that will be played on a hit", chosen by the weapon's WeaponImpactType and the
  victim's CompositionType (cutting weapons on flesh flash white, Weapon_Impact_Cutting_Strong; on orc flesh they
  splash orc blood). A pairing the table does not list uses the impact type's DefaultWeaponEffectSetting.
- Units.DeathDecalType1-3: "one of three randomly chosen decals for death", with the Decals record's size, growth and
  fade timing (human blood pools grow to 2 m over 4 s and fade out from 10 s).
- Units.CriticalDeathEffectType (build-130): the gore burst (Human_Explode, Orc_Explode, Horse_Explode) of a unit with
  CanBeCriticallyKilled ("hero units should not be critically killable") when a weapon with CanCriticallyKill ("can
  cause critical deaths to occur": Gimli's axe, the Giant Ent, the Ballista bolt, trolls) kills it. No shipped unit
  model uses the Attack/Death Critical Behead/Impale states or MODEL_FLAG_CRITICAL_DEATH_BLOOD parts.
- Effects.IsMature / Decals.IsMature: "will not be seen if player turns off maturity in the options". The prototype's
  Settings > Game > Blood switch honours these flags.

Each texture track keeps its original atlas crop (one grid image per layer), size, colour and duration keys, delay,
the UVA frame timing (start mode 1 = a random frame per particle, otherwise frames every FrameMs) and its emitter's
count, speed, speed variation and cone. Motion, ground splats and particle budgets are runtime adapters; see Policy.
"""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

from PIL import Image

import lfx
import wor
from prototype_interface import crop_rect

PACK = wor.ROOT / 'converted/content/good'
FOLDER = 'combat/blood'
POLICY = ('Original atlas crops, sizes, colours, durations, delays, UVA frame timing and emitter count/speed/cone. '
          'Adapters: particles launch from the struck body part and fall under 9.8 m/s2; Splat tracks (bound to the '
          'Drops track, where drops land) appear on the ground around the feet; counts are capped per hit so large '
          'battles stay smooth; blood uses alpha blending and the weapon flash additive blending. Gore that flies for '
          'two seconds or more (skulls, bones, hearts, flesh) lands and rests on the ground until it fades; its source '
          'speed varies by a quarter and spreads at least 45 degrees. Exact legacy emitter semantics, sub-emission and '
          'blending remain unverified.')


def _float(word):
    return struct.unpack('<f', struct.pack('<I', word))[0]


def _keys(track):
    keys = []
    for key in sorted(track['keys'], key=lambda k: k['time']):
        w = key['words']
        width, height, width_var, height_var = (_float(w[i]) for i in (4, 5, 7, 8))
        if not (math.isfinite(key['time']) and 0 <= key['time'] <= 1 and 0 < width <= 20 and 0 < height <= 20):
            raise ValueError('invalid blood sprite key in ' + track['name'])
        keys.append({'Time': round(key['time'], 4), 'WidthMm': round(width*1000), 'HeightMm': round(height*1000),
                     'WidthVariationMm': round(max(0, width_var)*1000) if math.isfinite(width_var) else 0,
                     'HeightVariationMm': round(max(0, height_var)*1000) if math.isfinite(height_var) else 0,
                     'ColorArgb': w[10]})
    if not keys:
        raise ValueError('empty blood sprite timeline in ' + track['name'])
    return keys


def _emitter(effect, name):
    """The particle emitter that drives a texture track: E_<track>, E<track> or the track name itself."""
    wanted = {('e_' + name).lower(), ('e' + name).lower(), name.lower()}
    emitter = next((e for e in effect['particle_emitters'] if e['name'].lower() in wanted), None)
    if not emitter or not emitter['keys']:
        return None
    w, count = emitter['keys'][0]['words'], emitter['words'][20]
    speed, variation, cone = _float(w[4]), _float(w[5]), _float(w[15])
    # Header word 20 is the number of particles (one skull, six bones, fourteen drops); the key's first count is
    # the emission rate per second (343 for that one skull).
    return {'Count': count if 0 < count <= 200 else 1, 'RatePerSecond': w[3],
            'SpeedMm': round(speed*1000) if math.isfinite(speed) and 0 <= speed < 100 else 0,
            'SpeedVariationMm': round(variation*1000) if math.isfinite(variation) and 0 <= variation < 100 else 0,
            'ConeDegrees': round(math.degrees(cone), 1) if math.isfinite(cone) and 0 <= cone <= 2*math.pi else 0,
            'Name': emitter['name']}


def _ground(track, emitter):
    """Marks lying on the ground: tracks with parameter 1 (an explosion's blood, scorch and blood mark) and still
    ground-aligned tracks (third track flag: a hit's splats, the Huorn's sap). Ground-aligned gore that is launched
    (an orc's bones and flesh) flies first."""
    aligned = len(track['track_flags']) > 2 and track['track_flags'][2] == 1
    return track['parameter'] == 1 or aligned and not (emitter and emitter['SpeedMm'] > 0)


def _rests(track, emitter):
    """Adapter (build-130): gore pieces fly, land and lie until they fade. They are tracks lasting two seconds or
    more, at most a metre across, not on the ground, launched by their emitter or opaque; mists (diffusion) hover."""
    keys = _keys(track)
    size = max(max(k['WidthMm'], k['HeightMm']) for k in keys)
    launched = emitter is not None and emitter['SpeedMm'] > 0
    opaque = keys[0]['ColorArgb'] >> 24 >= 0xc0
    return (not _ground(track, emitter) and track['duration_ms'] >= 2000 and size <= 1000
            and 'diffusion' not in track['name'].lower() and (launched or opaque))


def export_effect(output, resolver, effect_id, trace):
    record = resolver.db['Effects'].get(effect_id)
    source = Path(resolver.index.find('Effects', record['Filename']))
    effect = lfx.parse(source.read_bytes())
    trace(source)
    target = output / FOLDER
    target.mkdir(parents=True, exist_ok=True)
    layers = []
    for index, track in enumerate(effect['texture_tracks']):
        uva = track['keys'][0].get('uva') if track['keys'] else None
        if not uva or uva['texture'] < 0:
            continue  # Model-bound and null tracks draw nothing here.
        texture = resolver.texture(uva['texture'])
        texture_path = wor.ROOT / texture['file']
        trace(texture_path)
        emitter = _emitter(effect, track['name'])
        columns = max(1, min(uva['frames_per_row'], uva['frame_count']))
        rows = -(-uva['frame_count'] // columns)
        with Image.open(texture_path) as atlas:
            x, y, width, height = uva['rect']
            first = crop_rect([x, y, width, height], atlas.size)
            cell = (first[2]-first[0], first[3]-first[1])
            grid = Image.new('RGBA', (cell[0]*columns, cell[1]*rows))
            for frame in range(uva['frame_count']):
                rect = [x + frame % uva['frames_per_row']*width, y + frame // uva['frames_per_row']*height, width, height]
                tile = atlas.crop(crop_rect(rect, atlas.size)).convert('RGBA').resize(cell)
                grid.paste(tile, (frame % columns*cell[0], frame // columns*cell[1]))
        path = target / ('%s_%d_%s.png' % (record.name.lower(), index, track['name'].lower()))
        grid.save(path)
        layers.append({'Name': track['name'], 'Texture': path.relative_to(output).as_posix(),
                       'Columns': columns, 'Rows': rows, 'Frames': uva['frame_count'],
                       'FrameMs': uva['frame_ms'], 'RandomFrame': uva['start_mode'] == 1,
                       'DurationMs': track['duration_ms'], 'DelayMs': track['delay_ms'],
                       'Ground': _ground(track, emitter), 'Rests': _rests(track, emitter), 'Keys': _keys(track),
                       'Emitter': emitter})
    return {'Name': record.name, 'File': source.relative_to(wor.ROOT).as_posix(), 'Mature': bool(record['IsMature']),
            'DurationMs': effect['duration_ms'], 'Layers': layers}


def export_decal(output, resolver, decal_id, trace):
    record = resolver.db['Decals'].get(decal_id)
    texture = resolver.texture(record['FirstStageTexture'])
    texture_path = wor.ROOT / texture['file']
    trace(texture_path)
    target = output / FOLDER
    target.mkdir(parents=True, exist_ok=True)
    path = target / (record.name.lower() + '.png')
    with Image.open(texture_path) as image:
        image.convert('RGBA').save(path)
    mm, ms, ppm = (lambda v: round(v*1000)), (lambda v: round(v*1000)), (lambda v: round(v*1_000_000))
    return {'Name': record.name, 'Texture': path.relative_to(output).as_posix(), 'Mature': bool(record['IsMature']),
            'StartWidthMm': mm(record['StartWidth']), 'EndWidthMm': mm(record['EndWidth']),
            'EndWidthVariationMm': mm(record['EndWidthVariation']),
            'ResizeStartMs': ms(record['ResizeStartOffset']), 'ResizeMs': ms(record['ResizeDuration']),
            'ResizeVariationMs': ms(record['ResizeDurationVariation']), 'FadeInMs': ms(record['FadeInDuration']),
            'FadeOutStartMs': ms(record['FadeOutStartOffset']), 'FadeOutStartVariationMs': ms(record['FadeOutStartOffsetVariation']),
            'FadeOutMs': ms(record['FadeOutDuration']), 'FadeOutVariationMs': ms(record['FadeOutDurationVariation']),
            'AlphaPpm': ppm(record['Alpha']), 'AlphaVariationPpm': ppm(record['AlphaVariation']),
            'RandomRotation': bool(record['IsRotatedRandomly'])}


def weapon_effect_rows(resolver, impact, compositions=None):
    """The WeaponEffects row (hit effect and sound) per victim composition for one weapon impact type. A pairing the
    table does not list uses the impact type's DefaultWeaponEffectSetting ("the setting used when an
    impact/composition pairing is not defined"): an arrow striking a wooden or stone building sounds like one striking
    dirt. Without compositions, every source composition."""
    if compositions is None:
        compositions = [r.id for r in resolver.db['Compositions'].records]
    rows = resolver.db['WeaponEffects'].records
    default = resolver.db['WeaponImpactTypes'].get(impact)['DefaultWeaponEffectSetting']
    result = {}
    for composition in sorted(compositions):
        row = next((r for r in rows if r['WeaponImpactType'] == impact and r['CompositionType'] == composition), None)
        if row is None and default >= 0:
            row = resolver.db['WeaponEffects'].get(default)
        if row is not None:
            result[composition] = row
    return result


def weapon_effects(resolver, weapon_id, compositions):
    """Effect id per victim composition for one weapon, from WeaponEffects or the impact type's default row."""
    if weapon_id < 0:
        return {}
    impact = resolver.db['Weapons'].get(weapon_id)['WeaponImpactType']
    return {str(c): row['Effect'] for c, row in weapon_effect_rows(resolver, impact, compositions).items() if row['Effect'] >= 0}


def export(output, resolver, roster, trace=lambda path: None):
    output = Path(output).resolve()
    units = [resolver.db['Units'].get(name) for name in roster]
    compositions = {unit['CompositionType'] for unit in units} | {b['CompositionType'] for b in resolver.db['Buildings'].records}
    unit_data, effect_ids, decal_ids = {}, set(), set()
    for unit in units:
        decals = [d for d in (unit['DeathDecalType1'], unit['DeathDecalType2'], unit['DeathDecalType3']) if d >= 0]
        data = {'Blood': unit['BloodEffectType'] if unit['BloodEffectType'] >= 0 else None, 'DeathDecals': decals,
                'MeleeImpacts': weapon_effects(resolver, unit['MeleeWeapon'], compositions),
                'MissileImpacts': weapon_effects(resolver, unit['MissileWeapon'], compositions),
                'CriticalDeath': unit['CriticalDeathEffectType']
                if unit['CanBeCriticallyKilled'] and unit['CriticalDeathEffectType'] >= 0 else None}
        unit_data[unit.name] = data
        effect_ids.update(e for e in [data['Blood'], data['CriticalDeath'], *data['MeleeImpacts'].values(),
                                      *data['MissileImpacts'].values()] if e is not None)
        decal_ids.update(decals)
    for name in ['units', 'weapons', 'weaponeffects', 'weaponimpacttypes', 'effects', 'decals', 'buildings']:
        trace(resolver.db.files[name])
    data = {'FormatVersion': 1,
            'Effects': {str(i): export_effect(output, resolver, i, trace) for i in sorted(effect_ids)},
            'Decals': {str(i): export_decal(output, resolver, i, trace) for i in sorted(decal_ids)},
            'Units': unit_data, 'Policy': POLICY}
    path = output / 'combat/blood.json'
    path.write_text(json.dumps(data, indent=2) + '\n')
    return data


def refresh(pack=PACK):
    """Export into an existing content pack and record the new files in its content.json."""
    import prepare_prototype
    pack = Path(pack).resolve()
    manifest_path = pack / 'content.json'
    manifest = json.loads(manifest_path.read_text())
    resolver = prepare_prototype.open_resolver(pack)
    roster = [m['unit'] for m in manifest['models']]
    data = export(pack, resolver, roster)
    manifest['blood'] = 'combat/blood.json'
    for path in sorted([pack / 'combat/blood.json', *(pack / FOLDER).rglob('*')]):
        if path.is_file():
            manifest['files'][path.relative_to(pack).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    return data


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--pack', type=Path, default=PACK)
    args = parser.parse_args()
    data = refresh(args.pack)
    print('Blood: %d effects, %d decals, %d units -> %s' % (len(data['Effects']), len(data['Decals']), len(data['Units']), args.pack))
