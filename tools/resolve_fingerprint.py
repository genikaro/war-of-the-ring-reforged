"""What resolve_model.py makes of every Models record of an install (build-196), for tests/formats --python-resolve to
check the C# ModelResolver against.

python tools/resolve_fingerprint.py "C:/Games/War of the Ring" resolve.json
python tools/resolve_fingerprint.py "C:/Games/War of the Ring" --dump MODEL_G_ARAGORN

The resolver reads an extracted tree. So that the result is this install's and not whatever extracted/ holds, the
script lays out its own under a temporary folder: the models, skeletons, cloths and texture animations it reads, and
empty files for the meshes, weights, textures and effects it only looks up. Each record is resolved with recursion,
as the content pack's export does, written in models_fingerprint.py's canonical form and hashed. The install is only
read.
"""
import argparse
import hashlib
import json
import os
import tempfile
from pathlib import Path

import h2o
import lqr
import models_fingerprint
import resolve_model

READ = ('Models', 'Skeletons', 'Cloths', 'UVAs')
LISTED = ('Meshes', 'Weights', 'Textures', 'Effects')


def lay_out(install, root):
    """The archives as the import's extracted/ tree under root: the files the resolver reads, names for the rest."""
    for name in READ + LISTED:
        path = Path(install) / 'Archives' / (name + '.H2O')
        if not path.is_file():
            continue
        archive = h2o.H2OArchive(str(path))
        for entry in archive.entries:
            target = Path(root) / 'extracted' / name / entry.path.replace('\\', '/')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read(entry, verify=True) if name in READ else b'')


def resolver_for(install, root):
    resolve_model.ROOT = str(root)
    resolve_model.EXTRACTED = os.path.join(str(root), 'extracted')
    return resolve_model.Resolver(str(Path(install) / 'Database'), resolve_model.EXTRACTED)


def canonical_hash(value):
    return hashlib.sha256(''.join(models_fingerprint.canonical(value, [])).encode('ascii')).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out', nargs='?')
    parser.add_argument('--dump', help='print the canonical form of one Models record')
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='reforged-resolve-') as root:
        lay_out(args.install, root)
        resolver = resolver_for(args.install, root)
        if args.dump:
            print(''.join(models_fingerprint.canonical(resolver.resolve(args.dump), [])))
            return
        records = {}
        for record in resolver.models:
            try:
                records[record.name] = canonical_hash(resolver.resolve(record))
            except Exception as failure:  # noqa: BLE001 - the record's answer is that it does not resolve
                records[record.name] = 'error: %s: %s' % (type(failure).__name__, failure)
        Path(args.out).write_text(json.dumps({'format_version': 1, 'models': records}, indent=1) + '\n', encoding='utf-8')
        print('%d models, %d that do not resolve' % (len(records), sum(v.startswith('error') for v in records.values())))


if __name__ == '__main__':
    main()
