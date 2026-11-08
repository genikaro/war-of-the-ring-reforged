"""The original game's text, imported from the player's own install (build-183).

`Text.H2O` holds 49 LIQDTEXT tables (`<Table>.ltx`). Little endian:
    "LIQDTEXT", f32 1.0 (version), u32 count,
    count x {u32 id, u8 flag (always 0), u32 length in chars, UTF-16LE chars}
The length counts the closing NUL; an empty string has length 0 and no NUL. Entries are not sorted by id, and the same id
means different things in different tables, so text is always addressed as `Table:id` (`GameConsole:30`).

The database links records to text through one level of indirection: a content column `InterfaceTextID*` holds a record of
`InterfaceTextIDs.lqr` (symbol `INTERFACE_TEXT_ID_UNIT_G_GONDOR_SWORDSMAN_NAME`), whose `InterfaceTextIDNumber` is the id
inside an .ltx table. No field names the table; the symbol's editor folder decides it (`table_for_folder`). A few columns
(Hotkeys, MultiplayerGameModes, StatsScreenAwards, Actors, MusicTracks) hold an .ltx id directly.

Nothing in the files names the language: the .ltx header has no field, Rings.exe no version resource, and the registry
belongs to whichever installer was used. The language is read from the text itself (`detect_language`).

The export goes to the content pack (converted/, never the repository): `text/original.json` with the detected language,
its evidence, every table as {id: text} (`<br>` made a newline, `%s`/`%d` made `{0}`, `{1}`...), the symbols as `Table:id`
and the refs whose text is a placeholder left in the shipped tables.

Usage:
    python game_text.py [--install <game folder>] [--out <file.json>]
"""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path

import h2o
import wor

INSTALL = wor.ROOT / 'TLOTR - War of the Ring'
FORMAT_VERSION = 1
MAGIC = b'LIQDTEXT'
SYMBOL_PREFIX = 'INTERFACE_TEXT_ID_'


def parse_ltx(data):
    """{id: text} of one LIQDTEXT table, exactly as stored."""
    if data[:8] != MAGIC:
        raise ValueError('not a LIQDTEXT table')
    _version, count = struct.unpack_from('<fI', data, 8)
    pos, entries = 16, {}
    for _ in range(count):
        entry_id, _flag, length = struct.unpack_from('<IBI', data, pos)
        pos += 9
        raw = data[pos:pos + 2 * length]
        if len(raw) != 2 * length:
            raise ValueError('entry %d runs past the end of the table' % entry_id)
        pos += 2 * length
        text = raw.decode('utf-16-le')
        if text.endswith('\0'):
            text = text[:-1]
        if entry_id in entries:
            raise ValueError('duplicate entry id %d' % entry_id)
        entries[entry_id] = text
    if pos != len(data):
        raise ValueError('%d trailing bytes after %d entries' % (len(data) - pos, count))
    return entries


PRINTF = re.compile(r'%%|%(?:\.\d+)?[sdf]')


def runtime_text(text):
    """The form GameText reads: `<br>` is a newline and printf arguments are numbered `{0}`, `{1}`... in order."""
    index = iter(range(100))
    text = re.sub(r'<br>', '\n', text, flags=re.IGNORECASE)
    return PRINTF.sub(lambda m: '%' if m.group() == '%%' else '{%d}' % next(index), text)


PLACEHOLDER = re.compile(r'^\s*$|^\s*(?:xxx|ххх)|Затычка|Place Of Power Buff|^\s*(?:Empty description|Placeholder)\s*$|'
                         r'^<[^<>]*(?:Benefit|Description|Name|Название|Описание)[^<>]*>$', re.IGNORECASE)


