"""Reader for Liquid Entertainment .lqr database tables (War of the Ring).

Every table in Database/*.lqr shares one layout; all 154 files parse to the
exact byte. Little endian. "str" below means u8 0, u32 char count (incl. NUL),
UTF-16LE chars. "sN" means u32 index into the string table at the end.

    u32      0x6a                    format version
    u32      garbage                 (uninitialised memory, ignore)
    u64      0x28                    offset of the record block
    u64      offset of the name block
    u64      offset of the editor block
    u64      offset of the string table
  record block (at 0x28):
    u64      offset of record_count  (= 0x34 + 4 * column_count)
    u32      column_count
    u32[]    column types: 0 int, 1 float, 2 str, 3 int (enum, bool or record
             id of another table, see the column descriptor), 4 str filename
    u32      record_count
    u32      slot_count              next free id; ids are slot indices
    slot_count x:
             u32 id, u8 used, then if used one value per column
             (4 bytes for types 0, 1, 3; str for types 2 and 4)
  name block:
    u8[9]    unknown flags
    sN       prefix ("MESH_"), sN table name ("Mesh")
    u32      record_count, u32 column_count
    sN[record_count]   symbolic record names (MESH_M_SW_G_ARAGORN_BODY), in
                       the order of the used slots
    sN[column_count]   column names
  editor block (only the world editor needs it):
    u8       has_conversions, u32 n, n x {sN name, sN exe, sN args, sN ext}
    column_count x 44 bytes:
             u32 kind (0 plain, 1 record id of table ref_table, 2 enum list),
             i32 ref_table (table id from Rings.lrp, -1 none), i32 unknown
             (often looks like a max value), u32 default (raw int/float),
             sN default string, i32 unknown, sN file dialog title,
             sN file filter, sN file extension, sN description,
             i32 group (sN of the property group, -1 none)
    column_count x enum list: u32 n, n x {i32 value, sN label}
    u32 n, n x property groups {sN name, u8 expanded + 3 pad, i32 parent}
    u32 n, n x property grid rows {u8 is_group + 3 pad, sN group, sN column}
    record_count x {u32 unknown, u8 unknown + 3 pad, sN folder}
             folder is the editor tree path, e.g. "Root\\Units\\Good\\Aragorn"
    u32 n, n x folders {sN name, u32 depth, u8 expanded + 3 pad, sN parent}
    u32 n, sN[n] list view columns (first is "[ Entries ]"), i32 -1
  string table:
    u32 count, u32 byte_size, NUL-terminated UTF-16LE strings

Rings.lrp (project file) maps the ref_table ids to table names:
    u32 0x66, u32 unknown, u32 0, 4 x str (project name, source control
    ini paths), u8, u8, u32 n + u32[n], u32 n + u32[n], u32 0, 3 x str,
    u32 table_count, u32[table_count] table ids, table_count x str names.

Usage:
    python lqr.py tables [<Database dir>]
    python lqr.py schema <table.lqr|name>
    python lqr.py list <table.lqr|name> [--grep TEXT] [--json]
    python lqr.py show <table.lqr|name> <id|SYMBOLIC_NAME>
    python lqr.py export --out-dir <dir> [--db <Database dir>]
"""
import argparse
import json
import os
import struct
import sys
import tempfile
from dataclasses import dataclass, field

HERE = os.path.dirname(os.path.abspath(__file__))
# The original install the tools read when given none: REFORGED_INSTALL names it (the launcher's import sets it,
# build-188), else the repository's `TLOTR - War of the Ring` folder.
INSTALL = os.environ.get('REFORGED_INSTALL') or os.path.join(HERE, '..', 'TLOTR - War of the Ring')
DEFAULT_DB = os.path.join(INSTALL, 'Database')

T_INT, T_FLOAT, T_STRING, T_ID, T_FILENAME = range(5)
TYPE_NAMES = {T_INT: 'int', T_FLOAT: 'float', T_STRING: 'string', T_ID: 'id',
              T_FILENAME: 'filename'}


@dataclass
class Column:
    name: str
    type: int
    kind: int = 0             # 0 plain, 1 reference, 2 enum list
    ref_table: int = -1       # Rings.lrp table id when kind == 1
    default: object = None
    description: str = ''
    file_ext: str = ''
    group: str = ''
    enum: dict = field(default_factory=dict)   # value -> label


