"""Places of power: the source places, their objects and the random pool (build-54)."""
import unittest
import places_of_power
from resolve_model import Resolver
import wor


@unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/PlacesOfPower.lqr').exists(), 'owner install not available')
class PlacesOfPowerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = places_of_power.export(Resolver())

    def test_source_places(self):
        places = {p['name']: p for p in self.data['places']}
        self.assertEqual(len(places), 28)
        sentinel = places['PLACE_OF_POWER_WRETCHED_SENTINEL']
        self.assertEqual((sentinel['radius_mm'], sentinel['modifier'], sentinel['long'], sentinel['all_units'], sentinel['random']),
                         (13000, 'ARMOR_CLASS', 1, True, True))
        self.assertEqual((places['PLACE_OF_POWER_STATUE_OF_EORL']['modifier'], places['PLACE_OF_POWER_STATUE_OF_EORL']['float_ppm'],
                          places['PLACE_OF_POWER_STATUE_OF_EORL']['heroes_only']), ('MAX_HEALTH', 1_500_000, True))
        self.assertEqual(places['PLACE_OF_POWER_CATAPULT']['radius_mm'], 17000)
        self.assertTrue(all(p['must_capture'] and not p['must_be_in_radius'] for p in places.values()))

    def test_objects_and_random_pool(self):
        objects = self.data['objects']
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_RANDOM'], {'display_name': 'Random Place of Power', 'places': []})
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_ORB_OF_REDUCED_HERO_COST']['places'], [23, 24, 25])
        self.assertEqual(sum(p['random'] for p in self.data['places']), 19)


if __name__ == '__main__':
    unittest.main()