def is_placeholder(text):
    """Stub entries the shipped tables still carry: empty, "xxx"/"ххх" (obsolete), "Затычка" (stub), `<Spell name>` style
    names in angle brackets, and the English "<... Benefit>" / "Place Of Power Buff N" leftovers. The English release
    writes its stubs as "Empty description" (where the Russian has "Затычка под описание") and "Placeholder" (build-187).
    Real angle-bracket text (the chat status tags `<играет>`) stays."""
    return bool(PLACEHOLDER.search(text))


def table_for_folder(folder, symbol=''):
    """The .ltx table an InterfaceTextIDs record points into, from its editor folder (verified against every record)."""
    parts = folder.split('\\')
    top = parts[1] if len(parts) > 1 else ''
    if top in ('Units', 'Buildings', 'Techniques', 'Battlegear', 'Objects', 'Global Spells', 'Console'):
        return 'GameConsole'
    if top in ('Hints', 'Game Messages', 'Voice Message', 'In Game Menus'):
        return 'Game'
    if top == 'Errors':
        return 'RasterizerErrors'
    if top == 'Missions':
        # Objectives, tutorial steps and team names live in Game.ltx; mission names, briefings and epilogues in Menu.ltx.
        if '_OBJECTIVE' in symbol or parts[-1].replace(' ', '') == 'TeamNames':
            return 'Game'
        return 'Menu'
    raise ValueError('no text table known for folder %r' % folder)


# --- language detection ---------------------------------------------------------------------------------------------

STOPWORDS = {
    'en': {'the', 'of', 'and', 'to', 'your', 'is', 'with', 'for', 'this', 'can', 'units', 'enemy'},
    'de': {'der', 'die', 'und', 'das', 'ist', 'mit', 'den', 'zu', 'nicht', 'ein', 'eine', 'für'},
    'fr': {'le', 'la', 'les', 'et', 'des', 'du', 'est', 'une', 'pour', 'vos', 'votre', 'avec'},
    'es': {'el', 'los', 'las', 'y', 'del', 'que', 'con', 'una', 'para', 'por', 'sus', 'su'},
    'it': {'il', 'di', 'e', 'che', 'per', 'con', 'gli', 'una', 'della', 'delle', 'sono', 'nemico'},
    'pl': {'i', 'w', 'z', 'na', 'się', 'nie', 'do', 'jest', 'oraz', 'jednostki', 'twoje', 'przez'},
}
INI_LANGUAGES = {'KOR': 'ko', 'TCH': 'zh-Hant', 'SCH': 'zh-Hans'}


def script_of(char):
    code = ord(char)
    if 0x0400 <= code <= 0x04FF:
        return 'cyrillic'
    if 0xAC00 <= code <= 0xD7AF or 0x1100 <= code <= 0x11FF:
        return 'hangul'
    if 0x3040 <= code <= 0x30FF:
        return 'kana'
    if 0x4E00 <= code <= 0x9FFF:
        return 'han'
    if char.isascii() or 0x00C0 <= code <= 0x024F:
        return 'latin'
    return 'other'


def detect_language(tables, ini_language=''):
    """(language code, evidence) from what the tables say. Cyrillic is Russian (Ukrainian if і/ї/є/ґ are common), Hangul
    Korean, kana Japanese, Han Chinese (Rings.ini [Localization] Language TCH/SCH picks the script); Latin text is told
    apart by stop words. Reads the player-facing tables only (Menu, GameConsole, Game)."""
    letters, words = {}, {}
    for name in ('Menu', 'GameConsole', 'Game'):
        for text in tables.get(name, {}).values():
            for char in text:
                if char.isalpha():
                    script = script_of(char)
                    letters[script] = letters.get(script, 0) + 1
            for word in re.findall(r"[^\W\d_]+", text.lower()):
                words[word] = words.get(word, 0) + 1
    total = sum(letters.values()) or 1
    shares = {script: round(count / total, 4) for script, count in sorted(letters.items())}
    evidence = {'script_shares': shares, 'ini_language': ini_language}
    ini = INI_LANGUAGES.get(ini_language.strip().upper())
    if shares.get('cyrillic', 0) >= .5:
        ukrainian = sum(words.get(w, 0) for w in words if any(c in w for c in 'іїєґ'))
        evidence['ukrainian_words'] = ukrainian
        return ('uk' if ukrainian > sum(words.values()) * .05 else 'ru'), evidence
    if shares.get('hangul', 0) >= .3:
        return 'ko', evidence
    if shares.get('kana', 0) >= .1:
        return 'ja', evidence
    if shares.get('han', 0) >= .3:
        return ini or 'zh-Hans', evidence
    if ini:
        return ini, evidence
    votes = {language: sum(words.get(w, 0) for w in stop) for language, stop in STOPWORDS.items()}
    evidence['stopword_votes'] = votes
    best = max(votes, key=votes.get)
    return (best if votes[best] > 0 else 'en'), evidence