@dataclass
class Record:
    id: int
    name: str                 # symbolic name, e.g. MESH_M_SW_G_ARAGORN_BODY
    values: dict              # column name -> value
    folder: str = ''          # editor tree path

    def __getitem__(self, key):
        return self.values[key]

    def get(self, key, default=None):
        return self.values.get(key, default)


class Reader:
    def __init__(self, data, pos=0):
        self.data = data
        self.pos = pos

    def u8(self):
        self.pos += 1
        return self.data[self.pos - 1]

    def u32(self):
        self.pos += 4
        return struct.unpack_from('<I', self.data, self.pos - 4)[0]

    def i32(self):
        self.pos += 4
        return struct.unpack_from('<i', self.data, self.pos - 4)[0]

    def f32(self):
        self.pos += 4
        return struct.unpack_from('<f', self.data, self.pos - 4)[0]

    def u32s(self, n):
        self.pos += 4 * n
        return list(struct.unpack_from('<%dI' % n, self.data, self.pos - 4 * n))

    def skip_list(self, item_size, extra=0):
        n = self.u32()
        self.pos += item_size * n + extra

    def str(self):
        if self.data[self.pos] != 0:
            raise ValueError('bad string marker at 0x%x' % self.pos)
        n = struct.unpack_from('<I', self.data, self.pos + 1)[0]
        start = self.pos + 5
        self.pos = start + 2 * n
        return self.data[start:self.pos].decode('utf-16-le').rstrip('\0')


class Table:
    def __init__(self, path):
        self.path = path
        with open(path, 'rb') as f:
            data = f.read()
        version, _, rec_off, name_off, edit_off, str_off = struct.unpack_from('<IIQQQQ', data, 0)
        if version != 0x6a:
            raise ValueError('%s: unsupported lqr version 0x%x' % (path, version))
        r = Reader(data, str_off)
        count, size = r.u32(), r.u32()
        strings = data[r.pos:r.pos + size].decode('utf-16-le').split('\0')[:count]
        s = lambda i: strings[i] if 0 <= i < len(strings) else ''

        # record block
        r.pos = rec_off + 8
        ncol = r.u32()
        types = r.u32s(ncol)
        record_count, slot_count = r.u32(), r.u32()
        raw = []
        for _ in range(slot_count):
            rid, used = r.u32(), r.u8()
            if not used:
                continue
            vals = []
            for t in types:
                if t in (T_STRING, T_FILENAME):
                    vals.append(r.str())
                elif t == T_FLOAT:
                    vals.append(r.f32())
                else:
                    vals.append(r.i32())
            raw.append((rid, vals))
        if r.pos != name_off or len(raw) != record_count:
            raise ValueError('%s: record block size mismatch' % path)

        # name block
        r.pos = name_off + 9
        self.prefix, self.name = s(r.u32()), s(r.u32())
        nrec, nc = r.u32(), r.u32()
        rec_names = [s(i) for i in r.u32s(nrec)]
        col_names = [s(i) for i in r.u32s(nc)]

        # editor block
        r.pos = edit_off
        has_conv, nconv = r.u8(), r.u32()
        self.conversions = [[s(i) for i in r.u32s(4)] for _ in range(nconv)]
        self.columns = []
        for name, t in zip(col_names, types):
            kind, ref, _, dflt, dstr, _ = r.u32(), r.i32(), r.i32(), r.u32(), r.u32(), r.i32()
            _title, _filt, ext, desc, group = r.u32(), r.u32(), r.u32(), r.u32(), r.i32()
            if t == T_FLOAT:
                dflt = struct.unpack('<f', struct.pack('<I', dflt))[0]
            elif t in (T_STRING, T_FILENAME):
                dflt = s(dstr)
            elif dflt >= 0x80000000:
                dflt -= 1 << 32
            self.columns.append(Column(name, t, kind, ref, dflt, s(desc), s(ext), s(group)))
        for col in self.columns:
            for _ in range(r.u32()):
                value, label = r.i32(), r.u32()
                col.enum[value] = s(label)
        r.skip_list(12)                           # property groups
        r.skip_list(12)                           # property grid rows
        folders = []
        for _ in range(nrec):
            r.pos += 8
            folders.append(s(r.u32()))
        r.skip_list(16)                           # folder tree
        r.skip_list(4, 4)                         # list view columns
        if r.pos != str_off:
            raise ValueError('%s: editor block size mismatch' % path)

        self.records = [Record(rid, rec_names[i], dict(zip(col_names, vals)), folders[i])
                        for i, (rid, vals) in enumerate(raw)]
        self.by_id = {rec.id: rec for rec in self.records}
        self.by_name = {rec.name: rec for rec in self.records}

    def __iter__(self):
        return iter(self.records)

    def __len__(self):
        return len(self.records)

    def get(self, key):
        """Record by id (int) or symbolic name (str), None if missing."""
        if isinstance(key, int):
            return self.by_id.get(key)
        return self.by_name.get(key)


