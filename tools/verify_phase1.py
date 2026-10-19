"""Audit Phase 1 source coverage and generated outputs; write a reviewable report.

This verifies conversion coverage, not visual fidelity to the original game.
The roadmap's screenshot comparison gate is deliberately reported separately.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct

import lmd
import lqr
import presentation_assets

ROOT = Path(__file__).resolve().parent.parent


def glb_document(path):
    data = Path(path).read_bytes()
    if len(data) < 20:
        raise ValueError('truncated GLB')
    magic, version, size, length, chunk = struct.unpack_from('<5I', data)
    if (magic, version, size, chunk) != (0x46546c67, 2, len(data), 0x4e4f534a):
        raise ValueError('invalid GLB header')
    if 20 + length > len(data):
        raise ValueError('truncated GLB JSON')
    return json.loads(data[20:20 + length])


def audit(source=ROOT / 'extracted', converted=ROOT / 'converted', database=lqr.DEFAULT_DB):
    source, converted = Path(source), Path(converted)
    db = lqr.Database(str(database))
    report = {'format_version': 1, 'errors': [], 'source_gaps': [], 'coverage': {},
              'visual_gate': {'verified': False, 'status': 'deferred_until_after_phase2',
                              'reason': 'The user will capture original in-game references after Phase 2.'},
              'preview_limits': ['Cloth is an initial particle surface, without wind simulation.',
                                 'Effects export structured data and visibility; particles/trails are not rendered.',
                                 'Projectile GLBs preview the model; original flight/impact parameters remain in JSON.',
                                 'Unnamed effect/gameplay parameters retain raw words rather than inferred meanings.']}
    counts = Counter()
    models = sorted((source / 'Models').rglob('*.lmd'))
    if not models:
        report['errors'].append('No model source files')
    for path in models:
        try:
            model = lmd.load(path)
            if len(model.events) != model.counts[11]:
                raise ValueError('event table count mismatch')
            counts.update(models=1, events=len(model.events), cloths=len(model.cloths),
                          cloth_collisions=len(model.cloth_collisions), cloth_groups=len(model.cloth_collision_groups))
        except (OSError, ValueError) as error:
            report['errors'].append('%s: %s' % (path.relative_to(source), error))
    report['coverage']['model_sources'] = dict(counts)
    try:
        index = json.loads((converted / 'presentation/index.json').read_text(encoding='utf-8'))
        report['coverage']['presentation'] = index['counts']
        report['errors'].extend(index['errors'])
        sources = {path.relative_to(source).as_posix() for kind, (suffix, _) in presentation_assets.FORMATS.items()
                   for path in (source / kind).rglob('*') if path.is_file() and path.suffix.lower() == suffix}
        if sources != {row['source'] for row in index['assets']}:
            report['errors'].append('Presentation source inventory is stale or incomplete')
        for row in index['assets']:
            path = source / row['source']
            kind, decoded = presentation_assets.inspect(path)
            if 'file' not in row:
                continue
            cached = json.loads((converted / 'presentation' / row['file']).read_text(encoding='utf-8'))
            if cached['asset'] != decoded or cached['kind'] != kind:
                report['errors'].append('Stale presentation export: ' + row['source'])
            for ref in row.get('missing_references', []):
                report['source_gaps'].append({'source': row['source'], **ref})
    except (OSError, ValueError, KeyError) as error:
        report['errors'].append('Presentation audit: ' + str(error))
    glbs = set()
    for table in ('Units', 'Buildings', 'Projectiles'):
        folder = converted / table.lower()
        try:
            entries = json.loads((folder / 'index.json').read_text(encoding='utf-8'))
            by_name = {row['unit']: row for row in entries}
            exported, data_only = 0, 0
            for record in db[table]:
                row = by_name.get(record.name)
                if row is None:
                    report['errors'].append('Missing export index entry: ' + record.name)
                elif 'error' in row:
                    # Preserve the actual source issue, without manufacturing a
                    # replacement model for the unused butterfly record.
                    if table == 'Units' and db['Models'].get(record['Model']) is None:
                        report['source_gaps'].append({'record': record.name, 'error': row['error']})
                    else:
                        report['errors'].append({'record': record.name, 'error': row['error']})
                elif row.get('file'):
                    path = folder / row['file']
                    document = glb_document(path)
                    appearance = document.get('extras', {}).get('wotrAppearance')
                    if not appearance or not appearance['models']:
                        raise ValueError('missing model appearance metadata in ' + path.name)
                    glbs.add(str(path.resolve()))
                    exported += 1
                elif table == 'Projectiles' and row.get('data') and row.get('preview'):
                    json.loads((folder / row['data']).read_text(encoding='utf-8'))
                    data_only += 1
                else:
                    report['errors'].append('Missing output for ' + record.name)
            report['coverage'][table.lower()] = {'records': len(by_name), 'glb_records': exported, 'data_only': data_only}
        except (OSError, ValueError, KeyError) as error:
            report['errors'].append(table + ' audit: ' + str(error))
    report['coverage']['unique_glbs'] = len(glbs)
    validation = converted / 'gltf-validation.json'
    if validation.exists():
        try:
            data = json.loads(validation.read_text(encoding='utf-8'))
            validated = {str((converted / row['file']).resolve()): row for row in data['files']}
            if set(validated) != glbs or any(hashlib.sha256(Path(file).read_bytes()).hexdigest() !=
                                           validated[file]['sha256'] for file in glbs):
                raise ValueError('GLB validator report is stale or incomplete')
            report['gltf_validation'] = {'files': len(validated), 'errors': data['errors'], 'warnings': data['warnings']}
            if data['errors']:
                report['errors'].append('GLB validator errors: %d' % data['errors'])
        except (OSError, ValueError, KeyError) as error:
            report['errors'].append('GLB validation audit: ' + str(error))
    else:
        report['gltf_validation'] = {'status': 'not run'}
    report['conversion_checks_passed'] = not report['errors']
    report['phase1_implementation_complete'] = not report['errors']
    report['phase1_gate_passed'] = False
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--source', type=Path, default=ROOT / 'extracted')
    parser.add_argument('--converted', type=Path, default=ROOT / 'converted')
    parser.add_argument('--db', default=lqr.DEFAULT_DB)
    parser.add_argument('--report', type=Path, default=ROOT / 'converted/phase1-report.json')
    args = parser.parse_args(argv)
    args.report = args.report.resolve()
    protected = [args.source.resolve(), Path(args.db).resolve(), (ROOT / 'TLOTR - War of the Ring').resolve()]
    if any(args.report == root or root in args.report.parents for root in protected):
        parser.error('report must be outside the source assets and original installation')
    report = audit(args.source, args.converted, args.db)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, allow_nan=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('source_gaps', 'preview_limits')}, indent=2))
    print('%d source gaps recorded; screenshot gate deferred until after Phase 2' % len(report['source_gaps']))
    return int(bool(report['errors']))


if __name__ == '__main__':
    raise SystemExit(main())
