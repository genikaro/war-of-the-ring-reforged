"""What prepare_prototype.prepare_skirmish makes of every multiplayer map of an install (build-209), for tests/formats
--python-maps to check the C# map builders (prototype/Install/PackMap.cs, MapNavigation.cs) against.

python tools/map_fingerprint.py "C:/Games/War of the Ring" maps.json --bodies converted/content/good/content.json
python tools/map_fingerprint.py "C:/Games/War of the Ring" --dump MP02_Sorrow nav.json --bodies ...

Each map's cache is built the Python way from a layout of the install (resolve_fingerprint.lay_out, with the levels,
meshes and mesh path lookups read too) into a temporary folder, the content pack's collision bodies taken from the
given content.json. The scene export (wor2gltf) and the sky and cloud images are left out (build-210 checks them);
the lighting block still names them. Recorded per map: the manifest without its file hashes and pack path, the
SHA-256 of heights.bin, and navigation.json in pieces: each top-level value, each runtime obstacle layer and each
body profile's nodes, edges and obstacle layers hashed in a value form (numbers by their double value, -0 as 0, so an
integer and an integral float agree; keys sorted). --dump writes one map's navigation.json and manifest whole. The
install is only read.
"""
import argparse
import hashlib
import json
import os
import struct
import tempfile
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import h2o
import resolve_fingerprint

resolve_fingerprint.READ = ('Models', 'Skeletons', 'Cloths', 'UVAs', 'Meshes', 'MeshPathLookups', 'Levels')
resolve_fingerprint.LISTED = ('Weights', 'Textures', 'Effects')

_state = {}


def digest(value, out):
    if value is None:
        out.append('n')
    elif value is True or value is False:
        out.append('t' if value else 'f')
    elif isinstance(value, (int, float)):
        number = float(value)
        out.append('d%016x' % struct.unpack('<Q', struct.pack('<d', 0.0 if number == 0 else number))[0])
    elif isinstance(value, str):
        out.append('s' + value.encode('utf-8').hex())
    elif isinstance(value, list):
        out.append('[')
        for item in value:
            digest(item, out)
            out.append(',')
        out.append(']')
    elif isinstance(value, dict):
        out.append('{')
        for key in sorted(value):
            digest(key, out)
            out.append(':')
            digest(value[key], out)
            out.append(',')
        out.append('}')
    else:
        raise TypeError(type(value))


def hashed(value):
    out = []
    digest(value, out)
    return hashlib.sha256(''.join(out).encode('ascii')).hexdigest()


def navigation_record(nav):
    record = {key: hashed(value) for key, value in nav.items() if key not in ('runtime_obstacles', 'body_profiles')}
    record['runtime_obstacles'] = [hashed(layer) for layer in nav.get('runtime_obstacles', [])]
    record['body_profiles'] = [{'radius_mm': p['radius_mm'], 'height_mm': p['height_mm'], 'nodes': hashed(p['nodes']), 'edges': hashed(p['edges']),
                                'obstacles': [hashed(layer) for layer in p['obstacles']]} for p in nav.get('body_profiles', [])]
    return record


def _start(install, root, bodies):
    import map_lighting
    import prepare_prototype
    import resolve_model
    import wor
    import wor2gltf
    resolver = resolve_fingerprint.resolver_for(install, root)
    wor.ROOT = Path(root)
    prepare_prototype.open_resolver = lambda output, install=None: resolver

    def no_scene(source, output, *args, **kwargs):
        """The scene is left out; an empty file stands in for the one prepare_skirmish hashes."""
        Path(output).mkdir(parents=True, exist_ok=True)
        (Path(output) / 'scene.glb').write_bytes(b'')
    wor2gltf.export = no_scene

    def names_only(block, output, resolver):
        """write_images' names without the pictures."""
        block['clouds']['texture'] = 'map/clouds.png'
        sky = block['sky']
        record = resolver.db['Skyboxes'].get(sky['index']) if sky['skybox'] else None
        if record:
            sky['name'] = record.name
            for face in map_lighting.SKY_FACES:
                sky[face] = 'map/sky-%s.png' % face
        return []
    map_lighting.write_images = names_only
    content = Path(root) / 'content'
    content.mkdir(exist_ok=True)
    (content / 'content.json').write_text(json.dumps({'format_version': 1, 'bodies': bodies}))
    _state.update(root=root, content=content)


def build(stem):
    import prepare_prototype
    output = Path(tempfile.mkdtemp(prefix='map-', dir=_state['root']))
    # Build-211: the Sandbox is cut from Bilbo's Bluffs (sandbox_map.py).
    sandbox = stem == 'Sandbox'
    manifest = prepare_prototype.prepare_skirmish(output, None, None if sandbox else stem, _state['content'], sandbox=sandbox)
    manifest.pop('files', None)
    manifest.pop('content', None)
    nav = json.loads((output / 'navigation.json').read_text())
    heights = hashlib.sha256((output / 'heights.bin').read_bytes()).hexdigest()
    return manifest, nav, heights


def _fingerprint(stem):
    try:
        manifest, nav, heights = build(stem)
        return stem, {'manifest': manifest, 'heights': heights, 'navigation': navigation_record(nav)}
    except Exception as failure:  # noqa: BLE001 - the map's answer is that it does not build
        return stem, {'error': '%s: %s' % (type(failure).__name__, failure)}


def stems(install):
    archive = h2o.H2OArchive(str(Path(install) / 'Archives' / 'Levels.H2O'))
    paths = [e.path.replace('\\', '/') for e in archive.entries]
    return sorted((Path(p).stem for p in paths if p.lower().startswith('multiplayer/') and p.lower().endswith('.wor')
                   and Path(p).name.upper().startswith('MP')), key=str.lower)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out', nargs='?')
    parser.add_argument('--bodies', required=True, help="a content.json whose 'bodies' the clearance profiles use")
    parser.add_argument('--dump', nargs=2, metavar=('STEM', 'OUT'), help="write one map's navigation.json and manifest whole")
    parser.add_argument('--maps', nargs='*', help='only these map stems')
    parser.add_argument('--workers', type=int)
    args = parser.parse_args()
    bodies = json.loads(Path(args.bodies).read_text())['bodies']
    with tempfile.TemporaryDirectory(prefix='reforged-maps-') as root:
        resolve_fingerprint.lay_out(args.install, root)
        if args.dump:
            _start(args.install, root, bodies)
            manifest, nav, heights = build(args.dump[0])
            Path(args.dump[1]).write_text(json.dumps({'manifest': manifest, 'heights': heights, 'navigation': nav}, indent=1) + '\n', encoding='utf-8')
            return
        chosen = args.maps or stems(args.install) + ['Sandbox']
        def size(stem):
            return os.path.getsize(os.path.join(root, 'extracted', 'Levels', 'Multiplayer', ('MP02_Bilbos_Bluffs' if stem == 'Sandbox' else stem) + '.wor'))
        with ProcessPoolExecutor(args.workers, initializer=_start, initargs=(args.install, root, bodies)) as pool:
            maps = dict(pool.map(_fingerprint, sorted(chosen, key=lambda s: -size(s))))
    Path(args.out).write_text(json.dumps({'format_version': 1, 'bodies': bodies, 'maps': dict(sorted(maps.items()))}, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('%d maps, %d that do not build' % (len(maps), sum('error' in m for m in maps.values())))


if __name__ == '__main__':
    main()
