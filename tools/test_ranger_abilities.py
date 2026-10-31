import unittest

import hero_abilities
import lsk
import skirmish_research
import wor
from resolve_model import Resolver


@unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Techniques.lqr').exists(), 'owner install not available')
class RangerImportTests(unittest.TestCase):
    def test_source_slots_animation_and_recharge(self):
        resolver = Resolver()
        unit = resolver.db['Units'].get('UNIT_G_RANGER')
        model = resolver.resolve(unit.name, recurse=False)
        skeleton = lsk.parse((wor.ROOT / model['skeletons'][0]['file']).read_bytes())
        camouflage, eagle, detection = hero_abilities.export(resolver, unit, model, skeleton)
        self.assertEqual((camouflage['Kind'], camouflage['Slot'], camouflage['Passive'], camouflage['RechargeTicks']), (15, 0, False, 60))
        self.assertEqual((camouflage['CastClip'], camouflage['SoundEvents']), ('G_Ranger_Idle_03', [375]))
        self.assertEqual((eagle['Kind'], eagle['Slot'], eagle['Passive']), (16, 2, True))
        self.assertEqual((detection['Kind'], detection['Slot'], detection['Passive']), (17, 3, True))
        self.assertNotIn('RequiredTechnique', detection)
        self.assertEqual(camouflage['RequiredTechnique'], 'TECHNIQUE_CAMOUFLAGE')
        self.assertEqual(eagle['RequiredTechnique'], 'TECHNIQUE_EAGLE_EYE')
        self.assertEqual(camouflage['Source']['Gear']['GroupActivate'], 1)
        self.assertEqual(unit['SightRange'], 20)
        self.assertEqual(unit['DetectorType1'], 0)

    def test_ranger_research_costs_prerequisite_and_sight(self):
        data = skirmish_research.research_data(Resolver())['techniques']
        for name, slot in (('TECHNIQUE_CAMOUFLAGE', 2), ('TECHNIQUE_EAGLE_EYE', 3)):
            research = data[name]
            self.assertEqual((research['building'], research['slot']), ('BUILDING_G_ARMORY', slot))
            self.assertEqual((research['food_cost'], research['ore_cost'], research['duration_ticks']), (75, 75, 1800))
            self.assertEqual(research['prerequisite'], 'BUILDING_G_TOWN_CENTER_LEVEL_2')
            self.assertIn('UNIT_G_RANGER', research['bonuses'])
        self.assertEqual(data['TECHNIQUE_EAGLE_EYE']['bonuses']['UNIT_G_RANGER']['sight_ppm'], 2000000)
        for name, amount in (('ETCHED_BLADES', 1000), ('MYSTIC_BLADES', 2000), ('RUNIC_BLADES', 3000)):
            self.assertEqual(data['TECHNIQUE_'+name]['bonuses']['UNIT_G_RANGER']['damage_milli'], amount)
        for name, amount in (('IMPROVED_LEATHER', 1000), ('HARD_LEATHER', 2000), ('STUDDED_LEATHER', 3000)):
            self.assertEqual(data['TECHNIQUE_'+name]['bonuses']['UNIT_G_RANGER']['armor_milli'], amount)


if __name__ == '__main__':
    unittest.main()