def read_project(path):
    """Rings.lrp: returns {table id: table file name}."""
    with open(path, 'rb') as f:
        r = Reader(f.read())
    r.pos = 12
    for _ in range(4):
        r.str()
    r.pos += 2
    for _ in range(2):
        r.skip_list(4)
    r.pos += 4
    for _ in range(3):
        r.str()
    ids = r.u32s(r.u32())
    return {i: r.str() for i in ids}


class Database:
    """Lazy loader for a Database directory: db['Meshes'], db.table_by_id(5)."""

    def __init__(self, path=DEFAULT_DB):
        self.path = path
        self.files = {f[:-4].lower(): os.path.join(path, f)
                      for f in os.listdir(path) if f.lower().endswith('.lqr')}
        self._tables = {}
        lrp = os.path.join(path, 'Rings.lrp')
        self.table_ids = read_project(lrp) if os.path.exists(lrp) else {}

    def __getitem__(self, name):
        key = name.lower()
        if key not in self._tables:
            self._tables[key] = Table(self.files[key])
        return self._tables[key]

    def table_by_id(self, table_id):
        name = self.table_ids.get(table_id)
        return self[name] if name and name.lower() in self.files else None

    def resolve(self, column, value):
        """Symbolic name for a reference column value, else the value."""
        if column.kind == 1 and value >= 0:
            table = self.table_by_id(column.ref_table)
            rec = table.get(value) if table else None
            if rec:
                return rec.name
        if column.kind == 2 and value in column.enum:
            return column.enum[value]
        return value


def table_to_dict(table, table_id=None, table_ids=None):
    """JSON-ready table with raw values and enough schema to follow references.

    Values stay nested so columns named id, name or folder cannot overwrite
    record metadata. References and enums retain their original integer values.
    """
    table_ids = table_ids or {}
    columns = []
    for col in table.columns:
        column = {'name': col.name, 'type': TYPE_NAMES[col.type],
                  'kind': col.kind, 'default': col.default,
                  'description': col.description, 'file_ext': col.file_ext,
                  'group': col.group}
        if col.kind == 1:
            column['reference'] = {'table_id': col.ref_table,
                                   'table': table_ids.get(col.ref_table)}
        if col.enum:
            column['enum'] = [{'value': value, 'label': label}
                              for value, label in sorted(col.enum.items())]
        columns.append(column)
    return {'format_version': 1, 'table_id': table_id, 'name': table.name,
            'prefix': table.prefix, 'source': os.path.basename(table.path),
            'columns': columns,
            'records': [{'id': rec.id, 'name': rec.name, 'folder': rec.folder,
                         'values': rec.values.copy()} for rec in table]}


def _write_export(path, text):
    """Replace a JSON file only after its complete contents have been written."""
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', newline='\n',
                                         dir=os.path.dirname(path),
                                         prefix='.lqr-', suffix='.tmp',
                                         delete=False) as f:
            temporary = f.name
            f.write(text)
        os.replace(temporary, path)
    finally:
        if temporary is not None and os.path.exists(temporary):
            os.unlink(temporary)


def export_database(db, out_dir):
    """Export every .lqr plus index.json; return the index as a plain dict.

    Parse and serialize all tables before writing anything. Invalid input or
    non-JSON numeric values therefore leave an existing export untouched.
    """
    source = os.path.normcase(os.path.realpath(db.path))
    destination = os.path.normcase(os.path.realpath(out_dir))
    # commonpath raises ValueError for paths on different Windows drives.
    try:
        inside_source = os.path.commonpath([source, destination]) == source
    except ValueError:
        inside_source = False
    if inside_source:
        raise ValueError('output directory must be outside the source Database directory')
    if not db.files:
        raise ValueError('no .lqr tables found in %s' % db.path)

    ids = {name.lower(): rid for rid, name in db.table_ids.items()}
    files = []
    manifest = {'format_version': 1, 'record_count': 0, 'tables': []}
    for key in sorted(db.files):
        table = db[key]
        stem = os.path.splitext(os.path.basename(table.path))[0]
        if stem.lower() == 'index':
            raise ValueError('Index.lqr conflicts with the export index.json')
        filename = stem + '.json'
        payload = table_to_dict(table, ids.get(key), db.table_ids)
        files.append((filename, json.dumps(payload, ensure_ascii=False,
                                           allow_nan=False, indent=2) + '\n'))
        manifest['tables'].append({'id': ids.get(key), 'name': stem,
                                   'file': filename, 'record_count': len(table),
                                   'column_count': len(table.columns)})
        manifest['record_count'] += len(table)
    files.append(('index.json', json.dumps(manifest, ensure_ascii=False,
                                           allow_nan=False, indent=2) + '\n'))
    os.makedirs(out_dir, exist_ok=True)
    for filename, contents in files:
        _write_export(os.path.join(out_dir, filename), contents)
    return manifest


