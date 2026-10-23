"""Shipped AI personalities and the source AI columns of the Tier 1 roster."""
import unittest
import skirmish_ai
import skirmish_construction
from resolve_model import Resolver
import wor


class SkirmishAiTests(unittest.TestCase):
    def test_rai_is_cp1251_ini(self):
        values = skirmish_ai.parse_rai('#comment\r\n[AIPlayerSettings]\r\nName = Гимли\r\nDifficulty = 1\r\n'.encode('cp1251'))
        self.assertEqual(values, {'Name': 'Гимли', 'Difficulty': '1'})

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


if __name__ == '__main__':
    unittest.main()
