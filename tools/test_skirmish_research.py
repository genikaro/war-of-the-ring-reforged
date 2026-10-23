"""Source research rules at the Forge and Wilderness Outpost."""
import unittest

import skirmish_research
from resolve_model import Resolver
import wor


class ResearchImportTests(unittest.TestCase):
    def test_names_follow_the_english_manual(self):
        self.assertEqual(skirmish_research.display_name('TECHNIQUE_IRON_BLADES'), 'Iron Blades')
        self.assertEqual(skirmish_research.display_name('TECHNIQUE_IMPROVED_LEATHER'), 'Improved Leather')

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Techniques.lqr').exists(), 'owner install not available')
    def test_original_forge_and_outpost_research(self):
        data = skirmish_research.research_data(Resolver())
        self.assertEqual(sorted(data['techniques']), ['TECHNIQUE_ETCHED_BLADES', 'TECHNIQUE_IMPROVED_LEATHER',
                                                      'TECHNIQUE_IRON_BLADES', 'TECHNIQUE_IRON_PLATING'])
        blades, plating = data['techniques']['TECHNIQUE_IRON_BLADES'], data['techniques']['TECHNIQUE_IRON_PLATING']
        # Captured Iron Weapons / Iron Armor tooltips: 75 food, 75 ore at the Forge (local skirmish-017, -018).
        self.assertEqual((blades['building'], blades['slot'], blades['food_cost'], blades['ore_cost'], blades['duration_ticks']),
                         ('BUILDING_G_SKILLS_STRUCTURE', 0, 75, 75, 1800))
        self.assertEqual((plating['slot'], plating['food_cost'], plating['ore_cost']), (1, 75, 75))
        # The four unit types the captured tooltip names, plus the Rohan Archer outside this roster.
        self.assertEqual(sorted(blades['bonuses']), ['UNIT_G_DWARVEN_AXETHROWER', 'UNIT_G_DWARVEN_SHIELDBREAKER', 'UNIT_G_GONDOR_SWORDSMAN',
                                                     'UNIT_G_ROHAN_ARCHER', 'UNIT_G_ROHAN_RIDER'])
        self.assertEqual(blades['bonuses']['UNIT_G_GONDOR_SWORDSMAN'], {'damage_milli': 1000, 'armor_milli': 0, 'source': 'TechniqueUnitModifiers'})
        self.assertEqual(plating['bonuses']['UNIT_G_ROHAN_RIDER'], {'damage_milli': 0, 'armor_milli': 1000, 'source': 'TechniqueUnitModifiers'})
        self.assertEqual(data['unavailable']['TECHNIQUE_STEEL_BLADES'], 'requires BUILDING_G_TOWN_CENTER_LEVEL_3')
        self.assertEqual(data['unavailable']['TECHNIQUE_SHIELD_BREAK'], 'grants an ability')
        self.assertEqual(data['unavailable']['TECHNIQUE_DWARVEN_ENGINEERING'], 'affects buildings')


if __name__ == '__main__':
    unittest.main()
