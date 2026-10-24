"""Build the local Godot sandbox cache from the owner's installation.

python tools/prepare_prototype.py
python tools/prepare_prototype.py --install "C:/Games/War of the Ring"
No game files are included in the source project. Source directories are read only.
"""
import argparse
import math
import base64
from collections import deque
import hashlib
import json
from pathlib import Path
import struct

import h2o
import prototype_assets
import skirmish_map
import skirmish_economy
import skirmish_ai
import skirmish_construction
import skirmish_production
import skirmish_research
import prototype_navigation
import prototype_obstacles
import prototype_interface
import combat_rules
import sandbox_map
from lms2gltf import convert_model
from resolve_model import Resolver
import wor
import verify_walker_masks
import world_scene
import wor2gltf


def components(allowed, width, height):
    """4-connected components of the allowed cells, in order of their first cell."""
    remaining = {i for i, value in enumerate(allowed) if value}
    found = []
    while remaining:
        first = min(remaining); remaining.remove(first)
        queue, component = deque([first]), {first}
        while queue:
            cell = queue.popleft(); x, y = cell % width, cell // width
            for nxt in [cell - 1 if x else -1, cell + 1 if x + 1 < width else -1,
                        cell - width if y else -1, cell + width if y + 1 < height else -1]:
                if nxt in remaining:
                    remaining.remove(nxt); component.add(nxt); queue.append(nxt)
        found.append(component)
    return found


def connected(allowed, width, height):
    """The largest component (the first of equal ones)."""
    best = set()
    for component in components(allowed, width, height):
        if len(component) > len(best):
            best = component
    return best


