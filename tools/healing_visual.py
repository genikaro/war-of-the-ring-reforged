"""Extract the Greater Kingsfoil target effect's original sprite layers."""
import math
import struct
from pathlib import Path
from PIL import Image
import lfx
from prototype_interface import crop_rect


def layer_keys(track):
    keys = []
    for key in sorted(track['keys'], key=lambda k: k['time']):
        width, height = [struct.unpack('<f', struct.pack('<I', w))[0] for w in key['words'][4:6]]
        if not all(math.isfinite(v) for v in (key['time'], width, height)) or not 0 <= key['time'] <= 1 or not 0 < width <= 100 or not 0 < height <= 100:
            raise ValueError('invalid healing sprite key')
        keys.append({'Time': key['time'], 'Width': width, 'Height': height, 'ColorArgb': key['words'][10]})
    if not keys:
        raise ValueError('empty healing sprite timeline')
    return keys


def export(output, resolver, trace):
    return export_sprite_effect(output,resolver,trace,'EFFECT_ARAGORN_GREATER_KINGSFOIL_TARGET','healing')


def export_sprite_effect(output, resolver, trace, effect_name, folder, clip_atlas_edges=False, track_names=None):
    """Shared original atlas/key extraction for Kingsfoil and the building fire effect."""
    root = Path(__file__).resolve().parent.parent
    record = resolver.db['Effects'].get(effect_name)
    source = Path(resolver.index.find('Effects', record['Filename']))
    effect = lfx.parse(source.read_bytes())
    trace(source); trace(resolver.db.files['effects'])
    target = output / 'combat' / folder; target.mkdir(parents=True, exist_ok=True)
    layers = []
    for track in effect['texture_tracks']:
        if track_names is not None and track['name'] not in track_names:
            continue
        key = track['keys'][0]
        # Texture-kind 1 binds a model (e.g. Huorn roots), not an atlas; its caller exports that mesh separately.
        if 'uva' not in key:
            continue
        uva = key['uva']
        # Null texture tracks are particle controller nodes, not drawable sprites.
        if uva['texture'] < 0:
            continue
        texture = resolver.texture(uva['texture'])
        texture_path = root / texture['file']; trace(texture_path)
        frames = []
        with Image.open(texture_path) as atlas:
            x, y, width, height = uva['rect']
            for frame in range(uva['frame_count']):
                rect = [x + frame % uva['frames_per_row'] * width, y + frame // uva['frames_per_row'] * height, width, height]
                if clip_atlas_edges:
                    # Blessed Wind's shipped hair track extends 1/64 beyond the atlas at its last column.
                    # Clip that small source overhang, preserving the raw UVA in Source.Data.
                    ax, ay, aw, ah = rect
                    if min(ax, ay) < 0 or max(ax+aw, ay+ah) > 1.02:
                        raise ValueError('source effect atlas overhang exceeds two percent')
                    rect = [ax, ay, min(aw, 1-ax), min(ah, 1-ay)]
                box = crop_rect(rect, atlas.size)
                path = target / (track['name'] + '_' + str(frame) + '.png')
                atlas.crop(box).convert('RGBA').save(path)
                frames.append(path.relative_to(output).as_posix())
        layers.append({'Name': track['name'], 'DurationMs': track['duration_ms'], 'Frames': frames, 'Keys': layer_keys(track)})
    return {'DurationMs': effect['duration_ms'], 'Layers': layers,
        'Source': {'Id': record.id, 'Name': record.name, 'File': source.relative_to(root).as_posix(), 'Data': effect},
        'Policy': 'Original atlas crops, layer sizes/colors and durations. Bounded deterministic sprite motion and blending adapt legacy particle emission/transform fields; exact original motion and blending remain unverified.'}
