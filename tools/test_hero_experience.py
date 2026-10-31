import unittest
import hero_experience
import wor
from resolve_model import Resolver


@unittest.skipUnless((wor.ROOT/'TLOTR - War of the Ring/Database/Units.lqr').exists(),'owner install not available')
class HeroExperienceImportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.db=Resolver().db

    def test_incremental_thresholds_and_level_cap(self):
        for name in ['ARAGORN','FRODO','GIMLI','LEGOLAS','GANDALF']:
            data=hero_experience.export(self.db['Units'].get('UNIT_G_HERO_'+name))
            self.assertEqual([l['ExperienceMilli'] for l in data['Levels']],
                [0,500000,1050000,1650000,2300000,3000000,3750000,4550000,5400000,6300000])
            self.assertEqual((data['UnitDamagePpm'],data['BuildingDamagePpm']),(1000000,400000))

    def test_replacement_stat_bonuses_and_frodo_exception(self):
        aragorn=hero_experience.export(self.db['Units'].get('UNIT_G_HERO_ARAGORN'))['Levels']
        frodo=hero_experience.export(self.db['Units'].get('UNIT_G_HERO_FRODO'))['Levels']
        self.assertEqual(aragorn[1],dict(Level=2,ExperienceMilli=500000,HitPoints=25,ArmorMilli=0,DamageBonusPpm=50000))
        self.assertEqual(aragorn[-1],dict(Level=10,ExperienceMilli=6300000,HitPoints=250,ArmorMilli=4000,DamageBonusPpm=500000))
        self.assertEqual(frodo[-1]['ArmorMilli'],3000)

    def test_ordinary_units_do_not_get_legacy_hero_levels(self):
        self.assertIsNone(hero_experience.export(self.db['Units'].get('UNIT_G_GONDOR_SWORDSMAN')))
        self.assertIsNone(hero_experience.export(self.db['Units'].get('UNIT_G_ELF_KNIGHT')))


if __name__=='__main__':unittest.main()