def start_regions(allowed, width, height, spacing, centres, ring=skirmish_map.WORKER_RING):
    """Ground the start bases stand on: for each base, the largest component reaching its worker ring. River maps
    such as MP04_Mighty_Silverlode split the ground into halves, so the largest component alone misses bases."""
    found = components(allowed, width, height)
    owner = {cell: index for index, component in enumerate(found) for cell in component}
    region = set()
    for cx, cz in centres:
        near = {owner[cell] for cell in owner
                if ring[0] <= math.hypot(cell % width + .5 - cx/spacing, cell // width + .5 - cz/spacing)*spacing <= ring[1]}
        if not near:
            raise ValueError('no ground around the start point at %.1f, %.1f' % (cx, cz))
        region |= found[max(near, key=lambda index: (len(found[index]), -index))]
    return region


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
                               'model': (3 if band == 2 else 0 if band == 0 and index == 0 else 1) if team == 0 else 2})
    xs, zs = [s['cell'] % width for s in spawns], [s['cell'] // width for s in spawns]
    camera = ((min(zs) + max(zs)) // 2) * width + (min(xs) + max(xs)) // 2
    return spawns, groups, camera


def refresh_demo(output):
    output = Path(output).resolve()
    if output.is_relative_to(wor.ROOT / 'extracted') or output.is_relative_to(wor.ROOT / 'TLOTR - War of the Ring'):
        raise ValueError('cache must be outside original and extracted data')
    manifest = json.loads((output / 'manifest.json').read_text())
    if [m['unit'] for m in manifest['models']] != combat_rules.UNITS:
        raise ValueError('unit roster changed; refresh the cache with --units-only first')
    nav = json.loads((output / 'navigation.json').read_text())
    allowed = base64.b64decode(nav['allowed_base64'])
    anchor = next(s['cell'] for s in manifest['spawns'] if s['team'] == 0)
    manifest['spawns'], manifest['groups'], manifest['camera_cell'] = demo_layout(allowed, nav['width'], nav['height'], anchor)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    return manifest


def export_units(output, resolver, roster=None):
    models = []
    units_dir = output / 'units'; units_dir.mkdir(exist_ok=True)
    for unit_name in roster or combat_rules.UNITS:
        unit = resolver.db['Units'].by_name[unit_name]
        model = resolver.db['Models'].get(unit['Model'])
        target = units_dir / (model.name + '.glb')
        info = convert_model(resolver, model.name, str(target))
        locomotion = prototype_assets.adapt(target, center_pivot=unit_name in combat_rules.RANGED_UNITS)
        clips = info['clips']
        def clip(*terms):
            return next((c for c in clips if all(t.lower() in c.lower() for t in terms)), '')
        models.append({'unit': unit_name, 'file': 'units/' + target.name,
                       'in_place_locomotion': locomotion,
                       'idle': clip('Idle_01'), 'walk': clip('Run'),
                       'attack': clip('Attack', '01'), 'death': clip('Death') or clip('Defeated')})
    return models


def refresh_units(output):
    output = Path(output).resolve()
    if any(output.is_relative_to(wor.ROOT/p) for p in ['extracted', 'TLOTR - War of the Ring']):
        raise ValueError('cache must be outside original and extracted data')
    path = output/'manifest.json'
    manifest = json.loads(path.read_text())
    skirmish = manifest.get('mode') == 'skirmish'
    import content_pack
    content_pack.reject_slim_cache(manifest)
    manifest['models'] = export_units(output, Resolver(), SKIRMISH_ROSTER if skirmish else None)
    for model in manifest['models']:
        manifest['files'][model['file']] = hashlib.sha256((output/model['file']).read_bytes()).hexdigest()
    path.write_text(json.dumps(manifest,indent=2)+'\n')
    index = [{'unit': manifest['name'], 'file': manifest['map'], 'clips': []}]
    index.extend({'unit': m['unit'], 'file': m['file'], 'clips': []} for m in manifest['models'])
    (output/'index.json').write_text(json.dumps(index,indent=2)+'\n')
    combat_rules.refresh(output)
    prototype_interface.refresh(output)
    if skirmish:
        # The roster decides which source-listed units each producer can train.
        manifest = json.loads(path.read_text())
        manifest['skirmish']['production'] = skirmish_production.production_data(Resolver(), [m['unit'] for m in manifest['models']])
        path.write_text(json.dumps(manifest,indent=2)+'\n')
    return manifest if skirmish else refresh_demo(output)


def open_resolver(output, install=None):
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
                     'Models', 'Projectiles', 'Skeletons', 'SoundEffects', 'TerrainSets', 'TextureAnimations', 'Textures', 'UVAs', 'Weights']:
            source = install / 'Archives' / (name + '.H2O')
            # Validate paths before using the archive extractor.
            archive = h2o.H2OArchive(str(source))
            destination = (wor.ROOT / 'extracted' / name).resolve()
            for entry in archive.entries:
                target = (destination / entry.path.replace('\\', '/')).resolve()
                if not target.is_relative_to(destination):
                    raise ValueError('archive path escapes extraction directory')
            h2o.extract(str(source), str(wor.ROOT / 'extracted'), verify=True)
    return Resolver(str(db_path)) if db_path else Resolver()


# Player slot colours in slot order, one per start point of the largest (8-player) maps. The source table also has
# white TEAM_COLOR_GRAY; blue and red keep the first two slots as before. The order is an adapter: the original lobby
# lets each player pick.
SLOT_COLORS = ['TEAM_COLOR_BLUE', 'TEAM_COLOR_RED', 'TEAM_COLOR_GREEN', 'TEAM_COLOR_PURPLE', 'TEAM_COLOR_YELLOW',
               'TEAM_COLOR_CYAN', 'TEAM_COLOR_ORANGE', 'TEAM_COLOR_PINK']


def team_colors(resolver):
    return [[resolver.db['TeamColors'].by_name[name][channel] / 255
             for channel in ['Red', 'Green', 'Blue']]
            for name in SLOT_COLORS]


def walker_category(scene):
    return next(c for c in scene['pathing']['categories'] if c['name'] == 'PATHING_CATEGORY_WALKER')


def start_ground(scene):
    """Cells a start Citadel may cover: source building passability. Walker bits and scenery are not checked: several
    start points (MP04_Spire_of_Mordor, MP07_Choke) already put the Citadel over unpainted walker cells or scenery."""
    category = walker_category(scene)
    width, height = category['grid']
    building = next((c for c in scene['pathing']['categories'] if c['name'] == 'PATHING_CATEGORY_BUILDING' and 'painted' in c), None)
    if not building or building['grid'] != category['grid']:
        return bytes([1]) * (width * height)
    bits = building['painted']['words']
    return bytes((bits[i // 32] >> (i % 32)) & 1 for i in range(width * height))


def build_navigation(scene, resolver, output, extra_footprints=(), inactive_obstacles=(), precise=False, audit=True):
    """Write navigation.json and heights.bin; shared by the demo and skirmish maps. The Sandbox skips the walker audit:
    it repaints every cell walkable, so the source region graphs no longer describe it."""
    category = walker_category(scene)
    mask_audit = verify_walker_masks.audit_category(scene, category) if audit else {'skipped': 'Sandbox: every cell repainted walkable'}
    if audit and mask_audit.get('region_mismatches', 1):
        raise ValueError('painted walker bits differ from the original walker regions')
    width, height = category['grid']
    words = category['painted']['words']
    allowed = bytes((words[i // 32] >> (i % 32)) & 1 for i in range(width * height))
    source_allowed = allowed
    footprints = prototype_obstacles.placed_footprints(scene, resolver) + list(extra_footprints)
    allowed, obstacle_report = prototype_obstacles.ground_mask(width, height,
        scene['world'].metadata['terrain']['cell_size'], allowed, scene['world'].heights,
        [f for f in footprints if f['object_id'] not in inactive_obstacles], precise=precise)
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
        source_allowed, scene['world'].heights, nav['surface_nodes'], footprints, precise=precise)
    if precise:
        nav['footprint_collision'] = 'swept-circle-v1'
    building = next((c for c in scene['pathing']['categories'] if c['name'] == 'PATHING_CATEGORY_BUILDING' and 'painted' in c), None)
    if building and building['grid'] == category['grid']:
        # Source building passability is separate from (and stricter on slopes than) the walker grid.
        bits = building['painted']['words']
        nav['building_allowed_base64'] = base64.b64encode(bytes((bits[i // 32] >> (i % 32)) & 1 for i in range(width * height))).decode('ascii')
    if inactive_obstacles:
        nav['inactive_obstacles'] = sorted(inactive_obstacles)
    (output / 'navigation.json').write_text(json.dumps(nav, indent=2) + '\n')
    heights = [round(h * 1000) for h in scene['world'].heights]
    (output / 'heights.bin').write_bytes(struct.pack('<%di' % len(heights), *heights))
    return nav, allowed, width, height


def finish_cache(output, resolver, manifest, models, extra_files=()):
    """Interface art, combat rules, body clearance, hashes and the file index."""
    prototype_interface.export(output, resolver)
    combat_rules.export(output, resolver, roster=[m['unit'] for m in models])
    import prototype_clearance
    prototype_clearance.refresh(output)
    manifest['interface'] = 'interface/index.json'
    manifest['combat_rules'] = 'combat/rules.json'
    files = [output / 'map/scene.glb', output / 'navigation.json', output / 'heights.bin'] + [output / m['file'] for m in models]
    files += [output / f for f in extra_files]
    files += sorted((output / 'interface').glob('*'))
    files += [p for p in sorted((output/'combat').rglob('*')) if p.is_file()]
    manifest['files'] = {str(p.relative_to(output)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    index = [{'unit': manifest['name'], 'file': manifest['map'], 'clips': []}]
    index.extend({'unit': m['unit'], 'file': m['file'], 'clips': []} for m in models)
    (output / 'index.json').write_text(json.dumps(index, indent=2) + '\n')


def prepare(output, install=None):
    output = Path(output).resolve()
    resolver = open_resolver(output, install)
    source = wor.ROOT / 'extracted/Levels/Helms Deep.wor'
    output.mkdir(parents=True, exist_ok=True)
    map_dir = output / 'map'
    wor2gltf.export(source, map_dir, resolver)
    scene = world_scene.parse(source.read_bytes())
    world_scene.resolve_objects(scene, resolver.db)
    nav, allowed, width, height = build_navigation(scene, resolver, output)
    main_region = connected(allowed, width, height)
    offsets = [(x, z) for z in [0, 2] for x in [0, 2, 4, 14, 16, 18]]
    candidates = [i for i in sorted(main_region) if i % width + 19 < width and i // width + 3 < height
                  and all(i + z * width + x in main_region for x, z in offsets)]
    if not candidates:
        raise ValueError('no connected sandbox spawn area')
    anchor = min(candidates, key=lambda i: ((i % width - 215)**2 + (i // width - 160)**2, i))
    models = export_units(output, resolver)
    spawns, groups, camera_cell = demo_layout(allowed, width, height, anchor)
    manifest = {'format_version': 1, 'name': "Helm's Deep — Skirmish Sandbox", 'map': 'map/scene.glb',
                'team_colors': team_colors(resolver),
                'models': models, 'spawns': spawns, 'groups': groups, 'camera_cell': camera_cell,
                'rules': 'Imported combat stats, fixed-tick melee hits and Elven Archer projectiles; see combat/rules.json Policy for approximations.',
                'source_sha256': scene['world'].metadata['sha256']}
    finish_cache(output, resolver, manifest, models)
    return manifest


SKIRMISH_ROSTER = combat_rules.UNITS + skirmish_map.SKIRMISH_UNITS
CITADEL_OBJECT_BASE = 10000


def skirmish_cache(map_name=None):
    """Default cache folder: converted/skirmish for Bilbo's Bluffs, converted/skirmish-maps/<stem> for any other map."""
    file = skirmish_map.map_file(map_name)
    return wor.ROOT / ('converted/skirmish' if file == skirmish_map.MAP_FILE else 'converted/skirmish-maps/' + Path(file).stem)


SANDBOX_CACHE = wor.ROOT / 'converted/skirmish-maps' / sandbox_map.STEM


def prepare_skirmish(output, install=None, map_name=None, content=None, sandbox=False):
    """A multiplayer map (default Bilbo's Bluffs) with a Good Citadel and four workers on every start point (build-4,
    any map since build-35). Since build-48 the cache holds only the map: terrain, navigation, resources and start
    layout. Units, buildings, rules and HUD art come from the shared content pack it points to (content_pack.py).
    sandbox builds the Sandbox test map instead, an empty level field cut from Bilbo's Bluffs (sandbox_map.py, build-56)."""
    import content_pack
    import os
    output = Path(output).resolve()
    content = Path(content or content_pack.PACK).resolve()
    pack = content_pack.load(content)
    resolver = open_resolver(output, install)
    map_file = skirmish_map.map_file(map_name)
    source = wor.ROOT / 'extracted/Levels' / map_file
    if not source.is_file():
        raise ValueError('no such multiplayer map: ' + map_file)
    data = source.read_bytes()
    output.mkdir(parents=True, exist_ok=True)
    # Faction content lived in every map cache before build-48; the pack holds it now.
    for stale in ['units', 'buildings', 'objects', 'combat', 'interface']:
        for path in sorted((output / stale).rglob('*'), reverse=True) if (output / stale).is_dir() else []:
            path.unlink() if path.is_file() else path.rmdir()
        if (output / stale).is_dir():
            (output / stale).rmdir()
    wor2gltf.export(source, output / 'map', resolver, (lambda s: sandbox_map.flatten(s, resolver)) if sandbox else None)
    scene = world_scene.parse(data)
    world_scene.resolve_objects(scene, resolver.db)
    metadata = scene['world'].metadata
    points = sandbox_map.flatten(scene, resolver) if sandbox else skirmish_map.start_points(skirmish_map.parse_markers(data, metadata, metadata['name_table']['names']))
    if not 2 <= len(points) <= len(SLOT_COLORS):
        raise ValueError('expected 2 to %d start points, found %d' % (len(SLOT_COLORS), len(points)))
    width, _ = walker_category(scene)['grid']
    spacing, heights = metadata['terrain']['cell_size'], scene['world'].heights
    buildable, resources, radii = start_ground(scene),skirmish_map.resource_nodes(scene), skirmish_map.placement_radii(resolver)
    bases = []
    for team, point in enumerate(points):
        x, y, _ = point['position']
        marker, _ = skirmish_map.building_footprint(resolver, skirmish_map.CITADEL, (x, y, 0), CITADEL_OBJECT_BASE + team)
        dx, dy = skirmish_map.start_position(marker['polygon'], (x, y), resources, radii, buildable, width, len(buildable)//width, spacing)
        x, y = x+dx, y+dy
        z = prototype_navigation.terrain_height(x, y, width, spacing, heights)
        footprint, _ = skirmish_map.building_footprint(resolver, skirmish_map.CITADEL, (x, y, z), CITADEL_OBJECT_BASE + team)
        footprint['name'] = 'Start Citadel %d' % team
        bases.append({'team': team, 'start_point': point['name'], 'position': [x, y, z], 'footprint': footprint})
    construction, site_footprints = skirmish_construction.construction_data(resolver, skirmish_map.resource_nodes(scene))
    nav, allowed, width, height = build_navigation(scene, resolver, output, [b['footprint'] for b in bases]+site_footprints,
        [s['obstacle'] for s in construction['sites']], precise=True, audit=not sandbox)
    # Navigation for every body either faction can field, so new units never need a map re-import.
    import prototype_clearance
    prototype_clearance.refresh(output, pack['bodies'])
    region = start_regions(allowed, width, height, spacing, [b['position'][:2] for b in bases])
    ground = bytes(1 if allowed[i] and i in region else 0 for i in range(width * height))
    spawns, groups = [], []
    for base in bases:
        cells = skirmish_map.worker_cells(ground, width, height, spacing, base['position'])
        base['worker_cells'] = cells
        groups.append({'id': base['team'], 'team': base['team'], 'name': 'Player %d workers' % (base['team'] + 1),
                       'anchor': cells[0]})
        spawns.extend({'team': base['team'], 'cell': cell, 'group': base['team'], 'unit': 'UNIT_G_HUMAN_WORKER'} for cell in cells)
    nodes = skirmish_map.resource_nodes(scene)
    near = skirmish_map.assign_resources(nodes, points)
    for node in nodes:
        node['start_team'] = next((team for team, ids in enumerate(near) if node['id'] in ids.values()), None)
    x, y, _ = bases[0]['position']
    camera_cell = int(y / spacing) * width + int(x / spacing)
    files = [output / 'map/scene.glb', output / 'navigation.json', output / 'heights.bin']
    title = sandbox_map.TITLE if sandbox else skirmish_map.map_title(map_file)
    manifest = {'format_version': 1, 'mode': 'skirmish', 'name': title + ' — Good Skirmish', 'map': 'map/scene.glb',
                'map_file': map_file, 'players': len(bases),
                'content': Path(os.path.relpath(content, output)).as_posix(),
                'spawns': spawns, 'groups': groups, 'camera_cell': camera_cell,
                'skirmish': {'resources': nodes, 'construction': {'sites': construction['sites']},
                             'selectable_objects': skirmish_map.selectable_objects(scene, resolver),
                             'bases': [{'team': b['team'], 'start_point': b['start_point'], 'position': b['position'],
                                        'building': skirmish_map.CITADEL, 'footprint_object': b['footprint']['object_id'],
                                        'worker_cells': b['worker_cells']} for b in bases]},
                'source_sha256': metadata['sha256'],
                'files': {p.relative_to(output).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
    if sandbox:
        # The game reads this flag: both players are the person's, with every building standing (Main.SandboxMap.cs).
        manifest['sandbox'] = True
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (output / 'index.json').write_text(json.dumps([{'unit': manifest['name'], 'file': manifest['map'], 'clips': []}], indent=2) + '\n')
    return manifest


def prepare_all_maps(install=None):
    """Every multiplayer map into its own cache; one map's failure is reported and the batch goes on."""
    failed = []
    for source in sorted((wor.ROOT / 'extracted/Levels/Multiplayer').glob('MP*.wor')):
        try:
            manifest = prepare_skirmish(skirmish_cache(source.stem), install, source.stem)
            print('%s: %d players' % (source.stem, manifest['players']), flush=True)
        except Exception as error:  # noqa: BLE001 - the summary names every failed map
            print('%s: FAILED %s' % (source.stem, error), flush=True)
            failed.append(source.stem)
    try:
        prepare_skirmish(SANDBOX_CACHE, install, sandbox=True)
        print('%s: 2 players' % sandbox_map.STEM, flush=True)
    except Exception as error:  # noqa: BLE001 - the summary names every failed map
        print('%s: FAILED %s' % (sandbox_map.STEM, error), flush=True)
        failed.append(sandbox_map.STEM)
    return failed


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path)
    parser.add_argument('--skirmish', action='store_true', help="Build a skirmish cache (default Bilbo's Bluffs in converted/skirmish)")
    parser.add_argument('--map', help='Multiplayer map stem for --skirmish, e.g. MP04_Wasteland (default folder converted/skirmish-maps/<stem>)')
    parser.add_argument('--all-maps', action='store_true', help='Build a skirmish cache for every multiplayer map, each in its default folder')
    parser.add_argument('--sandbox', action='store_true', help='Build the Sandbox test map, an empty level field (default folder converted/skirmish-maps/Sandbox)')
    parser.add_argument('--install', type=Path)
    parser.add_argument('--demo-only', action='store_true', help='Refresh squads and camera in an existing local cache without reconverting assets')
    parser.add_argument('--ui-only', action='store_true', help='Extract original HUD artwork into an existing cache')
    parser.add_argument('--rules-only', action='store_true', help='Refresh imported combat rules, equipment geometry and impact sounds')
    parser.add_argument('--units-only', action='store_true', help='Refresh unit models, combat rules, portraits and demo squads in an existing cache')
    args = parser.parse_args()
    if args.sandbox and (args.map or args.skirmish or args.all_maps or args.demo_only or args.ui_only or args.rules_only or args.units_only):
        parser.error('--sandbox builds the Sandbox cache; combine it only with --out and --install')
    if args.out is None:
        args.out = SANDBOX_CACHE if args.sandbox else skirmish_cache(args.map) if args.skirmish else wor.ROOT / 'converted/prototype'
    if args.map and not args.skirmish:
        parser.error('--map requires --skirmish')
    if args.all_maps and (args.map or args.skirmish or args.demo_only or args.ui_only or args.rules_only or args.units_only):
        parser.error('--all-maps builds every map cache; combine it only with --install')
    if args.skirmish and (args.demo_only or args.ui_only or args.rules_only):
        parser.error('--skirmish builds a full cache; combine it only with --install')
    if args.units_only:
        if args.install or args.demo_only or args.ui_only or args.rules_only:
            parser.error('--units-only requires an existing cache and no other preparation mode')
        refresh_units(args.out)
    elif args.rules_only:
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
    elif args.all_maps:
        failed = prepare_all_maps(args.install)
        if failed:
            raise SystemExit('%d maps failed: %s' % (len(failed), ', '.join(failed)))
    elif args.sandbox:
        prepare_skirmish(args.out, args.install, sandbox=True)
    elif args.skirmish:
        prepare_skirmish(args.out, args.install, args.map)
    else:
        prepare(args.out, args.install)
    if not args.all_maps:
        print('Prepared local sandbox cache: ' + str(args.out))
