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
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_RANDOM']['places'], [])
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_ORB_OF_REDUCED_HERO_COST']['places'], [23, 24, 25])
        self.assertEqual(sum(p['random'] for p in self.data['places']), 19)

    def test_original_model_assignments(self):
        objects = self.data['objects']
        self.assertEqual(len({o['model'] for o in objects.values()}), 15)
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_ANCIENT_FOUNTAIN']['model'], 'MODEL_POP_ANCIENT_FOUNTAIN')
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_STEED_OF_ROHAN']['model'], 'MODEL_POP_FELAROF_STATUE')
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_ORB_OF_HERO_LEVELING']['model'],
                         objects['OBJECT_PLACE_OF_POWER_ORB_OF_REDUCED_HERO_COST']['model'])
        for obj in objects.values():
            self.assertEqual(obj['file'], 'objects/places/' + obj['model'] + '.glb')

    def test_skirmish_balance(self):
        excluded = set(self.data['skirmish_excluded_objects'])
        self.assertEqual(excluded, {'OBJECT_PLACE_OF_POWER_STEED_OF_ROHAN', 'OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY',
                                    'OBJECT_PLACE_OF_POWER_ENCHANTED_WILLOW'})
        rules = {p['id']: p for p in self.data['places']}
        pool = {name for name, obj in self.data['objects'].items()
                if name not in excluded and any(rules[i]['random'] for i in obj['places'])}
        self.assertEqual(len(pool), 12)
        self.assertIn('OBJECT_PLACE_OF_POWER_MONUMENT_OF_NARSIL', pool)
        narsil = next(p for p in rules.values() if p['name'] == 'PLACE_OF_POWER_MONUMENT_OF_NARSIL')
        self.assertEqual((narsil['modifier'], narsil['float_ppm'], narsil['long'], narsil['heroes_only'], narsil['all_units']),
                         ('DAMAGE', 1_250_000, 0, True, False))
        self.assertEqual((narsil['source_rule']['modifier'], narsil['source_rule']['long'], narsil['source_rule']['all_units']),
                         ('ARMOR_CLASS', 1, True))

    def test_spell_parameters(self):
        objects = self.data['objects']
        willow = objects['OBJECT_PLACE_OF_POWER_ENCHANTED_WILLOW']['power']
        aviary = objects['OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY']['power']
        self.assertEqual((willow['TimeScalePpm'], willow['RadiusMm'], willow['RangeMm'], willow['DurationTicks'], willow['RechargeTicks']),
                         (500000, 15000, 30000, 300, 1800))
        self.assertEqual((aviary['RadiusMm'], aviary['DurationTicks'], aviary['RechargeTicks']), (25000, 300, 1800))
        self.assertEqual(objects['OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY']['display_name'], "Radagast's Aviary")
        self.assertEqual(sum(o['power'] is not None for o in objects.values()), 2)


if __name__ == '__main__':
    unittest.main()
