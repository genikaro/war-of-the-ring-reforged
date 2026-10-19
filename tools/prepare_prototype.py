"""Build the local Godot sandbox cache from the owner's installation.

python tools/prepare_prototype.py
python tools/prepare_prototype.py --install "C:/Games/War of the Ring"
No game files are included in the source project. Source directories are read only.
"""
import argparse
import base64
from collections import deque
import hashlib
import json
from pathlib import Path
import struct

import h2o
import prototype_assets
from lms2gltf import convert_model
from resolve_model import Resolver
import wor
import world_scene
import wor2gltf


def connected(allowed, width, height):
    remaining = {i for i, value in enumerate(allowed) if value}
    best = set()
    while remaining:
        first = min(remaining); remaining.remove(first)
        queue, component = deque([first]), {first}
        while queue:
            cell = queue.popleft(); x, y = cell % width, cell // width
            for nxt in [cell - 1 if x else -1, cell + 1 if x + 1 < width else -1,
                        cell - width if y else -1, cell + width if y + 1 < height else -1]:
                if nxt in remaining:
                    remaining.remove(nxt); component.add(nxt); queue.append(nxt)
        if len(component) > len(best):
            best = component
    return best


def prepare(output, install=None):
    output = Path(output).resolve()
    if output.is_relative_to(wor.ROOT / 'extracted') or output.is_relative_to(wor.ROOT / 'TLOTR - War of the Ring'):
        raise ValueError('cache must be outside original and extracted data')
    db_path = None
    if install:
        install = Path(install).resolve()
        if output.is_relative_to(install):
            raise ValueError('cache must be outside original install')
        db_path = install / 'Database'
        if not (db_path / 'Models.lqr').is_file():
            raise ValueError('installation has no Database/Models.lqr')
        for name in ['Cloths', 'Effects', 'Levels', 'Materials', 'Meshes', 'MeshPathLookups',
                     'Models', 'Skeletons', 'TerrainSets', 'TextureAnimations', 'Textures', 'UVAs', 'Weights']:
            source = install / 'Archives' / (name + '.H2O')
            # Validate paths before using the archive extractor.
            archive = h2o.H2OArchive(str(source))
            destination = (wor.ROOT / 'extracted' / name).resolve()
            for entry in archive.entries:
                target = (destination / entry.path.replace('\\', '/')).resolve()
                if not target.is_relative_to(destination):
                    raise ValueError('archive path escapes extraction directory')
            h2o.extract(str(source), str(wor.ROOT / 'extracted'), verify=True)
    resolver = Resolver(str(db_path)) if db_path else Resolver()
    source = wor.ROOT / 'extracted/Levels/Helms Deep.wor'
    output.mkdir(parents=True, exist_ok=True)
    map_dir = output / 'map'
    wor2gltf.export(source, map_dir, resolver)
    scene = world_scene.parse(source.read_bytes())
    category = next(c for c in scene['pathing']['categories'] if c['name'] == 'PATHING_CATEGORY_WALKER')
    width, height = category['grid']
    words = category['painted']['words']
    allowed = bytes((words[i // 32] >> (i % 32)) & 1 for i in range(width * height))
    main_region = connected(allowed, width, height)
    offsets = [(x, z) for z in [0, 2] for x in [0, 2, 4, 14, 16, 18]]
    candidates = [i for i in sorted(main_region) if i % width + 19 < width and i // width + 3 < height
                  and all(i + z * width + x in main_region for x, z in offsets)]
    if not candidates:
        raise ValueError('no connected sandbox spawn area')
    anchor = min(candidates, key=lambda i: ((i % width - 215)**2 + (i // width - 160)**2, i))
    nav = {'width': width, 'height': height, 'cell_size_mm': round(scene['world'].metadata['terrain']['cell_size'] * 1000),
           'allowed_base64': base64.b64encode(allowed).decode('ascii'),
           'source_category': category['name'],
           'policy': 'Prototype interprets the saved painted walker bits as allowed cells; original auxiliary masks/portal queries and mesh entrances are not simulated.'}
    (output / 'navigation.json').write_text(json.dumps(nav, indent=2) + '\n')
    heights = [round(h * 1000) for h in scene['world'].heights]
    (output / 'heights.bin').write_bytes(struct.pack('<%di' % len(heights), *heights))
    models = []
    units_dir = output / 'units'; units_dir.mkdir(exist_ok=True)
    for unit_name in ['UNIT_G_HERO_ARAGORN', 'UNIT_G_ELF_KNIGHT', 'UNIT_E_URUK_HAI']:
        unit = resolver.db['Units'].by_name[unit_name]
        model = resolver.db['Models'].get(unit['Model'])
        target = units_dir / (model.name + '.glb')
        info = convert_model(resolver, model.name, str(target))
        locomotion = prototype_assets.adapt(target)
        clips = info['clips']
        def clip(*terms):
            return next((c for c in clips if all(t.lower() in c.lower() for t in terms)), '')
        models.append({'unit': unit_name, 'file': 'units/' + target.name,
                       'in_place_locomotion': locomotion,
                       'idle': clip('Idle_01'), 'walk': clip('Run'),
                       'attack': clip('Attack', '01'), 'death': clip('Death') or clip('Defeated')})
    spawns = []
    for team in [0, 1]:
        for index in range(6):
            cell = anchor + (index // 3) * 2 * width + (index % 3) * 2 + team * 14
            spawns.append({'team': team, 'cell': cell, 'model': (0 if index == 0 else 1) if team == 0 else 2})
    manifest = {'format_version': 1, 'name': "Helm's Deep — Skirmish Sandbox", 'map': 'map/scene.glb',
                'models': models, 'spawns': spawns, 'camera_cell': anchor + width + 8,
                'rules': 'Prototype: 100 HP, 12 melee damage, 1 second cooldown, 3 m/s movement, integer positions at 20 Hz.',
                'source_sha256': scene['world'].metadata['sha256']}
    files = [output / 'map/scene.glb', output / 'navigation.json', output / 'heights.bin'] + [output / m['file'] for m in models]
    manifest['files'] = {str(p.relative_to(output)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    index = [{'unit': manifest['name'], 'file': manifest['map'], 'clips': []}]
    index.extend({'unit': m['unit'], 'file': m['file'], 'clips': []} for m in models)
    (output / 'index.json').write_text(json.dumps(index, indent=2) + '\n')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=wor.ROOT / 'converted/prototype')
    parser.add_argument('--install', type=Path)
    args = parser.parse_args()
    prepare(args.out, args.install)
    print('Prepared local sandbox cache: ' + str(args.out))
