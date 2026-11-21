"""Audio source mapping and original-install protection, without proprietary assets."""
import io
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
import wave
from unittest.mock import patch

import game_audio


def record(number, name, **values):
    class Record(dict):
        pass
    result = Record(values)
    result.id, result.name = number, name
    return result


def table(records):
    return SimpleNamespace(records=records, get=lambda key: next((r for r in records if r.id == key or r.name == key), None))


class AudioExportTests(unittest.TestCase):
    def test_export_rejects_original_and_extracted_output(self):
        for folder in ['extracted/audio-test', 'TLOTR - War of the Ring/audio-test']:
            with self.assertRaisesRegex(ValueError, 'outside'):
                game_audio.export(game_audio.wor.ROOT / folder)

    def test_source_speech_and_ordered_menu_good_evil_music(self):
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            pcm = io.BytesIO()
            with wave.open(pcm, 'wb') as wav:
                wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(44100); wav.writeframes(b'\0\0'*10)
            (root / 'speech.wav').write_bytes(pcm.getvalue())
            (root / 'build.ogg').write_bytes(b'local-build-ogg')
            (root / 'battle.ogg').write_bytes(b'local-battle-ogg')
            (root / 'evil.ogg').write_bytes(b'local-evil-ogg')
            (root / 'menu.ogg').write_bytes(b'local-menu-ogg')
            unit = record(1, 'UNIT_G_TEST', MeleeWeapon=-1)
            event = record(9, 'SELECTED', Unit=1, EventType=4, ChanceToPlay=.75, MinVolume=.8,
                           **{'SpeechFX_%d' % i: 2 if i == 10 else -1 for i in range(1, 11)},
                           **{'Weight_%d' % i: 3 if i == 10 else 0 for i in range(1, 11)})
            db = {'Units': table([unit]), 'SpeechFXEvents': table([event]),
                  'SpeechFXEventTypes': table([record(4, 'SELECTED', Priority=100, ExclusivityGroup=0)]),
                  'SpeechFX': table([record(2, 'LINE', Filename='speech.wav')]),
                  'Weapons': table([]), 'WeaponEffects': table([]), 'SoundEvents': table([]),
                  'MusicTracks': table([record(0, 'MUSIC_TRACK_TEST_GOOD', ClanAffiliation=1),
                                        record(1, 'MUSIC_TRACK_TEST_EVIL', ClanAffiliation=2),
                                        record(2, game_audio.MENU_TRACK, ClanAffiliation=2),
                                        record(3, 'MUSIC_TRACK_CINE_TEST', ClanAffiliation=-1),
                                        record(4, 'MUSIC_TRACK_SECOND_GOOD', ClanAffiliation=1),
                                        record(5, 'MUSIC_TRACK_GENERIC9', ClanAffiliation=0)]),
                  'Music': table([record(6, 'SECOND', Track=4, Theme=1, Filename='build.wav'),
                                  record(1, 'BUILD', Track=0, Theme=1, Filename='build.wav'),
                                  record(2, 'BATTLE', Track=0, Theme=2, Filename='missing.wav'),
                                  record(3, 'EVIL', Track=1, Theme=1, Filename='evil.wav'),
                                  record(4, 'MENU', Track=2, Theme=1, Filename='menu.wav'),
                                  record(5, 'CINEMATIC', Track=3, Theme=1, Filename='missing.wav'),
                                  record(7, 'GENERIC', Track=5, Theme=1, Filename='build.wav')])}
            resolver = SimpleNamespace(db=db, index=SimpleNamespace(find=lambda _, name:
                str(root / name) if (root / name).is_file() else None))
            with patch.object(game_audio, 'EFFECT_EVENTS', []):
                data = game_audio.export(root / 'output', resolver)
            speech = data['Speech']['UNIT_G_TEST']['4']
            self.assertEqual((speech['Chance'], speech['Volume'], speech['Clips'][0]['Weight']), (.75, .8, 3))
            self.assertEqual(data['FormatVersion'], 2)
            self.assertEqual(data['Music'], {
                'Menu': [{'Name': game_audio.MENU_TRACK, 'File': 'audio/music/4.ogg'}],
                'Good': [{'Name': 'MUSIC_TRACK_TEST_GOOD', 'File': 'audio/music/1.ogg'},
                         {'Name': 'MUSIC_TRACK_SECOND_GOOD', 'File': 'audio/music/6.ogg'},
                         {'Name': 'MUSIC_TRACK_GENERIC9', 'File': 'audio/music/7.ogg'}],
                'Evil': [{'Name': 'MUSIC_TRACK_TEST_EVIL', 'File': 'audio/music/3.ogg'},
                         {'Name': game_audio.MENU_TRACK, 'File': 'audio/music/4.ogg'},
                         {'Name': 'MUSIC_TRACK_GENERIC9', 'File': 'audio/music/7.ogg'}]})
            self.assertFalse((root / 'output/audio/music/2.ogg').exists())
            self.assertEqual((root / 'output/audio/music/4.ogg').read_bytes(), b'local-menu-ogg')
            self.assertEqual((root / 'output/audio/speechfx/2.wav').read_bytes(), pcm.getvalue())
            self.assertEqual(json.loads((root / 'output/audio/index.json').read_text())['Sources'], data['Sources'])


if __name__ == '__main__':
    unittest.main()
