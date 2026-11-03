"""A map's original lighting, sky and cloud settings for the runtime cache (build-152, classic lighting).

Every .wor stores the editor's Global illumination and Environment Settings; world_scene.py reads them and this module
names them. Field meanings come from the editor dialogs (RingsEditor.exe: Initial time, Shadow length, Ambient Color,
Intensity, Sun/Moon Diffuse Color and Color intensity; Use SkyBox, Sky Color; Disable clouds; Fog of War Color/Alpha)
and from Rings.exe, which reads them and turns them into its sun light:

- 0x56d412 reads the time-of-day block into the world at +0x81c4: sunrise 0.25, sunset 0.75, day length 3600 s,
  initial time, a flag, shadow length, ambient colour and intensity (defaults set at 0x565c2d), then the
  Sun/Moon light keyframes (one on every shipped map: time 0, colour, intensity 1-2.2).
- 0x53c80a places the sun on the map's south edge (source y = 0) at a height of the map's length and aims it at
  the map centre. Between sunrise and sunset it moves from x = width to x = 0; at night the moon moves back.
- TimeOfDaySetSunLight (0x53cef7) scales the light colour by its intensity and the ambient colour by its own.
- 0x5693e7 onwards reads the environment block: humidity and wind ranges, wind direction, fog-of-war colour/alpha and
  the clouds flag (constructor default 1 at 0x5627f8, so set means clouds are on: Lothlorien's forest has it clear).

Terrain materials (Materials/Terrain_*_LOD.lmt, Two_Stage_CloudMap.lmt) modulate the ground with Textures/baseclouds.tga
(MODULATE / MODULATE2X stages); the runtime scrolls it with the wind. Colours stay in the source's gamma space.
"""
import argparse
import hashlib
import json
import math
import struct
from pathlib import Path

from PIL import Image

import wor
import world_scene

SKY_SIZE = 512
CLOUD_TEXTURE = 'TEXTURE_BASECLOUDS'
SKY_FACES = {'side': 'FrontTexture', 'top': 'TopTexture', 'bottom': 'BottomTexture'}


def _float(word):
    return struct.unpack('<f', struct.pack('<I', word))[0]


def _rgb(argb):
    return [round(((argb >> shift) & 255) / 255, 4) for shift in (16, 8, 0)]


def sun_position(time, sunrise, sunset, width, length):
    """Source-space sun (or moon) position, as Rings.exe 0x53c80a places it."""
    if sunrise <= time < sunset:
        x = width - (time - sunrise) / (sunset - sunrise) * width
    else:
        night = 1 - sunset + sunrise
        x = ((time - sunset) if time >= sunset else (1 - sunset + time)) / night * width
    return x, 0.0, length


def sun_direction(time, sunrise, sunset, width, length):
    """Direction the light travels, in glTF/Godot axes (source x, y, z become -x, z, y)."""
    x, y, z = sun_position(time, sunrise, sunset, width, length)
    dx, dy, dz = width / 2 - x, length / 2 - y, -z
    norm = math.sqrt(dx * dx + dy * dy + dz * dz)
    return [round(-dx / norm, 6), round(dz / norm, 6), round(dy / norm, 6)]


