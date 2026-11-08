"""The launcher's import (build-188): check an original War of the Ring install, then turn it into the game data Reforged
plays from.

python tools/import_install.py --check "C:/Games/War of the Ring"    (parse check only, a second or two)
python tools/import_install.py "C:/Games/War of the Ring"            (the full import)

The check reads every database table, every archive's directory, the AI players and the text tables' language. The
import then extracts every archive into extracted/ (CRC-checked), builds the shared content pack, the front-end art
and every multiplayer map plus the Sandbox. converted/install.json records it: `complete` is false while the import
runs and true once every step finished, which is when the launcher offers Start Reforged. The install is only read.

Progress goes to stdout as lines `@reforged {json}` (events check, step, progress, warning, done, error), so the
launcher can follow the steps while the tools' own output stays readable in its log. The exit code is 0 on success.
Check errors carry a code (missing_folder, not_install, no_database, missing_archives, parse) for the launcher's text.
"""
import argparse
import datetime
import json
import os
import sys
import time
import traceback
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RECORD = ROOT / 'converted' / 'install.json'
RECORD_FORMAT = 1
# The archives the import reads. The rest (Movies, Dialogue, Screens) are extracted when present.
REQUIRED_ARCHIVES = ['Cloths', 'Cursors', 'Effects', 'Fonts', 'Levels', 'Materials', 'Meshes', 'MeshPathLookups', 'Models',
                     'Music', 'Projectiles', 'Skeletons', 'SoundEffects', 'SpeechFX', 'TerrainSets', 'Text',
                     'TextureAnimations', 'Textures', 'UVAs', 'Weights']
LANGUAGES = {'en': 'English', 'ru': 'Russian', 'uk': 'Ukrainian', 'de': 'German', 'fr': 'French', 'es': 'Spanish',
             'it': 'Italian', 'pl': 'Polish', 'cs': 'Czech', 'hu': 'Hungarian', 'ko': 'Korean', 'ja': 'Japanese',
             'zh': 'Chinese'}
# The import's steps and their share of its time (measured on the English install), for the launcher's bar.
STEPS = [('Checking the install', 1), ('Extracting archives', 6), ('Building the content pack', 8),
         ('Building the menu art', 1), ('Importing maps', 84)]


def emit(event, **fields):
    print('@reforged ' + json.dumps(dict(event=event, **fields)), flush=True)


def check(install):
    """What the install holds and whether every part the import needs parses: a report with `ok` and `errors`, each
    error {code, message, detail}."""
    import game_text
    import h2o
    import lqr
    import skirmish_ai
    install = Path(install)
    report = {'install': str(install), 'ok': False, 'errors': [], 'warnings': []}

    def error(code, message, detail=''):
        report['errors'].append({'code': code, 'message': message, 'detail': detail})

    if not install.is_dir():
        error('missing_folder', 'The folder does not exist.')
        return report
    if not (install / 'Database').is_dir() and not (install / 'Archives').is_dir():
        error('not_install', 'No Database or Archives folder here: choose the folder War of the Ring is installed in.')
        return report
    database = install / 'Database'
    if not (database / 'Models.lqr').is_file():
        error('no_database', 'No Database\\Models.lqr in this folder.')
    else:
        try:
            db = lqr.Database(str(database))
            report['tables'] = len(db.files)
            report['records'] = sum(len(db[name].records) for name in db.files)
        except Exception as failure:  # noqa: BLE001 - any parse failure is the report's answer
            error('parse', 'Database does not parse: %s' % failure, 'Database: %s' % failure)
    archives = install / 'Archives'
    found = {path.stem.lower(): path for path in archives.glob('*.H2O')} if archives.is_dir() else {}
    missing = [name + '.H2O' for name in REQUIRED_ARCHIVES if name.lower() not in found]
    if missing:
        error('missing_archives', 'Archives missing: %s.' % ', '.join(missing), ', '.join(missing))
    files = 0
    for path in sorted(found.values()):
        try:
            files += len(h2o.H2OArchive(str(path)).entries)
        except Exception as failure:  # noqa: BLE001
            error('parse', '%s does not parse: %s' % (path.name, failure), '%s: %s' % (path.name, failure))
    report['archives'], report['files'] = len(found), files
    if 'text' in found:
        try:
            report['language'] = game_text.install_language(install)
            report['language_name'] = LANGUAGES.get(report['language'], report['language'])
        except Exception as failure:  # noqa: BLE001
            error('parse', 'Text.H2O does not parse: %s' % failure, 'Text.H2O: %s' % failure)
    report['ai_players'] = 0
    if not (install / 'AIPlayers').is_dir():
        report['warnings'].append('No AIPlayers folder: bots play the default personality only.')
    else:
        try:
            report['ai_players'] = len(skirmish_ai.personalities(install, report.get('language'))) - 1
        except Exception as failure:  # noqa: BLE001
            error('parse', 'AI players do not parse: %s' % failure, 'AIPlayers: %s' % failure)
    report['ok'] = not report['errors']
    return report


