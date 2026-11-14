"""What the Python model parsers make of every model file in an install (build-195), for tests/formats --python-models
to check the C# parsers (prototype/Formats) against.

python tools/models_fingerprint.py "C:/Games/War of the Ring" models.json
python tools/models_fingerprint.py "C:/Games/War of the Ring" --dump "Models/Units/Good/Aragorn/G_Aragorn.lmd"

Every .lms (read plain and with the strict footer), .lsk, .lwt and .lmd in the archives is parsed with lms.py, lsk.py,
lwt.py and lmd.py. Each result is written in a canonical form both languages produce the same way and hashed (SHA-256):
None n, booleans t/f, integers i<decimal>, floats r<the 8 hex digits of their 32-bit pattern>, strings s<UTF-8 hex>,
lists [items,], dicts {key:value,} in key order, dataclasses as their fields. A file that does not parse records the
error instead. --dump prints one file's canonical form, to find where two parsers part. The install is only read.
"""
import argparse
import hashlib
import json
import struct
from concurrent.futures import ProcessPoolExecutor
from dataclasses import asdict, is_dataclass
from pathlib import Path

import h2o
import lmd
import lms
import lsk
import lwt

ARCHIVES = {'Meshes': '.lms', 'Skeletons': '.lsk', 'Weights': '.lwt', 'Models': '.lmd'}


def canonical(value, out):
    if value is None:
        out.append('n')
    elif value is True or value is False:
        out.append('t' if value else 'f')
    elif isinstance(value, int):
        out.append('i%d' % value)
    elif isinstance(value, float):
        out.append('r%08x' % struct.unpack('<I', struct.pack('<f', value))[0])
    elif isinstance(value, str):
        out.append('s' + value.encode('utf-8').hex())
    elif isinstance(value, (bytes, bytearray)):
        out.append('b' + bytes(value).hex())
    elif isinstance(value, (list, tuple)):
        out.append('[')
        for item in value:
            canonical(item, out)
            out.append(',')
        out.append(']')
    elif isinstance(value, dict):
        out.append('{')
        for key in sorted(value):
            canonical(key, out)
            out.append(':')
            canonical(value[key], out)
            out.append(',')
        out.append('}')
    elif is_dataclass(value):
        canonical(asdict(value), out)
    else:
        raise TypeError('no canonical form for %r' % type(value))
    return out


def readings(suffix, data):
    """(reading name, parse) for one file: meshes are read plain and with the strict footer."""
    if suffix == '.lms':
        return [('plain', lambda: lms.parse(data)), ('footer', lambda: lms.parse(data, read_footer=True))]
    parse = {'.lsk': lsk.parse, '.lwt': lwt.parse, '.lmd': lmd.parse}[suffix]
    return [('plain', lambda: parse(data))]


def fingerprint_one(suffix, data):
    result = {}
    for name, parse in readings(suffix, data):
        try:
            result[name] = hashlib.sha256(''.join(canonical(parse(), [])).encode('ascii')).hexdigest()
        except (ValueError, KeyError, IndexError, struct.error, UnicodeDecodeError) as failure:
            result[name] = 'error: %s' % failure
    return result


def archive_files(path):
    archive = h2o.H2OArchive(str(path))
    suffix = ARCHIVES[path.stem]
    return {'%s/%s' % (path.stem, entry.path.replace('\\', '/')): fingerprint_one(suffix, archive.read(entry, verify=True))
            for entry in archive.entries if entry.path.lower().endswith(suffix)}


def archives(install):
    folder = Path(install) / 'Archives'
    return sorted((p for p in folder.iterdir() if p.suffix.lower() == '.h2o' and p.stem in ARCHIVES), key=lambda p: p.name.lower())


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out', nargs='?')
    parser.add_argument('--dump', help='print the canonical form of one file, by its extracted/ path')
    parser.add_argument('--workers', type=int)
    args = parser.parse_args()
    if args.dump:
        stem, _, inner = args.dump.replace('\\', '/').partition('/')
        archive = h2o.H2OArchive(str(Path(args.install) / 'Archives' / (stem + '.H2O')))
        entry = next(e for e in archive.entries if e.path.replace('\\', '/').lower() == inner.lower())
        for name, parse in readings(ARCHIVES[stem], archive.read(entry, verify=True)):
            try:
                print(name, ''.join(canonical(parse(), [])))
            except (ValueError, KeyError, IndexError, struct.error, UnicodeDecodeError) as failure:
                print(name, 'error: %s' % failure)
        return
    files = {}
    paths = archives(args.install)
    with ProcessPoolExecutor(args.workers) as pool:
        for result in pool.map(archive_files, paths):
            files.update(result)
    Path(args.out).write_text(json.dumps({'format_version': 1, 'files': files}, indent=1) + '\n', encoding='utf-8')
    print('%d files, %d readings that fail' % (len(files), sum(v.startswith('error') for r in files.values() for v in r.values())))


if __name__ == '__main__':
    main()
