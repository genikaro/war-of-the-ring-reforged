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
import prototype_navigation
import prototype_obstacles
import prototype_interface
import combat_rules
from lms2gltf import convert_model
from resolve_model import Resolver
import wor
import verify_walker_masks
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


def demo_layout(allowed, width, height, anchor):
    """Three separated six-unit squads per side, entirely in the main region."""
    region = connected(allowed, width, height)
    offsets = [(x, z) for z in [0, 2] for x in [0, 2, 4]]
    candidates = [cell for cell in sorted(region)
                  if 1 <= cell % width < width - 5 and 1 <= cell // width < height - 3
                  and all((cell // width + z) * width + cell % width + x in region
                          for z in range(-1, 4) for x in range(-1, 6))]
    spawns, groups = [], []
    for band, dz in enumerate([0, -14, 14]):
        for team in [0, 1]:
            px, pz = anchor % width + team * 18, anchor // width + dz
            chosen = None
            for cell in sorted(candidates, key=lambda c: ((c % width - px)**2 + (c // width - pz)**2, c)):
                cells = [cell + z * width + x for x, z in offsets]
                if all((a % width - b['cell'] % width)**2 + (a // width - b['cell'] // width)**2
                       >= (12 if b['team'] != team else 8)**2 for a in cells for b in spawns):
                    chosen = cells
                    break
            if chosen is None:
                raise ValueError('no separated six-squad demo layout in the main region')
            group = len(groups)
            groups.append({'id': group, 'team': team, 'name': ('Blue' if team == 0 else 'Red') + f' squad {band+1}',
                           'anchor': chosen[0]})
            for index, cell in enumerate(chosen):
                spawns.append({'team': team, 'cell': cell, 'group': group,
                               'model': (0 if band == 0 and index == 0 else 1) if team == 0 else 2})
    xs, zs = [s['cell'] % width for s in spawns], [s['cell'] // width for s in spawns]
    camera = ((min(zs) + max(zs)) // 2) * width + (min(xs) + max(xs)) // 2
    return spawns, groups, camera


def refresh_demo(output):
    output = Path(output).resolve()
    if output.is_relative_to(wor.ROOT / 'extracted') or output.is_relative_to(wor.ROOT / 'TLOTR - War of the Ring'):
        raise ValueError('cache must be outside original and extracted data')
    manifest = json.loads((output / 'manifest.json').read_text())
    nav = json.loads((output / 'navigation.json').read_text())
    allowed = base64.b64decode(nav['allowed_base64'])
    anchor = next(s['cell'] for s in manifest['spawns'] if s['team'] == 0)
    manifest['spawns'], manifest['groups'], manifest['camera_cell'] = demo_layout(allowed, nav['width'], nav['height'], anchor)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    return manifest


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
                     'Models', 'Skeletons', 'SoundEffects', 'TerrainSets', 'TextureAnimations', 'Textures', 'UVAs', 'Weights']:
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
    world_scene.resolve_objects(scene, resolver.db)
    category = next(c for c in scene['pathing']['categories'] if c['name'] == 'PATHING_CATEGORY_WALKER')
    mask_audit = verify_walker_masks.audit_category(scene, category)
    if mask_audit.get('region_mismatches', 1):
        raise ValueError('painted walker bits differ from the original walker regions')
    width, height = category['grid']
    words = category['painted']['words']
    allowed = bytes((words[i // 32] >> (i % 32)) & 1 for i in range(width * height))
    source_allowed = allowed
    footprints = prototype_obstacles.placed_footprints(scene, resolver)
    allowed, obstacle_report = prototype_obstacles.ground_mask(width, height,
        scene['world'].metadata['terrain']['cell_size'], allowed, scene['world'].heights, footprints)
    main_region = connected(allowed, width, height)
    offsets = [(x, z) for z in [0, 2] for x in [0, 2, 4, 14, 16, 18]]
    candidates = [i for i in sorted(main_region) if i % width + 19 < width and i // width + 3 < height
                  and all(i + z * width + x in main_region for x, z in offsets)]
    if not candidates:
        raise ValueError('no connected sandbox spawn area')
    anchor = min(candidates, key=lambda i: ((i % width - 215)**2 + (i // width - 160)**2, i))
    nav = {'width': width, 'height': height, 'cell_size_mm': round(scene['world'].metadata['terrain']['cell_size'] * 1000),
           'allowed_base64': base64.b64encode(allowed).decode('ascii'),
           'painted_allowed_base64': base64.b64encode(source_allowed).decode('ascii'),
           'obstacle_report': obstacle_report,
           'source_category': category['name'], 'walker_mask_audit': mask_audit,
           'policy': 'Painted walker bits are allowed cells; they equal the original walker region coverage. Independently removable per-object collision masks. Ground diagonals need both flank cells, unlike corner-only region links.'}
    spacing = scene['world'].metadata['terrain']['cell_size']
    surfaces = prototype_navigation.placed_surfaces(scene, resolver)
    nav.update(prototype_navigation.build(width, height, spacing,
               source_allowed, scene['world'].heights, surfaces))
    nav['picking_surfaces'] = surfaces
    nav['runtime_obstacles'] = prototype_obstacles.runtime_layers(width, height, spacing,
        source_allowed, scene['world'].heights, nav['surface_nodes'], footprints)
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
    spawns, groups, camera_cell = demo_layout(allowed, width, height, anchor)
    manifest = {'format_version': 1, 'name': "Helm's Deep — Skirmish Sandbox", 'map': 'map/scene.glb',
                'team_colors': [[resolver.db['TeamColors'].by_name[name][channel] / 255
                                 for channel in ['Red', 'Green', 'Blue']]
                                for name in ['TEAM_COLOR_BLUE', 'TEAM_COLOR_RED']],
                'models': models, 'spawns': spawns, 'groups': groups, 'camera_cell': camera_cell,
                'rules': 'Imported melee stats and fixed-tick clip hits; see combat/rules.json Policy for approximations.',
                'source_sha256': scene['world'].metadata['sha256']}
    prototype_interface.export(output,resolver)
    combat_rules.export(output,resolver)
    import prototype_clearance
    prototype_clearance.refresh(output)
    manifest['interface'] = 'interface/index.json'
    manifest['combat_rules'] = 'combat/rules.json'
    files = [output / 'map/scene.glb', output / 'navigation.json', output / 'heights.bin'] + [output / m['file'] for m in models]
    files += sorted((output / 'interface').glob('*'))
    files += [p for p in sorted((output/'combat').rglob('*')) if p.is_file()]
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
    parser.add_argument('--demo-only', action='store_true', help='Refresh squads and camera in an existing local cache without reconverting assets')
    parser.add_argument('--ui-only', action='store_true', help='Extract original HUD artwork into an existing cache')
    parser.add_argument('--rules-only', action='store_true', help='Refresh imported combat rules, equipment geometry and impact sounds')
    args = parser.parse_args()
    if args.rules_only:
        if args.install or args.demo_only or args.ui_only:
            parser.error('--rules-only requires an existing cache and no other preparation mode')
        combat_rules.refresh(args.out)
    elif args.ui_only:
        if args.install or args.demo_only:
            parser.error('--ui-only uses an existing cache and cannot be combined with --install or --demo-only')
        prototype_interface.refresh(args.out)
    elif args.demo_only:
        if args.install:
            parser.error('--demo-only uses an existing cache and cannot be combined with --install')
        refresh_demo(args.out)
    else:
        prepare(args.out, args.install)
    print('Prepared local sandbox cache: ' + str(args.out))
