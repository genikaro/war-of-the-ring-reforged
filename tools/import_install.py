"""The launcher's import (build-188): check an original War of the Ring install, then turn it into the game data Reforged
plays from.

python tools/import_install.py --check "C:/Games/War of the Ring"    (parse check only, a second or two)
python tools/import_install.py "C:/Games/War of the Ring"            (the full import)

The check reads every database table, every archive's directory, the AI players and the text tables' language. The
import then extracts every archive into extracted/ (CRC-checked), builds the shared content pack, the front-end art
and every multiplayer map plus the Sandbox. converted/install.json records it: `complete` is false while the import
runs and true once every step finished, which is when the launcher offers Start Reforged. The install is only read.

Progress goes to stdout as lines `@reforged {json}` (events check, plan, step, progress, warning, done, error), so the
launcher can follow the steps while the tools' own output stays readable in its log. `plan` lists every step's share of
the bar up front; `progress` carries `fraction`, how much of the current step is done, weighted by the work (archive
bytes, map sizes; build-191) and, while maps import, an estimate for the maps still running, so the bar keeps moving.
The exit code is 0 on success.
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
# A map's import time grows with its file: about 20 s per MB with several maps at once, plus a fixed part worth 0.7 MB.
# The rate is re-measured from every finished map; this is only the first guess.
MAP_OVERHEAD = 700_000
SECONDS_PER_BYTE = 20e-6


def emit(event, **fields):
    print('@reforged ' + json.dumps(dict(event=event, **fields)), flush=True)


class Reporter:
    """`progress` events for one step: `fraction` of the step done, never going back, at most every `interval` seconds
    unless forced."""

    def __init__(self, total, interval=0.25):
        self.total, self.interval, self.last, self.sent = total, interval, 0.0, 0.0

    def __call__(self, fraction, done, item='', force=False):
        now = time.monotonic()
        if not force and now - self.last < self.interval:
            return
        self.last, self.sent = now, min(1.0, max(fraction, self.sent))
        emit('progress', done=done, total=self.total, item=item, fraction=round(self.sent, 4))


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
    archives = [(path, h2o.H2OArchive(str(path))) for path in paths]
    total = sum(entry.raw_size for _, archive in archives for entry in archive.entries) or 1
    report = Reporter(len(paths))
    state = {'written': 0, 'index': 0, 'name': ''}

    def entry_done(entry):
        state['written'] += entry.raw_size
        report(state['written'] / total, state['index'], state['name'])

    for index, (path, archive) in enumerate(archives):
        state.update(index=index, name=path.name)
        report(state['written'] / total, index, path.name, force=True)
        destination = (ROOT / 'extracted' / path.stem).resolve()
        for entry in archive.entries:
            if not (destination / entry.path.replace('\\', '/')).resolve().is_relative_to(destination):
                raise ValueError('%s: entry path escapes the extraction folder: %s' % (path.name, entry.path))
        h2o.extract(str(path), str(ROOT / 'extracted'), verify=True, progress=entry_done)
    report(1.0, len(paths), '', force=True)


def import_map(stem, cache, sandbox):
    """One map's cache, in a worker process: (stem, error or None, the tools' output, seconds taken)."""
    import contextlib
    import io
    import prepare_prototype
    output = io.StringIO()
    started = time.monotonic()
    try:
        with contextlib.redirect_stdout(output):
            if sandbox:
                prepare_prototype.prepare_skirmish(cache, sandbox=True)
            else:
                prepare_prototype.prepare_skirmish(cache, map_name=stem)
        return stem, None, output.getvalue(), time.monotonic() - started
    except Exception as failure:  # noqa: BLE001 - the record names every failed map
        return stem, str(failure) or type(failure).__name__, output.getvalue() + traceback.format_exc(), \
            time.monotonic() - started


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
    from concurrent.futures import FIRST_COMPLETED, ProcessPoolExecutor, wait
    import prepare_prototype
    import sandbox_map
    # Largest maps first: the 8-player maps take minutes each, and started last they would finish long after the rest.
    sources = sorted((ROOT / 'extracted/Levels/Multiplayer').glob('MP*.wor'), key=lambda path: (-path.stat().st_size, path.stem))
    jobs = [(path.stem, prepare_prototype.skirmish_cache(path.stem), False) for path in sources]
    jobs.append((sandbox_map.STEM, prepare_prototype.SANDBOX_CACHE, True))
    # Each map's share of the bar follows its file size; the generated Sandbox counts as a mid-sized map.
    weights = {path.stem: path.stat().st_size + MAP_OVERHEAD for path in sources}
    weights[sandbox_map.STEM] = 1_500_000 + MAP_OVERHEAD
    total_weight = sum(weights.values())
    imported, failed = [], []
    measured = {'seconds': 0.0, 'weight': 0}
    report = Reporter(len(jobs), interval=0.5)
    report(0.0, 0, '', force=True)

    def fraction(running, now):
        """Finished maps' weight plus, for each running map, the part its expected time says is done (at most 90%)."""
        rate = measured['seconds'] / measured['weight'] if measured['weight'] else SECONDS_PER_BYTE
        done = sum(weights[stem] for stem in imported + failed)
        partial = sum(weights[stem] * min(0.9, (now - started) / (weights[stem] * rate)) for stem, started in running)
        return (done + partial) / total_weight

    def finished(stem, failure, output, seconds):
        print(output, end='' if output.endswith('\n') or not output else '\n', flush=True)
        if failure:
            failed.append(stem)
            emit('warning', message='%s did not import: %s' % (stem, failure))
        else:
            imported.append(stem)
            measured['seconds'] += seconds
            measured['weight'] += weights[stem]
            print('%s: imported (%.0f s)' % (stem, seconds), flush=True)

    # Half the cores by default: a map takes about 100 MB and 20-90 s alone; the machine stays usable meanwhile.
    workers = max(1, min(len(jobs), workers or (os.cpu_count() or 2) // 2))
    if workers == 1:
        for job in jobs:
            finished(*import_map(*job))
            report(fraction([], 0), len(imported) + len(failed), job[0], force=True)
    else:
        multiprocessing.set_executable(python_executable())
        with ProcessPoolExecutor(max_workers=workers) as pool:
            pending = {pool.submit(import_map, *job): job[0] for job in jobs}
            started = {}
            while pending:
                done, _ = wait(pending, timeout=0.5, return_when=FIRST_COMPLETED)
                now = time.monotonic()
                for future in pending:
                    if future not in started and future.running():
                        started[future] = now
                last = ''
                for future in done:
                    last = pending.pop(future)
                    started.pop(future, None)
                    finished(*future.result())
                running = [(pending[future], at) for future, at in started.items()]
                report(fraction(running, now), len(imported) + len(failed), last, force=bool(done))
    report(1.0, len(jobs), '', force=True)
    return sorted(imported), sorted(failed)


def plan():
    """Every step's title and its share of the bar: [{title, start, end}], start and end from 0 to 1."""
    total, start, steps = sum(weight for _, weight in STEPS), 0, []
    for title, weight in STEPS:
        steps.append({'title': title, 'start': round(start / total, 3), 'end': round((start + weight) / total, 3)})
        start += weight
    return steps


def step(number):
    part = plan()[number - 1]
    emit('step', step=number, steps=len(STEPS), **part)
    print('== %s' % part['title'], flush=True)


def write_record(record):
    RECORD.parent.mkdir(parents=True, exist_ok=True)
    RECORD.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')


def run_import(install, build=None, workers=None):
    import content_pack
    import frontend_assets
    started = time.time()
    emit('plan', steps=plan())
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
