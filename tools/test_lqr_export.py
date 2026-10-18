"""Exporter regressions with synthetic tables; no game assets required.

Run from tools/: python -m unittest -v test_lqr_export
"""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import lqr


def make_table(path, name, prefix, columns, records):
    # The exporter consumes parsed Table objects, independently of binary parsing.
    table = lqr.Table.__new__(lqr.Table)
    table.path, table.name, table.prefix = str(path), name, prefix
    table.columns, table.records = columns, records
    return table


def make_database(root):
    source = root / 'source'
    source.mkdir()
    units = make_table(source / 'Units.LQR', 'Unit', 'UNIT_', [
        lqr.Column('id', lqr.T_INT, default=-1),
        lqr.Column('name', lqr.T_STRING, default='Без имени'),
        lqr.Column('folder', lqr.T_FILENAME, default=''),
        lqr.Column('Model', lqr.T_ID, kind=1, ref_table=7, default=-1,
                   description='Unit model', group='Resources'),
        lqr.Column('Mode', lqr.T_ID, kind=2, default=-1,
                   enum={2: 'Attack', -1: 'None'}),
        lqr.Column('Speed', lqr.T_FLOAT, default=0.0),
    ], [
        lqr.Record(2, 'UNIT_HERO', {'id': 123, 'name': 'Герой',
                   'folder': 'Textures\\hero.dds', 'Model': 42,
                   'Mode': 2, 'Speed': 0.800000011920929}, 'Root\\Units'),
        lqr.Record(9, 'UNIT_UNUSED', {'id': -1, 'name': '', 'folder': '',
                   'Model': -1, 'Mode': -1, 'Speed': 0.0}),
    ])
    models = make_table(source / 'Models.lqr', 'Model', 'MODEL_', [], [
        lqr.Record(42, 'MODEL_HERO', {}),
    ])
    db = lqr.Database.__new__(lqr.Database)
    db.path = str(source)
    db._tables = {'units': units, 'models': models}
    # Deliberately reverse the source order to check deterministic output.
    db.files = {key: table.path for key, table in db._tables.items()}
    db.table_ids = {3: 'Units', 7: 'Models'}
    return db


class ExportTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.db = make_database(self.root)
        self.out = self.root / 'output'

    def test_roundtrip_keeps_ids_raw_values_and_schema(self):
        manifest = lqr.export_database(self.db, self.out)
        self.assertEqual(sorted(p.name for p in self.out.iterdir()),
                         ['Models.json', 'Units.json', 'index.json'])
        self.assertEqual(json.loads((self.out / 'index.json').read_bytes()), manifest)
        self.assertEqual(manifest['record_count'], 3)
        self.assertEqual([t['id'] for t in manifest['tables']], [7, 3])
        for entry in manifest['tables']:
            table = self.db[entry['name']]
            payload = json.loads((self.out / entry['file']).read_bytes())
            self.assertEqual(payload['format_version'], 1)
            self.assertEqual(payload['table_id'], entry['id'])
            self.assertEqual(payload['name'], table.name)
            self.assertEqual(payload['prefix'], table.prefix)
            self.assertEqual(entry['record_count'], len(table))
            self.assertEqual(entry['column_count'], len(table.columns))
            for row, original in zip(payload['records'], table.records, strict=True):
                self.assertEqual(row, {'id': original.id, 'name': original.name,
                                      'folder': original.folder, 'values': original.values})
        raw = (self.out / 'Units.json').read_bytes()
        self.assertIn('Герой'.encode('utf-8'), raw)
        units = json.loads(raw)
        self.assertEqual([r['id'] for r in units['records']], [2, 9])
        self.assertEqual(units['source'], 'Units.LQR')
        columns = {c['name']: c for c in units['columns']}
        self.assertEqual(columns['Model']['reference'], {'table_id': 7, 'table': 'Models'})
        self.assertEqual(columns['Model']['default'], -1)
        self.assertEqual(columns['Model']['description'], 'Unit model')
        self.assertEqual(columns['Mode']['enum'], [
            {'value': -1, 'label': 'None'}, {'value': 2, 'label': 'Attack'}])
        self.assertEqual(columns['Speed']['type'], 'float')

    def test_missing_project_retains_reference_id(self):
        self.db.table_ids = {}
        lqr.export_database(self.db, self.out)
        units = json.loads((self.out / 'Units.json').read_bytes())
        self.assertIsNone(units['table_id'])
        model = next(c for c in units['columns'] if c['name'] == 'Model')
        self.assertEqual(model['reference'], {'table_id': 7, 'table': None})

    def test_repeated_export_has_identical_bytes(self):
        lqr.export_database(self.db, self.out)
        before = {p.name: p.read_bytes() for p in self.out.iterdir()}
        self.db.files = dict(reversed(list(self.db.files.items())))
        lqr.export_database(self.db, self.out)
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.out.iterdir()})

    def test_output_cannot_be_inside_source(self):
        for out in [Path(self.db.path), Path(self.db.path) / 'json']:
            with self.subTest(out=out), self.assertRaisesRegex(ValueError, 'outside'):
                lqr.export_database(self.db, out)
        self.assertEqual(list(Path(self.db.path).iterdir()), [])

    def test_empty_database_is_an_error(self):
        self.db.files = {}
        with self.assertRaisesRegex(ValueError, 'no .lqr tables'):
            lqr.export_database(self.db, self.out)
        self.assertFalse(self.out.exists())

    def test_nonfinite_numbers_do_not_modify_existing_export(self):
        lqr.export_database(self.db, self.out)
        before = {p.name: p.read_bytes() for p in self.out.iterdir()}
        for value in [float('nan'), float('inf'), float('-inf')]:
            with self.subTest(value=value):
                self.db['units'].records[0].values['Speed'] = value
                with self.assertRaises(ValueError):
                    lqr.export_database(self.db, self.out)
                self.assertEqual(before, {p.name: p.read_bytes() for p in self.out.iterdir()})

    def test_reserved_manifest_name_is_rejected(self):
        table = self.db['units']
        table.path = str(Path(self.db.path) / 'Index.lqr')
        self.db.files = {'index': table.path}
        self.db._tables = {'index': table}
        with self.assertRaisesRegex(ValueError, 'conflicts'):
            lqr.export_database(self.db, self.out)
        self.assertFalse(self.out.exists())

    def test_write_failure_keeps_previous_file_and_removes_temporary(self):
        self.out.mkdir()
        target = self.out / 'Units.json'
        target.write_text('previous contents', encoding='utf-8')
        with patch.object(lqr.os, 'replace', side_effect=OSError('disk error')):
            with self.assertRaises(OSError):
                lqr._write_export(str(target), 'replacement')
        self.assertEqual(target.read_text(encoding='utf-8'), 'previous contents')
        self.assertEqual(list(self.out.iterdir()), [target])

    def test_cli_reports_invalid_source(self):
        error = io.StringIO()
        with contextlib.redirect_stderr(error):
            result = lqr.main(['lqr.py', 'export', '--db', str(self.root / 'missing'),
                               '--out-dir', str(self.out)])
        self.assertEqual(result, 1)
        self.assertIn('export failed:', error.getvalue())
        self.assertFalse(self.out.exists())


if __name__ == '__main__':
    unittest.main()