def extract(install):
    """Every archive into extracted/<archive name>/, CRC-checked, entry paths kept inside it."""
    import h2o
    paths = sorted((Path(install) / 'Archives').glob('*.H2O'))
    for index, path in enumerate(paths):
        emit('progress', done=index, total=len(paths), item=path.name)
        archive = h2o.H2OArchive(str(path))
        destination = (ROOT / 'extracted' / path.stem).resolve()
        for entry in archive.entries:
            if not (destination / entry.path.replace('\\', '/')).resolve().is_relative_to(destination):
                raise ValueError('%s: entry path escapes the extraction folder: %s' % (path.name, entry.path))
        h2o.extract(str(path), str(ROOT / 'extracted'), verify=True)
    emit('progress', done=len(paths), total=len(paths), item='')


def import_map(stem, cache, sandbox):
    """One map's cache, in a worker process: (stem, error or None, the tools' output)."""
    import contextlib
    import io
    import prepare_prototype
    output = io.StringIO()
    try:
        with contextlib.redirect_stdout(output):
            if sandbox:
                prepare_prototype.prepare_skirmish(cache, sandbox=True)
            else:
                prepare_prototype.prepare_skirmish(cache, map_name=stem)
        return stem, None, output.getvalue()
    except Exception as failure:  # noqa: BLE001 - the record names every failed map
        return stem, str(failure) or type(failure).__name__, output.getvalue() + traceback.format_exc()


def python_executable():
    """The running interpreter's file. A portable Python renamed to python3.13.exe still reports python.exe as
    sys.executable, which worker processes cannot be started from."""
    if os.path.isfile(sys.executable) or os.name != 'nt':
        return sys.executable
    import ctypes
    buffer = ctypes.create_unicode_buffer(32768)
    ctypes.windll.kernel32.GetModuleFileNameW(None, buffer, len(buffer))
    return buffer.value