# The Windows ANSI code page each release was written in. Plain-text files of the install (the AI players' .rai) name no
# encoding, so their bytes are read in the code page of the install's language (build-187).
CODE_PAGES = {'ru': 'cp1251', 'uk': 'cp1251', 'pl': 'cp1250', 'ko': 'cp949', 'ja': 'cp932', 'zh-Hans': 'cp936',
              'zh-Hant': 'cp950'}


def code_page(language):
    """The ANSI code page of an install in `language`: Western European (cp1252) for English, German, French, Spanish
    and Italian."""
    return CODE_PAGES.get(language, 'cp1252')


# --- reading the install --------------------------------------------------------------------------------------------

def read_tables(install=INSTALL):
    """{table name: {id: raw text}}, sha256 of the source, and where it came from: the install's Archives/Text.H2O, or
    extracted/Text when only an extraction is at hand."""
    archive = Path(install) / 'Archives' / 'Text.H2O'
    tables = {}
    if archive.is_file():
        source = h2o.H2OArchive(str(archive))
        for entry in source.entries:
            if entry.path.lower().endswith('.ltx'):
                tables[Path(entry.path.replace('\\', '/')).stem] = parse_ltx(source.read(entry, verify=True))
        return tables, hashlib.sha256(source.data).hexdigest(), archive.name
    folder = wor.ROOT / 'extracted' / 'Text'
    digest = hashlib.sha256()
    for path in sorted(folder.glob('*.ltx')):
        data = path.read_bytes()
        digest.update(data)
        tables[path.stem] = parse_ltx(data)
    if not tables:
        raise ValueError('no Archives/Text.H2O in %s and no extracted/Text' % install)
    return tables, digest.hexdigest(), 'extracted/Text'


def ini_language(install=INSTALL):
    path = Path(install) / 'Rings.ini'
    if not path.is_file():
        return ''
    section = ''
    for line in path.read_text(encoding='latin-1').splitlines():
        line = line.strip()
        if line.startswith('['):
            section = line.strip('[]').strip().lower()
        elif section == 'localization' and '=' in line:
            key, value = (part.strip() for part in line.split('=', 1))
            if key.lower() == 'language':
                return value
    return ''


def install_language(install=INSTALL):
    """The language an install's text tables are in (detect_language)."""
    return detect_language(read_tables(install)[0], ini_language(install))[0]


def symbol_refs(db):
    """InterfaceTextIDs record id -> `Table:id`, and short symbol (no INTERFACE_TEXT_ID_ prefix) -> `Table:id`."""
    by_record, by_symbol = {}, {}
    for record in db['InterfaceTextIDs']:
        ref = '%s:%d' % (table_for_folder(record.folder, record.name), record['InterfaceTextIDNumber'])
        by_record[record.id] = ref
        by_symbol[record.name[len(SYMBOL_PREFIX):] if record.name.startswith(SYMBOL_PREFIX) else record.name] = ref
    return by_record, by_symbol


_REFS = {}


