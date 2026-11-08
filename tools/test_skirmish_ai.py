"""Shipped AI personalities and the source AI columns of the Tier 1 roster."""
import tempfile
import unittest
from pathlib import Path

import skirmish_ai
import skirmish_construction
from resolve_model import Resolver
import wor

ENGLISH_INSTALL = wor.ROOT / 'Legacy/Game EN'
RAI = ('[AIPlayerSettings]\r\nName = {}\r\nDifficulty = 2\r\nAggressiveness = 70\r\nTactics = 40\r\nScouting = 30\r\n'
       'Harassment = 60\r\nUsesSpecialAbilities = 1\r\nUsesHeroes = 0\r\n')


class SkirmishAiTests(unittest.TestCase):
    def test_rai_is_cp1251_ini(self):
        values = skirmish_ai.parse_rai('#comment\r\n[AIPlayerSettings]\r\nName = Гимли\r\nDifficulty = 1\r\n'.encode('cp1251'))
        self.assertEqual(values, {'Name': 'Гимли', 'Difficulty': '1'})

    def test_names_follow_the_install_language(self):
        # Build-187: the English release writes Grishnákh in cp1252, which cp1251 would read as "Grishnбkh".
        with tempfile.TemporaryDirectory() as folder:
            (Path(folder) / 'AIPlayers').mkdir()
            (Path(folder) / 'AIPlayers/Grishnakh.rai').write_bytes(RAI.format('Grishnákh').encode('cp1252'))
            people = {p['id']: p for p in skirmish_ai.personalities(folder, 'en')}
            self.assertEqual((people['Grishnakh']['display_name'], people['Grishnakh']['file'], people['Grishnakh']['uses_heroes']),
                             ('Grishnákh', 'AIPlayers/Grishnakh.rai', False))
            (Path(folder) / 'AIPlayers/Grishnakh.rai').write_bytes(RAI.format('Гришнак').encode('cp1251'))
            self.assertEqual(skirmish_ai.personalities(folder, 'ru')[1]['display_name'], 'Гришнак')

    @unittest.skipUnless((ENGLISH_INSTALL / 'Archives/Text.H2O').exists(), 'English install not available')
    def test_english_install_names(self):
        people = {p['id']: p['display_name'] for p in skirmish_ai.personalities(ENGLISH_INSTALL)}
        self.assertEqual((people['Grishnakh'], people['FarmerMaggot'], len(people)), ('Grishnákh', 'Farmer Maggot', 13))

    @unittest.skipUnless((wor.ROOT / skirmish_ai.AI_PLAYERS / 'Gimli.rai').exists(), 'owner install not available')
    def test_shipped_personalities(self):
        people = {p['id']: p for p in skirmish_ai.personalities()}
        self.assertEqual(len(people), 13)
        self.assertEqual((people['Default']['difficulty'], people['Default']['aggressiveness'], people['Default']['file']), (1, 50, None))
        gimli = people['Gimli']
        self.assertEqual((gimli['display_name'], gimli['difficulty'], gimli['aggressiveness'], gimli['tactics'], gimli['harassment']),
                         ('Гимли', 1, 35, 65, 20))
        self.assertEqual((people['FarmerMaggot']['uses_heroes'], people['Smaug']['aggressiveness']), (False, 100))

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Buildings.lqr').exists(), 'owner install not available')
    def test_source_ai_columns(self):
        data = skirmish_ai.export(Resolver(), skirmish_construction.CONSTRUCTION_ORDER,
                                  ['UNIT_G_HUMAN_WORKER', 'UNIT_G_GONDOR_SWORDSMAN', 'UNIT_G_DWARVEN_AXETHROWER'])
        barracks, tower = data['buildings']['BUILDING_G_GONDORIAN_BARRACKS'], data['buildings']['BUILDING_G_TOWER']
        self.assertEqual((barracks['class'], barracks['must_have_first_pass'], barracks['max_default'], barracks['lower_priority_if_exists']),
                         ('MILITARY', False, 3, True))
        self.assertEqual((tower['class'], tower['must_have_first_pass']), ('DEFENSIVE', True))
        self.assertEqual(data['buildings']['BUILDING_G_FOOD_STRUCTURE']['lower_priority_if_exists'], False)
        units = data['units']
        self.assertEqual((units['UNIT_G_HUMAN_WORKER']['never_add'], units['UNIT_G_GONDOR_SWORDSMAN']['assault'],
                          units['UNIT_G_DWARVEN_AXETHROWER']['assault']), (True, 70, 60))
        self.assertEqual(units['UNIT_G_DWARVEN_AXETHROWER']['origin'], 'BUILDING_G_DWARF_BARRACKS')
        self.assertEqual(data['war_parties']['ASSAULT'], {'min_size': 5, 'max_size': 12, 'members': [
            {'class': 'Assault Melee', 'kind': 'melee', 'share_ppm': 800000},
            {'class': 'Assault Missile', 'kind': 'missile', 'share_ppm': 200000}]})
        raiding = data['war_parties']['RAIDING']
        self.assertEqual((raiding['min_size'], raiding['max_size'], [m['share_ppm'] for m in raiding['members']]), (3, 6, [400000, 400000, 200000]))
        self.assertEqual(units['UNIT_G_GONDOR_SWORDSMAN']['raiding'], 50)
        scout = data['war_parties']['PLACEOFPOWERSCOUT']
        self.assertEqual((scout['min_size'], scout['max_size'], [m['class'] for m in scout['members']]), (5, 12, ['Place of Power Scout Primary']))


if __name__ == '__main__':
    unittest.main()
