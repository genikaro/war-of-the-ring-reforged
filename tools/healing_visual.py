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
        if not all(math.isfinite(v) for v in (key['time'], width, height)) or not 0 <= key['time'] <= 1 or not 0 < width <= 10 or not 0 < height <= 10:
            raise ValueError('invalid healing sprite key')
        keys.append({'Time': key['time'], 'Width': width, 'Height': height, 'ColorArgb': key['words'][10]})
    if not keys:
        raise ValueError('empty healing sprite timeline')
    return keys


def export(output, resolver, trace):
    root = Path(__file__).resolve().parent.parent
    record = resolver.db['Effects'].get('EFFECT_ARAGORN_GREATER_KINGSFOIL_TARGET')
    source = Path(resolver.index.find('Effects', record['Filename']))
    effect = lfx.parse(source.read_bytes())
    trace(source); trace(resolver.db.files['effects'])
    target = output / 'combat/healing'; target.mkdir(parents=True, exist_ok=True)
    layers = []
    for track in effect['texture_tracks']:
        uva = track['keys'][0]['uva']
        texture = resolver.texture(uva['texture'])
        texture_path = root / texture['file']; trace(texture_path)
        frames = []
        with Image.open(texture_path) as atlas:
            x, y, width, height = uva['rect']
            for frame in range(uva['frame_count']):
                box = crop_rect([x + frame % uva['frames_per_row'] * width,
                    y + frame // uva['frames_per_row'] * height, width, height], atlas.size)
                path = target / (track['name'] + '_' + str(frame) + '.png')
                atlas.crop(box).convert('RGBA').save(path)
                frames.append(path.relative_to(output).as_posix())
        layers.append({'Name': track['name'], 'DurationMs': track['duration_ms'], 'Frames': frames, 'Keys': layer_keys(track)})
    return {'DurationMs': effect['duration_ms'], 'Layers': layers,
        'Source': {'Id': record.id, 'Name': record.name, 'File': source.relative_to(root).as_posix(), 'Data': effect},
        'Policy': 'Original atlas crops, layer sizes/colors and durations. Bounded deterministic sprite orbits/rise, ring scaling and additive blending adapt legacy particle emission/transform fields; exact original motion and blending remain unverified.'}
