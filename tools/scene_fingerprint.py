"""What wor2gltf.export and map_lighting.write_images make of every multiplayer map of an install (build-210), for
tests/formats --python-scenes to check the C# MapScene and MapPictures against.

python tools/scene_fingerprint.py "C:/Games/War of the Ring" scenes.json
python tools/scene_fingerprint.py "C:/Games/War of the Ring" --dump MP02_Sorrow scene.json

The install's archives are extracted into a temporary folder (as pack_fingerprint.py does) and each map's scene is
exported the Python way. The GLB is recorded in gltf_fingerprint's form (each accessor's bytes and each image's
decoded pixels by SHA-256, without the buffer views), its extras' source path left out, each top-level key hashed in
models_fingerprint's canonical form; the cloud map and skybox faces by their decoded pixels, size and mode. --dump
writes one map's normalized scene whole. The install is only read.
"""
import argparse
import hashlib
import io
import json
import tempfile
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

from PIL import Image

import h2o
import resolve_fingerprint

_state = {}


def _start(install, root):
    import lms2gltf
    import resolve_model
    import wor
    wor.ROOT = Path(root)
    lms2gltf.ROOT = root
    resolve_model.ROOT = str(root)
    resolve_model.EXTRACTED = str(Path(root) / 'extracted')
    _state['resolver'] = resolve_model.Resolver(str(Path(install) / 'Database'), resolve_model.EXTRACTED)
    _state['root'] = root


def normalized_scene(stem):
    import gltf_fingerprint
    import map_lighting
    import world_scene
    import wor
    import wor2gltf
    import sandbox_map
    output = Path(tempfile.mkdtemp(prefix='scene-', dir=_state['root']))
    # Build-211: the Sandbox is Bilbo's Bluffs flattened (sandbox_map.flatten), as prepare_skirmish exports it.
    sandbox = stem == 'Sandbox'
    source = wor.ROOT / 'extracted/Levels/Multiplayer' / (('MP02_Bilbos_Bluffs' if sandbox else stem) + '.wor')
    resolver = _state['resolver']
    wor2gltf.export(source, output / 'map', resolver, (lambda s: sandbox_map.flatten(s, resolver)) if sandbox else None)
    gltf = gltf_fingerprint.normalized((output / 'map/scene.glb').read_bytes())
    gltf['extras']['wotrWorld'].pop('source', None)
    block = map_lighting.lighting(world_scene.parse(source.read_bytes()))
    pictures = {}
    for path in map_lighting.write_images(block, output, _state['resolver']):
        with Image.open(path) as picture:
            pictures[path.relative_to(output).as_posix()] = {'pixels': hashlib.sha256(picture.tobytes()).hexdigest(),
                                                             'size': [picture.width, picture.height], 'mode': picture.mode}
    return gltf, pictures


def _fingerprint(stem):
    try:
        gltf, pictures = normalized_scene(stem)
        return stem, {'scene': {key: resolve_fingerprint.canonical_hash(value) for key, value in gltf.items()}, 'pictures': pictures}
    except Exception as failure:  # noqa: BLE001 - the map's answer is that it does not export
        return stem, {'error': '%s: %s' % (type(failure).__name__, failure)}


def stems(install):
    archive = h2o.H2OArchive(str(Path(install) / 'Archives' / 'Levels.H2O'))
    return sorted((Path(p).stem for p in (e.path.replace('\\', '/') for e in archive.entries)
                   if p.lower().startswith('multiplayer/') and p.lower().endswith('.wor') and Path(p).name.upper().startswith('MP')), key=str.lower)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out', nargs='?')
    parser.add_argument('--dump', nargs=2, metavar=('STEM', 'OUT'), help="write one map's normalized scene whole")
    parser.add_argument('--maps', nargs='*')
    parser.add_argument('--workers', type=int)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='reforged-scenes-') as root:
        for archive in sorted((Path(args.install) / 'Archives').glob('*.H2O')):
            if archive.stem not in ('Movies', 'Dialogue', 'Text', 'Fonts', 'Screens', 'Music', 'SpeechFX', 'SoundEffects'):
                h2o.extract(str(archive), str(Path(root) / 'extracted'), verify=True)
        if args.dump:
            _start(args.install, root)
            gltf, pictures = normalized_scene(args.dump[0])
            Path(args.dump[1]).write_text(json.dumps({'scene': gltf, 'pictures': pictures}, indent=1) + '\n', encoding='utf-8')
            return
        with ProcessPoolExecutor(args.workers, initializer=_start, initargs=(args.install, root)) as pool:
            scenes = dict(pool.map(_fingerprint, args.maps or stems(args.install) + ['Sandbox']))
    Path(args.out).write_text(json.dumps({'format_version': 1, 'maps': scenes}, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('%d maps, %d that do not export' % (len(scenes), sum('error' in m for m in scenes.values())))


if __name__ == '__main__':
    main()