def _open(arg):
    if os.path.isfile(arg):
        db = Database(os.path.dirname(os.path.abspath(arg)))
        return db, db[os.path.splitext(os.path.basename(arg))[0]]
    db = Database()
    return db, db[arg]


def _fmt(db, col, value):
    v = db.resolve(col, value)
    if isinstance(v, float):
        return '%g' % v
    return str(v)


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    cmd = argv[1]
    if cmd == 'export':
        parser = argparse.ArgumentParser(prog='%s export' % argv[0],
                                         description='Export all .lqr tables and their schema to JSON.')
        parser.add_argument('--db', default=DEFAULT_DB, metavar='DIR',
                            help='source Database directory (default: the local game install)')
        parser.add_argument('--out-dir', required=True, metavar='DIR',
                            help='destination directory, outside the source Database')
        args = parser.parse_args(argv[2:])
        try:
            manifest = export_database(Database(args.db), args.out_dir)
        except (OSError, ValueError, KeyError, IndexError, struct.error) as e:
            print('export failed: %s' % e, file=sys.stderr)
            return 1
        print('Exported %d tables, %d records to %s' % (
            len(manifest['tables']), manifest['record_count'], os.path.abspath(args.out_dir)))
    elif cmd == 'tables':
        db = Database(argv[2] if len(argv) > 2 else DEFAULT_DB)
        names = {v.lower(): k for k, v in db.table_ids.items()}
        for key in sorted(db.files):
            t = db[key]
            print('%4s  %-36s %5d records  %3d columns  prefix %s' % (
                names.get(key, '-'), os.path.basename(db.files[key]), len(t),
                len(t.columns), t.prefix))
    elif cmd == 'schema':
        db, t = _open(argv[2])
        print('%s: table %s, prefix %s, %d records' % (os.path.basename(t.path), t.name, t.prefix, len(t)))
        for i, c in enumerate(t.columns):
            ref = ''
            if c.kind == 1:
                ref = ' -> %s' % db.table_ids.get(c.ref_table, c.ref_table)
            elif c.enum:
                ref = ' enum %s' % c.enum
            print('  %3d %-28s %-8s default=%r%s%s' % (
                i, c.name, TYPE_NAMES.get(c.type, c.type), c.default, ref,
                ('  # ' + c.description) if c.description else ''))
        for conv in t.conversions:
            print('  conversion:', ' | '.join(conv))
    elif cmd == 'list':
        db, t = _open(argv[2])
        grep = argv[argv.index('--grep') + 1].lower() if '--grep' in argv else None
        rows = []
        for rec in t:
            vals = [_fmt(db, c, rec.values[c.name]) for c in t.columns]
            line = '0x%04x  %-44s %s' % (rec.id, rec.name, '  '.join(vals))
            if grep and grep not in (line + rec.folder).lower():
                continue
            rows.append(rec)
            if '--json' not in argv:
                print(line)
        if '--json' in argv:
            print(json.dumps([{'id': r.id, 'name': r.name, 'folder': r.folder, **r.values}
                              for r in rows], indent=1))
    elif cmd == 'show':
        db, t = _open(argv[2])
        key = argv[3]
        rec = t.get(int(key, 0)) if key[:1].isdigit() else t.get(key)
        if rec is None:
            print('not found:', key)
            return 1
        print('0x%x %s  (%s)' % (rec.id, rec.name, rec.folder))
        for c in t.columns:
            v = rec.values[c.name]
            shown = _fmt(db, c, v)
            raw = '' if c.kind == 0 or shown == str(v) else '  (%s)' % v
            print('  %-32s %s%s' % (c.name, shown, raw))
    else:
        print(__doc__)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
