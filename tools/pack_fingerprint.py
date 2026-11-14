"""Every file the Python import writes into the content pack, built from an install, for tests/formats --python-files
to check the C# builders against (build-205).

python tools/pack_fingerprint.py "C:/Games/War of the Ring" files.json

Builds the whole content pack (content_pack.build) from the install's archives, extracted into a temporary folder, and
records each file it writes: a PNG by the SHA-256 of its decoded pixels with its size and mode (PNG bytes differ
between encoders, pixels may not), a GLB in gltf_fingerprint's canonical form, anything else (sounds, music, JSON) by
the SHA-256 of its bytes. Also writes the pack's audio and interface indexes whole. The install is only read.
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
import wor


def fingerprint(path):
    data = path.read_bytes()
    suffix = path.suffix.lower()
    if suffix == '.png':
        with Image.open(io.BytesIO(data)) as picture:
            return {'pixels': hashlib.sha256(picture.tobytes()).hexdigest(), 'size': [picture.width, picture.height], 'mode': picture.mode}
    if suffix == '.glb':
        import gltf_fingerprint
        return {'gltf': resolve_fingerprint.canonical_hash(gltf_fingerprint.normalized(data))}
    return {'sha256': hashlib.sha256(data).hexdigest()}


def _fingerprint(item):
    output, relative = item
    return relative, fingerprint(Path(output) / relative)


def build(install, workers=None):
    import combat_rules
    import content_pack
    import healing_visual
    import lms2gltf
    import prepare_prototype
    import prototype_interface
    import resolve_model
    import skirmish_economy
    with tempfile.TemporaryDirectory(prefix='reforged-pack-') as root:
        layout = Path(root)
        for archive in sorted((Path(install) / 'Archives').glob('*.H2O')):
            if archive.stem not in ('Movies', 'Dialogue', 'Text', 'Levels', 'Fonts', 'Screens'):
                h2o.extract(str(archive), str(layout / 'extracted'), verify=True)
        wor.ROOT = combat_rules.ROOT = healing_visual.ROOT = prototype_interface.ROOT = skirmish_economy.ROOT = layout
        lms2gltf.ROOT = resolve_model.ROOT = root
        resolve_model.EXTRACTED = str(layout / 'extracted')
        tiles, corruption = layout / 'extracted/Textures/TerrainSetTextures/Corruption', content_pack.export_corruption_tiles
        content_pack.export_corruption_tiles = lambda output, source=tiles: corruption(output, source)
        resolver = resolve_model.Resolver(str(Path(install) / 'Database'), resolve_model.EXTRACTED)
        prepare_prototype.open_resolver = lambda output, install=None: resolver
        output = layout / 'pack'
        pack = content_pack.build(output, install)
        files = sorted(p.relative_to(output).as_posix() for p in output.rglob('*') if p.is_file())
        with ProcessPoolExecutor(workers) as pool:
            result = dict(pool.map(_fingerprint, [(str(output), f) for f in files]))
        indexes = {name: json.loads((output / name).read_text()) for name in ('audio/index.json', 'interface/index.json')}
    return {'format_version': 1, 'files': result, 'indexes': indexes, 'pack_files': sorted(pack['files'])}


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out')
    parser.add_argument('--workers', type=int)
    args = parser.parse_args()
    result = build(args.install, args.workers)
    Path(args.out).write_text(json.dumps(result, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('%d files' % len(result['files']))


if __name__ == '__main__':
    main()
