"""What tools/frontend_assets.py makes of an install (build-212), for tests/formats --python-frontend to check the C#
PackFrontEnd against.

python tools/frontend_fingerprint.py "C:/Games/War of the Ring" frontend.json

The install's archives are extracted into a temporary folder (as pack_fingerprint.py does) and the menu art exported
the Python way. Recorded: index.json without its file hashes, fonts/immortal.json whole, every picture by its decoded
pixels, size and mode, and the menu scene in gltf_fingerprint's form (its extras' source path left out), each
top-level key hashed in the canonical form. The install is only read.
"""
import argparse
import hashlib
import io
import json
import tempfile
from pathlib import Path

from PIL import Image

import h2o
import resolve_fingerprint


def build(install):
    import frontend_assets
    import gltf_fingerprint
    import lms2gltf
    import resolve_model
    import wor
    with tempfile.TemporaryDirectory(prefix='reforged-frontend-') as root:
        root = Path(root)
        for archive in sorted((Path(install) / 'Archives').glob('*.H2O')):
            if archive.stem not in ('Movies', 'Dialogue', 'Text', 'Music', 'SpeechFX', 'SoundEffects'):
                h2o.extract(str(archive), str(root / 'extracted'), verify=True)
        wor.ROOT = lms2gltf.ROOT = root
        resolve_model.ROOT = str(root)
        resolve_model.EXTRACTED = str(root / 'extracted')
        frontend_assets.ROOT = root
        frontend_assets.MENU_UVAS = root / 'extracted/UVAs/Interface/Menu'
        frontend_assets.QUIT_BOX = root / 'extracted/UVAs/Interface/Console/Dialogs/Quit Acknowledgement/Good/console_dialog_quit_acknowledgement_g_box.uva'
        frontend_assets.MENU_SCENE = root / 'extracted/Levels/MenuScene.wor'
        frontend_assets.FONT = root / 'extracted/Fonts/Immortal.lfn'
        resolver = resolve_model.Resolver(str(Path(install) / 'Database'), resolve_model.EXTRACTED)
        output = root / 'frontend'
        index = frontend_assets.export(output, resolver)
        index.pop('files', None)
        pictures = {}
        for path in sorted(output.rglob('*.png')):
            with Image.open(path) as picture:
                pictures[path.relative_to(output).as_posix()] = {'pixels': hashlib.sha256(picture.tobytes()).hexdigest(),
                                                                 'size': [picture.width, picture.height], 'mode': picture.mode}
        scene = gltf_fingerprint.normalized((output / 'menu/scene.glb').read_bytes())
        scene['extras']['wotrWorld'].pop('source', None)
        font = json.loads((output / 'fonts/immortal.json').read_text())
    return {'format_version': 1, 'index': index, 'font': font, 'pictures': pictures,
            'scene': {key: resolve_fingerprint.canonical_hash(value) for key, value in scene.items()}}


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out')
    args = parser.parse_args()
    result = build(args.install)
    Path(args.out).write_text(json.dumps(result, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    print('%d pictures, %d font sizes, menu scene %d objects' % (len(result['pictures']), len(result['font']['sizes']), result['index']['menu_scene']['placed_objects']))


if __name__ == '__main__':
    main()
