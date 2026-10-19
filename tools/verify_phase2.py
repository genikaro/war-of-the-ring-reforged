"""Audit all shipped map sections and the exported Helm's Deep scene.

python tools/verify_phase2.py
Requires the source install/extraction and the map export; Khronos validation
is optional tooling, but its fresh report is required to pass the scene gate.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

import mpl
import map_decals
from resolve_model import Resolver
import terrain_set
import world_scene
from wor2gltf import load_glb
import wor


def audit(output):
    output = Path(output).resolve()
    if output.is_relative_to(wor.ROOT / 'TLOTR - War of the Ring') or output.is_relative_to(wor.ROOT / 'extracted'):
        raise ValueError('audit output must be outside source folders')
    resolver = Resolver(); db = resolver.db; maps = []; totals = Counter()
    for path in sorted((wor.ROOT / 'extracted/Levels').rglob('*.wor')):
        row = {'source': path.relative_to(wor.ROOT).as_posix()}
        try:
            scene = world_scene.parse(path.read_bytes()); counts = world_scene.resolve_objects(scene, db)
            decals = world_scene.resolve_decals(scene, db)
            decal_surfaces = [map_decals.project(scene['world'], d) for d in decals]
            effects = scene['placements']['other_placeables']
            row.update(sha256=scene['world'].metadata['sha256'], objects=sum(counts.values()),
                       unresolved_objects=counts[None], effects=len(effects),
                       unresolved_effects=sum(e['effect'] is None for e in effects),
                       lakes=len(scene['water']['lakes']),
                       pathing_categories=len(scene['pathing']['categories']),
                       editor_decals=len(scene['tail']['decals']), object_decals=len(decals) - len(scene['tail']['decals']),
                       decal_triangles=sum(len(s['indices']) // 3 for s in decal_surfaces),
                       decals_outside_terrain=sum(not s['indices'] for s in decal_surfaces),
                       missing_decal_textures=sum(not resolver.texture(d['texture_id']).get('exists') for d in decals),
                       secondary_painted_tiles=sum(sum(i != -1 for i in layer['texture_ids']) for layer in scene['terrain']['layers'][1:]),
                       decoded_to_eof=all(s['decoded'] for s in scene['sections']))
            for key in ['objects', 'unresolved_objects', 'effects', 'unresolved_effects', 'lakes', 'pathing_categories',
                        'editor_decals', 'object_decals', 'decal_triangles', 'decals_outside_terrain', 'missing_decal_textures', 'secondary_painted_tiles']:
                totals[key] += row[key]
        except (ValueError, OSError) as error:
            row['error'] = str(error)
        maps.append(row)
    resources = []
    for folder, suffix, reader in [('TerrainSets', '.ter', terrain_set.parse), ('MeshPathLookups', '.mpl', mpl.parse)]:
        for path in sorted((wor.ROOT / 'extracted' / folder).glob('*' + suffix)):
            row = {'source': path.relative_to(wor.ROOT).as_posix()}
            try:
                result = reader(path.read_bytes()) if folder == 'TerrainSets' else mpl.load_resource(path, resolver, strict=False)
                if folder == 'TerrainSets':
                    row['palette_entries'] = len(result['palette'])
                    row['references_valid'] = all(db['TerrainCompositions'].get(e['composition_id']).name == e['composition'] for e in result['palette'])
                else:
                    row['matrices'] = [c['matrix_size'] for c in result['categories']]
                    row['mesh_source'] = result['mesh_source']
                    row['excluded_triangles'] = result['navigation']['excluded_triangles']
                    row['stored_routes_valid'] = result['stored_routes_valid']
                    row['verification'] = result['verification']
                    if not result['stored_routes_valid']:
                        row['source_issue'] = result['stored_route_error']
            except (ValueError, OSError) as error:
                row['error'] = str(error)
            resources.append(row)
    target = wor.ROOT / 'converted/maps/helms-deep'
    scene_check = {}
    try:
        report = json.loads((target / 'report.json').read_text(encoding='utf-8'))
        gltf, _ = load_glb(target / 'scene.glb')
        validation = json.loads((target / 'gltf-validation.json').read_text(encoding='utf-8'))
        digest = hashlib.sha256((target / 'scene.glb').read_bytes()).hexdigest()
        original = next(m for m in maps if m['source'] == 'extracted/Levels/Helms Deep.wor')
        preview = json.loads((target / 'preview.json').read_text(encoding='utf-8'))
        preview_current = preview['glb_sha256'] == digest and all(
            hashlib.sha256((target / filename).read_bytes()).hexdigest() == image_hash
            for filename, image_hash in preview['images'].items())
        objects = [n for n in gltf['nodes'] if 'wotrObjectId' in n.get('extras', {})]
        scene_check = {'objects': len(objects), 'lakes': len(report['water']),
                       'sha256': digest, 'bytes': (target / 'scene.glb').stat().st_size,
                       'source_current': report['source_sha256'] == original['sha256'],
                       'all_objects_present': len(objects) == original['objects'] and len(set(n['extras']['wotrObjectId'] for n in objects)) == len(objects),
                       'all_lakes_present': len(report['water']) == original['lakes'],
                       'all_decals_present': len(report['decals']) == original['editor_decals'] + original['object_decals'],
                       'validation_current': any(f['sha256'] == digest and f['numErrors'] == 0 and f['numWarnings'] == 0 for f in validation['files']),
                       'hidden_navigation_helpers': report.get('hidden_navigation_helpers', []),
                       'preview_current': preview_current}
    except (ValueError, OSError, KeyError, StopIteration) as error:
        scene_check['error'] = str(error)
    required = ['source_current', 'all_objects_present', 'all_lakes_present', 'all_decals_present', 'validation_current', 'preview_current']
    decal_target = wor.ROOT / 'converted/maps/uruk-birth'
    decal_check = {}
    try:
        report = json.loads((decal_target / 'report.json').read_text(encoding='utf-8'))
        original = next(m for m in maps if m['source'] == 'extracted/Levels/DS06_Birth_of_the_UrukHai.wor')
        validation = json.loads((decal_target / 'gltf-validation.json').read_text(encoding='utf-8'))
        preview = json.loads((decal_target / 'preview.json').read_text(encoding='utf-8'))
        digest = hashlib.sha256((decal_target / 'scene.glb').read_bytes()).hexdigest()
        gltf, _ = load_glb(decal_target / 'scene.glb')
        nodes = [n for n in gltf['nodes'] if 'wotrDecalId' in n.get('extras', {})]
        decal_check = {'decals': len(nodes), 'editor_decals': sum(n['extras']['wotrDecalSource'] == 'editor' for n in nodes),
                       'source_current': report['source_sha256'] == original['sha256'],
                       'all_decals_present': len(nodes) == original['editor_decals'] + original['object_decals'] == len(report['decals']),
                       'validation_current': any(f['sha256'] == digest and f['numErrors'] == 0 and f['numWarnings'] == 0 for f in validation['files']),
                       'preview_current': preview['glb_sha256'] == digest and all(
                           hashlib.sha256((decal_target / filename).read_bytes()).hexdigest() == value for filename, value in preview['images'].items())}
    except (ValueError, OSError, KeyError, StopIteration) as error:
        decal_check['error'] = str(error)
    result = {'format_version': 1, 'maps': maps, 'totals': dict(totals), 'resources': resources,
              'maps_decoded': sum('error' not in m for m in maps), 'map_count': len(maps),
              'scene': scene_check,
              'scene_gate_passed': all(scene_check.get(key) for key in required),
              'decal_scene': decal_check,
              'decal_scene_gate_passed': all(decal_check.get(key) for key in ['source_current', 'all_decals_present', 'validation_current', 'preview_current']),
              'phase2_complete': False,
              'source_navigation_issues': [r['source'] for r in resources if 'source_issue' in r],
              'remaining_runtime_work': ['Use decoded geometry to rebuild invalid source bridge routes; never follow their cyclic stored matrices.',
                                         'Implement movement, clearance/avoidance and ground graph query semantics in the chosen engine.',
                                         'Animate decal resizing/fading, water, effects and dynamic light/camera tracks.'],
              'deferred_visual_comparison': 'Owner will capture original-game references after Phase 2; fidelity is not verified.'}
    result['all_map_sections_passed'] = len(maps) == 54 and all('error' not in m and m['decoded_to_eof'] and not m.get('unresolved_objects') and not m.get('unresolved_effects') and not m['missing_decal_textures'] and not m['secondary_painted_tiles'] for m in maps)
    result['all_resources_passed'] = len(resources) == 20 and all('error' not in r and r.get('references_valid', True) for r in resources)
    result['phase2_complete'] = result['all_map_sections_passed'] and result['all_resources_passed'] and result['scene_gate_passed'] and result['decal_scene_gate_passed']
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=wor.ROOT / 'converted/phase2-report.json')
    args = parser.parse_args(); report = audit(args.out)
    print('%d/%d maps; %d objects; scene gate %s' % (report['maps_decoded'], report['map_count'], report['totals'].get('objects', 0), report['scene_gate_passed']))
    raise SystemExit(0 if report['phase2_complete'] else 1)