def ref(db, record_id):
    """`Table:id` for the value of an InterfaceTextID* column, or None when the column is unset (-1) or dangling."""
    if record_id is None or record_id < 0:
        return None
    key = id(db)
    if key not in _REFS:
        _REFS[key] = symbol_refs(db)[0]
    return _REFS[key].get(record_id)


def refs(db, record, **columns):
    """{field: `Table:id`} for the InterfaceTextID columns of a database record that are set, e.g.
    refs(db, unit, name_text='InterfaceTextIDUnitName')."""
    out = {}
    for field, column in columns.items():
        value = ref(db, record.get(column, -1))
        if value:
            out[field] = value
    return out


# Database tables whose records name their text through InterfaceTextIDs: field -> column. Spells are left out: their
# names are stale placeholders (SPELL_BOROMIR_HORNOFGONDOR -> "Лезвия с шипами"); hero abilities take BattleGear's.
RECORD_COLUMNS = {
    'Units': {'name': 'InterfaceTextIDUnitName', 'title': 'InterfaceTextIDUnitSubName',
              'description': 'InterfaceTextIDUnitDescription', 'hint': 'InterfaceTextIDHint'},
    'Buildings': {'name': 'InterfaceTextIDName', 'description': 'InterfaceTextIDDescription', 'hint': 'InterfaceTextIDHint'},
    'Techniques': {'name': 'InterfaceTextIDName', 'description': 'InterfaceTextIDDescription', 'hint': 'InterfaceTextIDHint'},
    'BattleGear': {'name': 'InterfaceTextIDName', 'description': 'InterfaceTextIDDescription',
                   'effect': 'InterfaceTextIDBenefit', 'hint': 'InterfaceTextIDHint'},
    'GlobalSpells': {'name': 'InterfaceTextIDName', 'description': 'InterfaceTextIDDescription', 'hint': 'InterfaceTextIDHint'},
    'Objects': {'name': 'InterfaceTextIDName', 'description': 'InterfaceTextIDDescription'},
    'PlacesOfPower': {'effect': 'InterfaceTextIDBenefit'},
    'Missions': {'name': 'InterfaceTextIDName', 'description': 'InterfaceTextIDDescription', 'epilogue': 'InterfaceTextIDEpilogue'},
}
# Columns that hold an .ltx id directly (-1 = none): table -> (text table, {field: column}).
DIRECT_COLUMNS = {
    'MultiplayerGameModes': ('Menu', {'name': 'LocalizedNameTextID'}),
    'StatsScreenAwards': ('Menu', {'name': 'AwardTextID'}),
    'Hotkeys': ('Hotkeys_Text', {'name': 'InterfaceTextID'}),
    'HotKeyGroups': ('Hotkeys_Text', {'name': 'InterfaceTextID'}),
    'Actors': ('Game', {'name': 'ActorNameTextID'}),
    'MusicTracks': ('Game', {'name': 'TextID'}),
}


def record_refs(db, tables):
    """Symbolic record name -> {field: `Table:id`} for every record that names a text that exists."""
    by_record, _ = symbol_refs(db)
    exists = lambda value: int(value.split(':')[1]) in tables.get(value.split(':')[0], {})
    out = {}
    for table, columns in RECORD_COLUMNS.items():
        for record in db[table]:
            fields = {field: by_record[record[column]] for field, column in columns.items()
                      if record.get(column, -1) in by_record and exists(by_record[record[column]])}
            if fields:
                out[record.name] = fields
    for table, (text_table, columns) in DIRECT_COLUMNS.items():
        for record in db[table]:
            fields = {field: '%s:%d' % (text_table, record[column]) for field, column in columns.items()
                      if record.get(column, -1) >= 0 and record[column] in tables.get(text_table, {})}
            if fields:
                out[record.name] = fields
    return dict(sorted(out.items()))


def wor_text_ids(data):
    """(name id, description id) in MultiPlayerMaps.ltx from a .wor header: after the world/metadata versions and the
    editor's English name and description come these two u32s (MP02_Bilbos_Bluffs 203/204, MP04_Wasteland 415/416)."""
    reader = wor.Reader(data)
    reader.take('2I')
    reader.string(), reader.string()
    return reader.take('2I')


