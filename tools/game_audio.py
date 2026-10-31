"""Export original music, unit speech and gameplay sounds into the local content cache.

python tools/game_audio.py                  # refresh shared pack and existing demo
python tools/game_audio.py --cache PATH     # refresh one existing cache
Original and extracted assets stay read-only; generated audio is never committed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

import blood_effects
from pcm_audio import decode_ms_adpcm
from resolve_model import Resolver
import wor

EFFECT_EVENTS = [124, 147, 148, 149, 150, 243, 244, 245, 255, 256, 276, 277, 291]
MENU_TRACK = 'MUSIC_TRACK_HOBBIT_MAIN_INTRO_ENDING'


def export(output, resolver=None, roster=None):
    output = Path(output).resolve()
    if any(output.is_relative_to(wor.ROOT / p) for p in ['extracted', 'TLOTR - War of the Ring']):
        raise ValueError('audio cache must be outside original and extracted data')
    resolver = resolver or Resolver()
    roster = set(roster or [u.name for u in resolver.db['Units'].records if u.name.startswith(('UNIT_G_', 'UNIT_E_'))])
    previous_path = output / 'audio/index.json'
    previous = json.loads(previous_path.read_text()) if previous_path.is_file() else {}
    sources = {}
    written = set()

    def copy_asset(table, record):
        filename = record['Filename'].replace('\\', '/')
        source = resolver.index.find(table, str(Path(filename).with_suffix('.ogg'))) or resolver.index.find(table, filename)
        if source is None:
            raise ValueError('missing %s audio: %s' % (table, filename))
        source = Path(source)
        relative = 'audio/%s/%d%s' % (table.lower(), record.id, source.suffix.lower())
        target = output / relative
        if relative in written:
            return relative
        written.add(relative)
        data = source.read_bytes()
        source_key = '%s/%s' % (table, filename)
        digest = hashlib.sha256(data).hexdigest()
        sources[source_key] = digest
        if not target.is_file() or previous.get('Sources', {}).get(source_key) != digest:
            target.parent.mkdir(parents=True, exist_ok=True)
            if source.suffix.lower() == '.ogg':
                shutil.copyfile(source, target)
            else:
                # Shipped short clips use mono/stereo MS ADPCM; retain PCM WAVs too.
                import struct
                try:
                    target.write_bytes(data if struct.unpack_from('<H', data, 20)[0] == 1 else decode_ms_adpcm(data))
                except ValueError as error:
                    raise ValueError('%s: %s' % (source, error)) from error
        return relative

    speech = {}
    for event in resolver.db['SpeechFXEvents'].records:
        unit = resolver.db['Units'].get(event['Unit'])
        if unit is None or unit.name not in roster:
            continue
        kind = resolver.db['SpeechFXEventTypes'].get(event['EventType'])
        clips = []
        for slot in range(1, 11):
            clip = resolver.db['SpeechFX'].get(event['SpeechFX_%d' % slot])
            weight = event['Weight_%d' % slot]
            if clip is not None and weight > 0:
                clips.append({'File': copy_asset('SpeechFX', clip), 'Weight': weight})
        if clips:
            speech.setdefault(unit.name, {})[str(kind.id)] = {
                'Name': event.name, 'Chance': event['ChanceToPlay'], 'Volume': event['MinVolume'],
                'Priority': kind['Priority'], 'Group': kind['ExclusivityGroup'], 'Clips': clips}

    # Melee uses the actual weapon/material pair, just as ranged impacts already do.
    melee = {}
    event_ids = set(EFFECT_EVENTS)
    for name in sorted(roster):
        unit = resolver.db['Units'].get(name)
        weapon = resolver.db['Weapons'].get(unit['MeleeWeapon']) if unit else None
        if weapon is None:
            continue
        # Build-131: pairings the table leaves out (an Elven Archer's dagger on a building) use the default row.
        pairs = {str(c): row['SoundEvent'] for c, row in blood_effects.weapon_effect_rows(resolver, weapon['WeaponImpactType']).items()
                 if row['SoundEvent'] >= 0}
        melee[name] = pairs
        event_ids.update(pairs.values())
    effects = {}
    for event_id in sorted(event_ids):
        event = resolver.db['SoundEvents'].get(event_id)
        clips = [copy_asset('SoundEffects', clip) for slot in range(1, 11)
                 if (clip := resolver.db['SoundEffects'].get(event['SoundEffect%d' % slot])) is not None]
        effects[str(event_id)] = {'Name': event.name, 'Volume': event['MinVolume'],
                                 'MaxSimultaneous': event['MaxSimultaneous'], 'Clips': clips}
    music = {'Menu': [], 'Good': [], 'Evil': []}
    for entry in sorted(resolver.db['Music'].records, key=lambda e: (e['Track'], e.id)):
        track = resolver.db['MusicTracks'].get(entry['Track'])
        # Normal themes play to completion; combat never replaces them. The menu
        # has its own cue, and cinematic/credits music stays out of faction play.
        if track is None or entry['Theme'] != 1:
            continue
        context = ('Menu' if track.name == MENU_TRACK else
                   'Good' if track.name.endswith('_GOOD') and track['ClanAffiliation'] == 1 else
                   'Evil' if track.name.endswith('_EVIL') and track['ClanAffiliation'] == 2 else None)
        if context:
            music[context].append({'Name': track.name, 'File': copy_asset('Music', entry)})
    data = {'FormatVersion': 2, 'Speech': speech, 'Effects': effects, 'Melee': melee,
            'Music': music,
            'Sources': sources}
    previous_path.parent.mkdir(parents=True, exist_ok=True)
    previous_path.write_text(json.dumps(data, indent=2) + '\n')
    return data


def refresh(cache):
    cache = Path(cache).resolve()
    manifest_path = cache / ('content.json' if (cache / 'content.json').is_file() else 'manifest.json')
    manifest = json.loads(manifest_path.read_text())
    if 'content' in manifest:
        return refresh((cache / manifest['content']).resolve())
    roster = [m['unit'] for m in manifest['models']]
    data = export(cache, roster=roster)
    manifest['audio'] = 'audio/index.json'
    manifest['files'].update({p.relative_to(cache).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                              for p in (cache / 'audio').rglob('*') if p.is_file()})
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    return data


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path)
    args = parser.parse_args()
    targets = [args.cache] if args.cache else [wor.ROOT / 'converted/content/good', wor.ROOT / 'converted/prototype']
    for target in targets:
        if target.is_dir():
            data = refresh(target)
            print('%s: %d speaking unit types, music %s' % (target, len(data['Speech']),
                  ', '.join('%s=%d' % (k, len(v)) for k, v in data['Music'].items())), flush=True)