def import_maps(workers=None):
    """Every multiplayer map into its own cache, then the Sandbox, several at once (each writes only its own folder);
    a failed map is a warning, not the import's end. `workers` 1 imports them one by one in this process."""
    import multiprocessing
    from concurrent.futures import ProcessPoolExecutor, as_completed
    import prepare_prototype
    import sandbox_map
    # Largest maps first: the 8-player maps take minutes each, and started last they would finish long after the rest.
    sources = sorted((ROOT / 'extracted/Levels/Multiplayer').glob('MP*.wor'), key=lambda path: (-path.stat().st_size, path.stem))
    jobs = [(path.stem, prepare_prototype.skirmish_cache(path.stem), False) for path in sources]
    jobs.append((sandbox_map.STEM, prepare_prototype.SANDBOX_CACHE, True))
    imported, failed = [], []
    emit('progress', done=0, total=len(jobs), item='')

    def finished(stem, failure, output):
        print(output, end='' if output.endswith('\n') or not output else '\n', flush=True)
        if failure:
            failed.append(stem)
            emit('warning', message='%s did not import: %s' % (stem, failure))
        else:
            imported.append(stem)
            print('%s: imported' % stem, flush=True)
        emit('progress', done=len(imported) + len(failed), total=len(jobs), item=stem)

    # Half the cores by default: a map takes about 100 MB and 20-90 s alone; the machine stays usable meanwhile.
    workers = max(1, min(len(jobs), workers or (os.cpu_count() or 2) // 2))
    if workers == 1:
        for job in jobs:
            finished(*import_map(*job))
    else:
        multiprocessing.set_executable(python_executable())
        with ProcessPoolExecutor(max_workers=workers) as pool:
            for future in as_completed([pool.submit(import_map, *job) for job in jobs]):
                finished(*future.result())
    return sorted(imported), sorted(failed)


def step(number):
    total = sum(weight for _, weight in STEPS)
    start = sum(weight for _, weight in STEPS[:number - 1]) / total
    title, weight = STEPS[number - 1]
    emit('step', step=number, steps=len(STEPS), title=title, start=round(start, 3), end=round(start + weight / total, 3))
    print('== %s' % title, flush=True)


def write_record(record):
    RECORD.parent.mkdir(parents=True, exist_ok=True)
    RECORD.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')


def run_import(install, build=None, workers=None):
    import content_pack
    import frontend_assets
    started = time.time()
    step(1)
    report = check(install)
    emit('check', report=report)
    if not report['ok']:
        raise ValueError(' '.join(error['message'] for error in report['errors']))
    record = {'format_version': RECORD_FORMAT, 'complete': False, 'install': str(Path(install).resolve()),
              'language': report.get('language'), 'language_name': report.get('language_name'), 'build': build,
              'started': datetime.datetime.now().isoformat(timespec='seconds')}
    write_record(record)
    step(2)
    extract(install)
    step(3)
    pack = content_pack.build(content_pack.PACK)
    print('Content pack: %d units, %d techniques, text language %s' % (
        len(pack['models']), len(pack['skirmish']['research']['techniques']), pack['language']), flush=True)
    step(4)
    frontend_assets.export(frontend_assets.DEFAULT_OUT)
    step(5)
    imported, failed = import_maps(workers)
    if not imported:
        raise ValueError('No map imported.')
    record.update(complete=True, imported=datetime.datetime.now().isoformat(timespec='seconds'),
                  seconds=round(time.time() - started), maps=imported, failed_maps=failed)
    write_record(record)
    return record


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install', type=Path, help='the folder War of the Ring is installed in (holds Database and Archives)')
    parser.add_argument('--check', action='store_true', help='only check that the install parses')
    parser.add_argument('--build', type=int, help='the Reforged build importing, kept in converted/install.json')
    parser.add_argument('--jobs', type=int, help='maps imported at once (default half the cores; 1 = one by one)')
    args = parser.parse_args()
    # Text from any install language reaches the launcher's log intact.
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    install = args.install.resolve()
    if install == ROOT or ROOT.is_relative_to(install) or install.is_relative_to(ROOT / 'converted') \
            or install.is_relative_to(ROOT / 'extracted'):
        report = {'install': str(install), 'ok': False, 'warnings': [], 'errors': [{
            'code': 'reforged_folder', 'message': 'Choose the original game folder, not the Reforged folder.', 'detail': ''}]}
        emit('check', report=report)
        emit('error', message=report['errors'][0]['message'])
        return 2
    # Every tool reads this install when given none (lqr.INSTALL); set before they are imported.
    os.environ['REFORGED_INSTALL'] = str(install)
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    if args.check:
        report = check(install)
        emit('check', report=report)
        return 0 if report['ok'] else 1
    try:
        record = run_import(install, args.build, args.jobs)
    except Exception as failure:  # noqa: BLE001 - the launcher shows the message, the log has the trace
        traceback.print_exc(file=sys.stdout)
        emit('error', message=str(failure) or type(failure).__name__)
        return 1
    emit('done', record=record)
    print('Imported %s in %d s: %d maps%s' % (record['install'], record['seconds'], len(record['maps']),
          ', failed: ' + ', '.join(record['failed_maps']) if record['failed_maps'] else ''), flush=True)
    return 0


if __name__ == '__main__':
    sys.exit(main())
