"""What the Python readers make of an install (build-192), for tests/formats to check the C# readers against.

python tools/formats_fingerprint.py "C:/Games/War of the Ring" fingerprint.json

Every archive file's path, size, packing, CRC and SHA-256 (read with h2o.py, CRC-checked) and every database table
as `lqr.py export` writes it. The install is only read.
"""
import argparse
import hashlib
import json
from pathlib import Path

import h2o
import lqr


def archive_entries(path):
    archive = h2o.H2OArchive(str(path))
    return [{'path': entry.path, 'size': entry.raw_size, 'packed': entry.packed, 'crc': entry.crc,
             'sha256': hashlib.sha256(archive.read(entry, verify=True)).hexdigest()} for entry in archive.entries]


def fingerprint(install, workers=None):
    from concurrent.futures import ProcessPoolExecutor
    install = Path(install)
    paths = sorted((p for p in (install / 'Archives').iterdir() if p.suffix.lower() == '.h2o'), key=lambda p: p.name.lower())
    with ProcessPoolExecutor(workers) as pool:
        archives = dict(zip((p.stem for p in paths), pool.map(archive_entries, paths)))
    db = lqr.Database(str(install / 'Database'))
    ids = {name.lower(): table_id for table_id, name in db.table_ids.items()}
    return {'format_version': 1, 'archives': archives,
            'table_ids': {str(table_id): name for table_id, name in db.table_ids.items()},
            'tables': {key: lqr.table_to_dict(db[key], ids.get(key), db.table_ids) for key in sorted(db.files)}}


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out')
    parser.add_argument('--workers', type=int)
    args = parser.parse_args()
    Path(args.out).write_text(json.dumps(fingerprint(args.install, args.workers), ensure_ascii=False, allow_nan=False) + '\n',
                              encoding='utf-8')


if __name__ == '__main__':
    main()