def map_refs(tables, levels=wor.ROOT / 'extracted' / 'Levels' / 'Multiplayer'):
    """Multiplayer map file stem -> {name, description} refs, where the table has the header's ids."""
    out = {}
    for path in sorted(Path(levels).glob('MP*.wor')):
        name, description = wor_text_ids(path.read_bytes()[:4096])
        fields = {field: 'MultiPlayerMaps:%d' % value for field, value in (('name', name), ('description', description))
                  if value in tables.get('MultiPlayerMaps', {})}
        if fields:
            out[path.stem] = fields
    return out


TECHNIQUE_ABILITY_BASE = -1000


def ability_refs(records, db):
    """Exported ability id -> refs. The combat rules give a unit ability its BattleGear id, or -1000 - Technique id when a
    technique grants it with no gear of its own (Elven Cloaks -1022); gear names win over the technique's."""
    out = {}
    for record in db['BattleGear']:
        if record.name in records:
            out[str(record.id)] = records[record.name]
    for record in db['Techniques']:
        if record.name in records:
            out[str(TECHNIQUE_ABILITY_BASE - record.id)] = records[record.name]
    return out


def build(db, install=INSTALL):
    tables, digest, source = read_tables(install)
    ini = ini_language(install)
    language, evidence = detect_language(tables, ini)
    _, symbols = symbol_refs(db)
    placeholders = sorted('%s:%d' % (name, entry_id) for name, entries in tables.items()
                          for entry_id, text in entries.items() if is_placeholder(text))
    dangling = sorted(symbol for symbol, value in symbols.items()
                      if int(value.split(':')[1]) not in tables.get(value.split(':')[0], {}))
    records = record_refs(db, tables)
    return {'format_version': FORMAT_VERSION, 'language': language, 'evidence': evidence,
            'source': source, 'source_sha256': digest,
            'tables': {name: {str(entry_id): runtime_text(text) for entry_id, text in sorted(entries.items())}
                       for name, entries in sorted(tables.items())},
            'symbols': dict(sorted(symbols.items())), 'records': records, 'maps': map_refs(tables),
            'abilities': ability_refs(records, db),
            'global_powers': {str(r.id): records[r.name] for r in db['GlobalSpells'] if r.name in records},
            'dangling_symbols': dangling, 'placeholders': placeholders}


TEXT_FILE = 'text/original.json'


def export(output, resolver, install=INSTALL):
    """Write text/original.json into a content pack folder; returns (relative path, language, evidence). The game reads
    the file by this fixed path, so refreshing it alone (`--pack`) needs no other pack file rewritten."""
    text = build(resolver.db, install)
    path = Path(output) / TEXT_FILE
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(text, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
    return TEXT_FILE, text['language'], text['evidence']


def summary(result):
    return ('language: %s  %s\n%d tables, %d entries, %d symbols (%d dangling), %d records, %d maps, %d abilities, '
            '%d placeholders') % (
        result['language'], json.dumps(result['evidence'], ensure_ascii=False), len(result['tables']),
        sum(len(t) for t in result['tables'].values()), len(result['symbols']), len(result['dangling_symbols']),
        len(result['records']), len(result['maps']), len(result['abilities']), len(result['placeholders']))


if __name__ == '__main__':
    import lqr
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--install', type=Path, default=INSTALL)
    parser.add_argument('--out', type=Path, help='write the JSON here')
    parser.add_argument('--pack', type=Path, help='write text/original.json into this content pack (converted/content/good)')
    args = parser.parse_args()
    db = lqr.Database(str(args.install / 'Database'))
    result = build(db, args.install)
    print(summary(result))
    for target in [args.out, args.pack / TEXT_FILE if args.pack else None]:
        if target:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(json.dumps(result, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
            print('->', target)
