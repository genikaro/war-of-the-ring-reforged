"""What lms2gltf.convert_model makes of the models the content pack exports (build-197), for tests/formats
--python-gltf to check the C# ModelGltf against.

python tools/gltf_fingerprint.py "C:/Games/War of the Ring" gltf.json
python tools/gltf_fingerprint.py "C:/Games/War of the Ring" --dump MODEL_G_ARAGORN

The models: every roster unit's, every skirmish building's, every place-of-power object's and the rally banner, each
converted from a layout of the given install (resolve_fingerprint.lay_out, with the meshes, weights and textures read
too). A GLB is compared by its JSON with each accessor's buffer view replaced by the SHA-256 of its bytes and each
image's by the SHA-256 of its decoded pixels and its size, without the bufferViews and buffers lists: PNG bytes may
differ between encoders, pixels may not. Written in models_fingerprint.py's canonical form and hashed.
"""
import argparse
import hashlib
import io
import json
import os
import struct
import tempfile
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

from PIL import Image

import models_fingerprint
import resolve_fingerprint

resolve_fingerprint.READ = ('Models', 'Skeletons', 'Cloths', 'UVAs', 'Meshes', 'Weights', 'Textures')
resolve_fingerprint.LISTED = ('Effects',)

_resolver = None


def model_names(db):
    import prepare_prototype
    import skirmish_map
    import skirmish_production
    names = []
    for unit in prepare_prototype.SKIRMISH_ROSTER:
        names.append(db['Models'].get(db['Units'].get(unit)['Model']).name)
    for building in skirmish_map.SKIRMISH_BUILDINGS:
        names.append(db['Models'].get(db['Buildings'].get(building)['Model']).name)
    for record in db['Objects'].records:
        if record.name.startswith('OBJECT_PLACE_OF_POWER_'):
            event = db['ModelEvents'].get(record['ModelEvent'])
            names.append(db['Models'].get(event['AssociatedModelResource1']).name)
    names.append(skirmish_production.RALLY_BANNER)
    return list(dict.fromkeys(names))


def normalized(glb):
    """The GLB's JSON with buffer contents in place of their offsets."""
    length = struct.unpack_from('<I', glb, 12)[0]
    gltf = json.loads(glb[20:20 + length])
    binary_start = 20 + length + 8
    views = gltf.pop('bufferViews', [])
    gltf.pop('buffers', None)

    def view_bytes(index):
        view = views[index]
        start = binary_start + view.get('byteOffset', 0)
        return glb[start:start + view['byteLength']]

    for accessor in gltf.get('accessors', []):
        accessor['data'] = hashlib.sha256(view_bytes(accessor.pop('bufferView'))).hexdigest()
    for image in gltf.get('images', []):
        picture = Image.open(io.BytesIO(view_bytes(image.pop('bufferView'))))
        image['pixels'] = hashlib.sha256(picture.tobytes()).hexdigest()
        image['size'] = [picture.width, picture.height, len(picture.mode)]
    return gltf


def _start(install, root):
    global _resolver
    import lms2gltf
    _resolver = resolve_fingerprint.resolver_for(install, root)
    lms2gltf.ROOT = root


def _convert(name):
    import lms2gltf
    path = os.path.join(tempfile.gettempdir(), 'reforged-gltf-%d-%s.glb' % (os.getpid(), name))
    try:
        lms2gltf.convert_model(_resolver, name, path)
        return name, resolve_fingerprint.canonical_hash(normalized(Path(path).read_bytes()))
    except Exception as failure:  # noqa: BLE001 - the model's answer is that it does not convert
        return name, 'error: %s: %s' % (type(failure).__name__, failure)
    finally:
        if os.path.exists(path):
            os.remove(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out', nargs='?')
    parser.add_argument('--dump', help='print the canonical form of one model')
    parser.add_argument('--workers', type=int)
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='reforged-gltf-') as root:
        resolve_fingerprint.lay_out(args.install, root)
        _start(args.install, root)
        if args.dump:
            import lms2gltf
            path = os.path.join(root, 'dump.glb')
            lms2gltf.convert_model(_resolver, args.dump, path)
            print(''.join(models_fingerprint.canonical(normalized(Path(path).read_bytes()), [])))
            return
        names = model_names(_resolver.db)
        with ProcessPoolExecutor(args.workers, initializer=_start, initargs=(args.install, root)) as pool:
            models = dict(pool.map(_convert, names))
    Path(args.out).write_text(json.dumps({'format_version': 1, 'models': models}, indent=1) + '\n', encoding='utf-8')
    print('%d models, %d that do not convert' % (len(models), sum(v.startswith('error') for v in models.values())))


if __name__ == '__main__':
    main()
