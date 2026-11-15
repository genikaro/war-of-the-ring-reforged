"""What prepare_prototype.prepare makes of an install's Helm's Deep demo (converted/prototype, build-215), for
tests/formats --python-demo to check the C# builder (prototype/Install/DemoMap.cs) against.

python tools/demo_fingerprint.py "C:/Games/War of the Ring" demo.json
python tools/demo_fingerprint.py "C:/Games/War of the Ring" demo.json --keep data/converted/prototype

The install's archives are extracted into a temporary folder (as pack_fingerprint.py does) and the demo's cache is
built the Python way there. Recorded: the manifest without its file hashes and the list of its files; the scene in
scene_fingerprint's form; heights.bin by its SHA-256 and navigation.json in map_fingerprint's pieces; every JSON file
whole (the combat rules' Sources named as combat_fingerprint names them); every other file in pack_fingerprint's form
(a PNG by its decoded pixels, a GLB in gltf_fingerprint's form, anything else by its bytes). --keep copies the built
cache folder, a fresh Python demo to run the smokes against (run.ps1 -Data). The install is only read.
"""
import argparse
import hashlib
import json
import shutil
import tempfile
from pathlib import Path

import h2o
import resolve_fingerprint
import wor

SKIPPED = ('Movies', 'Dialogue', 'Text', 'Fonts', 'Screens')
WHOLE = ('navigation.json', 'heights.bin', 'map/scene.glb')


def build(install, keep=None):
    import combat_fingerprint
    import combat_rules
    import gltf_fingerprint
    import healing_visual
    import lms2gltf
    import map_fingerprint
    import pack_fingerprint
    import prepare_prototype
    import prototype_interface
    import resolve_model
    import skirmish_economy
    with tempfile.TemporaryDirectory(prefix='reforged-demo-') as root:
        layout = Path(root)
        for archive in sorted((Path(install) / 'Archives').glob('*.H2O')):
            if archive.stem not in SKIPPED:
                h2o.extract(str(archive), str(layout / 'extracted'), verify=True)
        wor.ROOT = combat_rules.ROOT = healing_visual.ROOT = prototype_interface.ROOT = skirmish_economy.ROOT = layout
        lms2gltf.ROOT = resolve_model.ROOT = root
        resolve_model.EXTRACTED = str(layout / 'extracted')
        resolver = resolve_model.Resolver(str(Path(install) / 'Database'), resolve_model.EXTRACTED)
        prepare_prototype.open_resolver = lambda output, install=None: resolver
        output = layout / 'prototype'
        manifest = prepare_prototype.prepare(output)
        files = sorted(manifest.pop('files'))
        nav = json.loads((output / 'navigation.json').read_text())
        gltf = gltf_fingerprint.normalized((output / 'map/scene.glb').read_bytes())
        gltf['extras']['wotrWorld'].pop('source', None)
        whole, fingerprints = {}, {}
        for relative in files:
            if relative in WHOLE:
                continue
            if relative.endswith('.json'):
                value = json.loads((output / relative).read_text(encoding='utf-8'))
                if relative == 'combat/rules.json':
                    value['Sources'] = combat_fingerprint.import_names(value['Sources'], resolver)
                whole[relative] = value
            else:
                fingerprints[relative] = pack_fingerprint.fingerprint(output / relative)
        record = {'format_version': 1, 'manifest': manifest, 'file_list': files,
                  'scene': {key: resolve_fingerprint.canonical_hash(value) for key, value in gltf.items()},
                  'heights': hashlib.sha256((output / 'heights.bin').read_bytes()).hexdigest(),
                  'navigation': map_fingerprint.navigation_record(nav), 'json': whole, 'files': fingerprints}
        if keep:
            shutil.copytree(output, keep, dirs_exist_ok=True)
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out')
    parser.add_argument('--keep', type=Path, help='copy the built cache folder here')
    args = parser.parse_args()
    record = build(args.install, args.keep)
    Path(args.out).write_text(json.dumps(record, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('%d files' % len(record['file_list']))


if __name__ == '__main__':
    main()
