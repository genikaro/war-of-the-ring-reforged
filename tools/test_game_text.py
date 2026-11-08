"""The original-text importer (build-183) on synthetic tables; runs without original assets."""
import struct
import unittest

import game_text


def ltx(entries, trailing=b''):
    """A LIQDTEXT table from (id, text) pairs in the given order; None stores an empty string with length 0 and no NUL."""
    body = b''
    for entry_id, text in entries:
        chars = b'' if text is None else (text + '\0').encode('utf-16-le')
        body += struct.pack('<IBI', entry_id, 0, len(chars) // 2) + chars
    return game_text.MAGIC + struct.pack('<fI', 1.0, len(entries)) + body + trailing


class ParseTests(unittest.TestCase):
    def test_entries_are_read_by_id_in_any_order_with_empty_strings(self):
        table = game_text.parse_ltx(ltx([(30, 'Гондорский мечник'), (0, 'Работник'), (5, None)]))
        self.assertEqual(table, {30: 'Гондорский мечник', 0: 'Работник', 5: ''})

    def test_rejects_bad_magic_trailing_bytes_short_entries_and_duplicates(self):
        with self.assertRaises(ValueError):
            game_text.parse_ltx(b'LIQDFONT' + bytes(8))
        with self.assertRaises(ValueError):
            game_text.parse_ltx(ltx([(1, 'a')], trailing=b'\0'))
        with self.assertRaises(ValueError):
            game_text.parse_ltx(ltx([(1, 'abc')])[:-2])
        with self.assertRaises(ValueError):
            game_text.parse_ltx(ltx([(1, 'a'), (1, 'b')]))


class RuntimeTextTests(unittest.TestCase):
    def test_breaks_become_newlines_and_printf_arguments_are_numbered(self):
        self.assertEqual(game_text.runtime_text('Описание<br><BR>Особые возможности'), 'Описание\n\nОсобые возможности')
        self.assertEqual(game_text.runtime_text('%s (союзникам) >> %s'), '{0} (союзникам) >> {1}')
        self.assertEqual(game_text.runtime_text('Вы будете обнаружены через %d сек.'), 'Вы будете обнаружены через {0} сек.')
        self.assertEqual(game_text.runtime_text('%.02f MB, 100%%'), '{0} MB, 100%')
        self.assertEqual(game_text.runtime_text('+10% к атаке'), '+10% к атаке')

    def test_placeholders(self):
        for stub in ['', 'Затычка под описание', 'хххKeep level', 'xxxMake passive', '<Название заклинания>',
                     '<Capture Benefit>', '<Decommission Description>', 'Place Of Power Buff 3',
                     # Build-187: the English release's stubs.
                     'Empty description', 'Placeholder', '<Placeholder Description>', 'xxxxx']:
            self.assertTrue(game_text.is_placeholder(stub), stub)
        for text in ['<играет>', 'Гондорский мечник', 'Вы победили!', 'Make passive', 'Keep level', 'Gondor Swordsman',
                     'Description', 'Placeholders are stubs.']:
            self.assertFalse(game_text.is_placeholder(text), text)


class FolderTests(unittest.TestCase):
    def test_editor_folders_pick_the_table(self):
        cases = [('Root\\Units\\Good', 'UNIT_G_GONDOR_SWORDSMAN_NAME', 'GameConsole'),
                 ('Root\\Console\\ToolTips', 'TOOLTIPS_LEVEL', 'GameConsole'),
                 ('Root\\Battlegear', 'BATTLE_GEAR_X', 'GameConsole'),
                 ('Root\\Objects\\Places of Power', 'X', 'GameConsole'),
                 ('Root\\Hints\\Unit Hints', 'HINTS_UNITS_X', 'Game'),
                 ('Root\\Game Messages\\Alerts', 'X', 'Game'),
                 ('Root\\Errors', 'ERROR_CODE', 'RasterizerErrors'),
                 ('Root\\Missions\\Grey Ledge', 'INTERFACE_TEXT_ID_MISSION_GREY_LEDGE_NAME', 'Menu'),
                 ('Root\\Missions\\Grey Ledge', 'INTERFACE_TEXT_ID_MISSION_GREY_LEDGE_OBJECTIVE_KILL_LEADER', 'Game'),
                 ('Root\\Missions\\Balrog\\TeamNames', 'X', 'Game'),
                 ('Root\\Missions\\Grey Ledge\\Team Names', 'X', 'Game')]
        for folder, symbol, table in cases:
            self.assertEqual(game_text.table_for_folder(folder, symbol), table, folder)
        with self.assertRaises(ValueError):
            game_text.table_for_folder('Root\\Unknown')


class LanguageTests(unittest.TestCase):
    def detect(self, menu, console='', ini=''):
        return game_text.detect_language({'Menu': {1: menu}, 'GameConsole': {1: console}}, ini)[0]

    def test_scripts_and_stop_words(self):
        self.assertEqual(self.detect('Один игрок', 'Гондорский мечник - воин из народа Людей.'), 'ru')
        self.assertEqual(self.detect('Single Player', 'The swordsman is the backbone of your army and can hold the line.'), 'en')
        self.assertEqual(self.detect('Einzelspieler', 'Der Schwertkämpfer ist das Rückgrat der Armee und die beste Wahl.'), 'de')
        self.assertEqual(self.detect('Joueur unique', 'Le guerrier est la base de votre armée et des troupes.'), 'fr')
        self.assertEqual(self.detect('싱글 플레이', '곤도르 검사'), 'ko')
        self.assertEqual(self.detect('單人遊戲', '剛鐸劍士', ini='TCH'), 'zh-Hant')
        self.assertEqual(self.detect('Gra', 'xyz', ini='KOR'), 'ko')

    def test_code_pages(self):
        # Build-187: plain-text install files are read in the ANSI code page of the install's language.
        self.assertEqual([game_text.code_page(language) for language in ['ru', 'uk', 'en', 'de', 'pl', 'ko', 'zh-Hant']],
                         ['cp1251', 'cp1251', 'cp1252', 'cp1252', 'cp1250', 'cp949', 'cp950'])


class RefTests(unittest.TestCase):
    class Db:
        class Record:
            def __init__(self, rid, name, number, folder):
                self.id, self.name, self.folder, self.values = rid, name, folder, {'InterfaceTextIDNumber': number}

            def __getitem__(self, key):
                return self.values[key]

        def __init__(self):
            self.table = [self.Record(0, 'INTERFACE_TEXT_ID_UNIT_G_GONDOR_SWORDSMAN_NAME', 30, 'Root\\Units\\Good'),
                          self.Record(7, 'INTERFACE_TEXT_ID_HINTS_UNITS_SWORDSMAN', 1302, 'Root\\Hints\\Unit Hints')]

        def __getitem__(self, name):
            return self.table

    def test_record_zero_is_a_real_text_and_minus_one_is_none(self):
        db = self.Db()
        self.assertEqual(game_text.ref(db, 0), 'GameConsole:30')
        self.assertEqual(game_text.ref(db, 7), 'Game:1302')
        self.assertIsNone(game_text.ref(db, -1))
        self.assertIsNone(game_text.ref(db, 99))
        record = {'InterfaceTextIDUnitName': 0, 'InterfaceTextIDHint': 7, 'InterfaceTextIDUnitDescription': -1}
        self.assertEqual(game_text.refs(db, record, name_text='InterfaceTextIDUnitName', hint_text='InterfaceTextIDHint',
                                        description_text='InterfaceTextIDUnitDescription'),
                         {'name_text': 'GameConsole:30', 'hint_text': 'Game:1302'})
        self.assertEqual(game_text.symbol_refs(db)[1]['UNIT_G_GONDOR_SWORDSMAN_NAME'], 'GameConsole:30')


class MapHeaderTests(unittest.TestCase):
    def test_text_ids_follow_the_editor_name_and_description(self):
        def string(text):
            chars = (text + '\0').encode('utf-16-le')
            return b'\0' + struct.pack('<I', len(chars) // 2) + chars
        header = struct.pack('<2I', 125, 102) + string("Bilbo's Bluffs") + string('') + struct.pack('<2I', 203, 204) + bytes(32)
        self.assertEqual(game_text.wor_text_ids(header), (203, 204))


if __name__ == '__main__':
    unittest.main()
