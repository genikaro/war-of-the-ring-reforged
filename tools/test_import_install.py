"""The launcher's import check (build-188) on folders that are not installs; no game files needed."""
import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path

import import_install


class CheckTest(unittest.TestCase):
    def codes(self, folder):
        report = import_install.check(folder)
        self.assertFalse(report['ok'])
        return [error['code'] for error in report['errors']]

    def test_missing_folder(self):
        with tempfile.TemporaryDirectory() as root:
            self.assertEqual(self.codes(Path(root) / 'nope'), ['missing_folder'])

    def test_folder_without_game(self):
        with tempfile.TemporaryDirectory() as root:
            self.assertEqual(self.codes(root), ['not_install'])

    def test_empty_database_and_archives(self):
        with tempfile.TemporaryDirectory() as root:
            (Path(root) / 'Database').mkdir()
            (Path(root) / 'Archives').mkdir()
            report = import_install.check(root)
            self.assertEqual([e['code'] for e in report['errors']], ['no_database', 'missing_archives'])
            self.assertIn('Levels.H2O', report['errors'][1]['detail'])
            self.assertTrue(report['warnings'])  # no AIPlayers folder

    def test_broken_archive_is_a_parse_error(self):
        with tempfile.TemporaryDirectory() as root:
            (Path(root) / 'Archives').mkdir()
            (Path(root) / 'Archives' / 'Levels.H2O').write_bytes(b'not an archive')
            self.assertIn('parse', self.codes(root))


class ProgressTest(unittest.TestCase):
    def test_steps_cover_the_whole_bar(self):
        lines = io.StringIO()
        with contextlib.redirect_stdout(lines):
            for number in range(1, len(import_install.STEPS) + 1):
                import_install.step(number)
        steps = [json.loads(line[len('@reforged '):]) for line in lines.getvalue().splitlines() if line.startswith('@reforged ')]
        self.assertEqual(steps[0]['start'], 0)
        self.assertEqual(steps[-1]['end'], 1)
        for previous, current in zip(steps, steps[1:]):
            self.assertEqual(previous['end'], current['start'])

    def test_plan_matches_the_steps(self):
        steps = import_install.plan()
        self.assertEqual(len(steps), len(import_install.STEPS))
        self.assertEqual((steps[0]['start'], steps[-1]['end']), (0, 1))

    def test_progress_never_goes_back(self):
        lines = io.StringIO()
        with contextlib.redirect_stdout(lines):
            report = import_install.Reporter(4, interval=0)
            for fraction in (0.2, 0.5, 0.3, 1.5):
                report(fraction, 1, force=True)
        sent = [json.loads(line[len('@reforged '):])['fraction'] for line in lines.getvalue().splitlines()]
        self.assertEqual(sent, [0.2, 0.5, 0.5, 1.0])


if __name__ == '__main__':
    unittest.main()