def lighting(scene):
    """The cache's "lighting" block. Image paths are filled in by write_images()."""
    meta, tail, env = scene['world'].metadata, scene['tail'], scene['terrain']['environment']
    day = tail['time_of_day']
    sunrise, sunset, day_seconds, time = (_float(w) for w in (day[3], day[5], day[4], day[6]))
    keys = tail['sun_keyframes']
    if not keys:
        raise ValueError('world has no Sun/Moon keyframe')
    key = keys[0]
    grass, final = tail['grass'], tail['final']
    words = [_float(w) for w in env['raw_words_a'][2:8]]
    return {
        'format_version': 1,
        'time_of_day': round(time, 4), 'sunrise': round(sunrise, 4), 'sunset': round(sunset, 4),
        'day_seconds': round(day_seconds, 1), 'shadow_length': round(_float(day[7]), 4),
        'ambient': {'color': _rgb(day[1]), 'intensity': round(_float(day[2]), 4)},
        'sun': {'color': _rgb(key[1]), 'intensity': round(_float(key[3]), 4),
                'direction': sun_direction(time, sunrise, sunset, meta['width'], meta['height'])},
        'clouds': {'enabled': bool(env['flag_b']), 'humidity': [round(v) for v in words[:3]],
                   'wind': [round(v) for v in words[3:]], 'wind_direction': [round(-env['raw_vector_a'][0], 4),
                                                                            round(env['raw_vector_a'][1], 4)]},
        'sky': {'skybox': final[0] == 1, 'index': final[4], 'color': _rgb(final[1])},
        'fog_of_war': {'color': _rgb(env['raw_words_c'][0]), 'alpha': round(_float(env['raw_words_c'][1]), 4)},
        'grass': {'enabled': bool(grass[8]), 'color': _rgb(grass[7]), 'height': round(_float(grass[6]), 3)},
    }


def write_images(block, output, resolver):
    """Cloud map and skybox faces as PNGs in the cache's map folder; returns the files written."""
    db = resolver.db

    def texture(rid, size=None):
        entry = resolver.texture(rid)
        if not entry or not entry['exists']:
            raise ValueError('missing texture %s' % rid)
        image = Image.open(wor.ROOT / entry['file']).convert('RGB')
        return image.resize((size, size), Image.LANCZOS) if size and image.size != (size, size) else image

    files = []
    cloud = output / 'map/clouds.png'
    cloud.parent.mkdir(parents=True, exist_ok=True)
    texture(db['Textures'].by_name[CLOUD_TEXTURE].id).save(cloud)
    block['clouds']['texture'] = 'map/clouds.png'
    files.append(cloud)
    sky = block['sky']
    record = db['Skyboxes'].get(sky['index']) if sky['skybox'] else None
    if record:
        sky['name'] = record.name
        for face, column in SKY_FACES.items():
            path = output / ('map/sky-%s.png' % face)
            texture(record[column], SKY_SIZE).save(path)
            sky[face] = path.relative_to(output).as_posix()
            files.append(path)
    return files


def refresh(cache, source=None, resolver=None):
    """Writes the lighting block and images into an existing cache without re-exporting its map."""
    from prepare_prototype import open_resolver
    cache = Path(cache).resolve()
    manifest = json.loads((cache / 'manifest.json').read_text(encoding='utf-8'))
    source = Path(source) if source else wor.ROOT / 'extracted/Levels' / manifest['map_file']
    resolver = resolver or open_resolver(cache)
    block = lighting(world_scene.parse(source.read_bytes()))
    files = write_images(block, cache, resolver)
    manifest['lighting'] = block
    manifest['files'].update({p.relative_to(cache).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files})
    (cache / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return block


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('caches', nargs='*', type=Path, help='cache folders to refresh (default: every skirmish map cache)')
    parser.add_argument('--source', type=Path, help='.wor file for a cache whose manifest names none (Helm\'s Deep prototype)')
    parser.add_argument('--show', type=Path, help='print the lighting block of a .wor file and exit')
    args = parser.parse_args()
    if args.show:
        print(json.dumps(lighting(world_scene.parse(args.show.read_bytes())), indent=2))
    else:
        caches = args.caches or [wor.ROOT / 'converted/skirmish'] + sorted(p for p in (wor.ROOT / 'converted/skirmish-maps').iterdir() if (p / 'manifest.json').is_file())
        for cache in caches:
            block = refresh(cache, args.source)
            print('%s: time %.3f, sun %s x%.2f, ambient %s x%.2f, clouds %s' % (cache.name, block['time_of_day'], block['sun']['color'],
                  block['sun']['intensity'], block['ambient']['color'], block['ambient']['intensity'], block['clouds']['enabled']))
