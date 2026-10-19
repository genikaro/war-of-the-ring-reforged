"""Inspect/export effects, projectiles, UVAs, texture sequences and cloth.

Run `python presentation_assets.py export --out-dir ../converted/presentation`.
Every source is parsed, not just the database's currently referenced subset.
Output contains deterministic JSON, typed dependencies and an index with
per-file errors. Original installation and extracted sources are read-only.
"""
import argparse
import json
from pathlib import Path

import cth
import lfx
import lpo
import texture_animation

ROOT = Path(__file__).resolve().parent.parent
FORMATS = {'Effects': ('.lfx', lfx.parse), 'Projectiles': ('.lpo', lpo.parse),
           'UVAs': ('.uva', texture_animation.parse_uva),
           'TextureAnimations': ('.lta', texture_animation.parse_lta), 'Cloths': ('.cth', cth.parse)}


def references(kind, asset):
    """Known database references only. Unnamed enums are never guessed as IDs."""
    result = []

    def add(table, rid, field):
        if rid is not None and rid >= 0:
            result.append({'table': table, 'id': rid, 'field': field})

    def uva(item, field):
        add('Textures', item['texture'], field + '.texture')

    if kind == 'UVAs':
        uva(asset, 'asset')
    elif kind == 'TextureAnimations':
        for i, rid in enumerate(asset['textures']):
            add('Textures', rid, 'textures.%d' % i)
    elif kind == 'Cloths':
        for i, rid in enumerate(asset['uvas']):
            add('UVAs', rid, 'uvas.%d' % i)
        add('Materials', asset['material'], 'material')
    elif kind == 'Projectiles':
        for i, key in enumerate(asset['resource_keys']):
            if key['kind'] in (0, 1):
                add('Models' if key['kind'] == 0 else 'Effects', key['id'], 'resource_keys.%d.id' % i)
    elif kind == 'Effects':
        for group in ('texture_tracks', 'beam_tracks'):
            for i, track in enumerate(asset[group]):
                for j, key in enumerate(track['keys']):
                    field = '%s.%d.keys.%d' % (group, i, j)
                    if 'uva' in key:
                        uva(key['uva'], field + '.uva')
                    elif key.get('texture_kind') == 1:
                        add('Models', key['texture'], field + '.texture')
    return result


def resolve_references(resolver, kind, asset):
    entries = []
    for ref in references(kind, asset):
        table = resolver.db[ref['table']]
        entry = resolver.texture(ref['id']) if ref['table'] == 'Textures' else resolver._entry(
            table, ref['table'], ref['id'])
        entries.append({**ref, **entry})
    return entries


def inspect(path):
    path = Path(path)
    kind, parse = next(((kind, parse) for kind, (suffix, parse) in FORMATS.items()
                        if path.suffix.lower() == suffix), (None, None))
    if parse is None:
        raise ValueError('unsupported presentation asset suffix: ' + path.suffix)
    return kind, parse(path.read_bytes())


def export(source, output, resolver=None):
    source, output = Path(source).resolve(), Path(output).resolve()
    # Protect both the source tree and the player's installation, even if an
    # explicit alternate --source or --out-dir is used.
    protected = [source, (ROOT / 'TLOTR - War of the Ring').resolve()]
    if resolver is not None:
        if hasattr(resolver.db, 'path'):
            protected.append(Path(resolver.db.path).resolve())
    if any(output == root or root in output.parents for root in protected):
        raise ValueError('output must be outside the source assets and original installation')
    index = {'format_version': 1, 'counts': {}, 'assets': [], 'errors': []}
    output.mkdir(parents=True, exist_ok=True)
    for kind, (suffix, parse) in FORMATS.items():
        files = sorted((source / kind).rglob('*'), key=lambda p: p.as_posix().lower())
        files = [p for p in files if p.is_file() and p.suffix.lower() == suffix]
        index['counts'][kind] = {'total': len(files), 'exported': 0}
        if not files:
            index['errors'].append({'source': kind, 'error': 'no source files'})
        for path in files:
            relative = path.relative_to(source)
            row = {'kind': kind, 'source': relative.as_posix()}
            try:
                asset = parse(path.read_bytes())
                dependencies = resolve_references(resolver, kind, asset) if resolver else references(kind, asset)
                document = {'format_version': 1, **row, 'asset': asset, 'references': dependencies}
                dest = output / relative.with_suffix('.json')
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_text(json.dumps(document, ensure_ascii=False, allow_nan=False, indent=2) + '\n', encoding='utf-8')
                row.update(file=dest.relative_to(output).as_posix(), references=len(dependencies))
                missing = [ref for ref in dependencies if ref.get('exists') is False]
                if missing:
                    row['missing_references'] = missing
                index['counts'][kind]['exported'] += 1
            except (ValueError, OSError, UnicodeError) as error:
                row['error'] = str(error)
                index['errors'].append(row.copy())
            index['assets'].append(row)
    (output / 'index.json').write_text(json.dumps(index, ensure_ascii=False, allow_nan=False, indent=2) + '\n', encoding='utf-8')
    return index


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest='command', required=True)
    p = sub.add_parser('inspect')
    p.add_argument('path')
    p = sub.add_parser('export')
    p.add_argument('--source', default=str(ROOT / 'extracted'))
    p.add_argument('--out-dir', required=True)
    p.add_argument('--db', default=None)
    args = parser.parse_args(argv)
    if args.command == 'inspect':
        kind, asset = inspect(args.path)
        print(json.dumps({'kind': kind, 'asset': asset, 'references': references(kind, asset)}, allow_nan=False, indent=2))
        return 0
    import resolve_model
    import lqr
    resolver = resolve_model.Resolver(args.db or lqr.DEFAULT_DB, args.source)
    index = export(args.source, args.out_dir, resolver)
    for kind, counts in index['counts'].items():
        print('%s: %d/%d exported' % (kind, counts['exported'], counts['total']))
    for error in index['errors']:
        print('%s: %s' % (error['source'], error['error']))
    missing = sum(bool(row.get('missing_references')) for row in index['assets'])
    print('%d parse errors; %d assets with missing references' % (len(index['errors']), missing))
    return int(bool(index['errors'] or missing))


if __name__ == '__main__':
    raise SystemExit(main())
