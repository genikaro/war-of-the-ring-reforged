"""Source research rules at the Forge and Wilderness Outpost."""
import unittest

import skirmish_research
from resolve_model import Resolver
import wor


class ResearchImportTests(unittest.TestCase):
    def test_names_follow_the_english_manual(self):
        self.assertEqual(skirmish_research.display_name('TECHNIQUE_IRON_BLADES'), 'Iron Blades')
        self.assertEqual(skirmish_research.display_name('TECHNIQUE_IMPROVED_LEATHER'), 'Improved Leather')
        self.assertEqual(skirmish_research.display_name('TECHNIQUE_STRENGTH_OF_THE_CLAW'), 'Strength of the Claw')
        self.assertEqual(skirmish_research.display_name('TECHNIQUE_NATURES_SHIELD'), "Nature's Shield")

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Techniques.lqr').exists(), 'owner install not available')
    def test_original_forge_and_outpost_research(self):
        data = skirmish_research.research_data(Resolver())
        # Build-58: every stat chain, Dwarven Engineering, Eagle Fletching and the two axes; 22 of the 32 Good techniques.
        self.assertEqual(len(data['techniques']), 22)
        self.assertEqual(len(data['techniques'])+len(data['unsimulated']), 32)
        self.assertEqual(sorted(data['unsimulated']), ['TECHNIQUE_BEAR_FORM', 'TECHNIQUE_CAMOUFLAGE', 'TECHNIQUE_EAGLE_EYE', 'TECHNIQUE_ELVEN_CLOAKS',
                         'TECHNIQUE_LIGHT_OF_LOTHLORIEN', 'TECHNIQUE_LOOKOUT_TOWER', 'TECHNIQUE_NEGATE_MAGIC', 'TECHNIQUE_SAVAGE_BLOW',
                         'TECHNIQUE_SHIELD_BREAK', 'TECHNIQUE_TREEFORM'])
        steel, claw = data['techniques']['TECHNIQUE_STEEL_BLADES'], data['techniques']['TECHNIQUE_STRENGTH_OF_NATURE']
        self.assertEqual((steel['food_cost'], steel['ore_cost'], steel['previous'], steel['prerequisite']),
                         (125, 125, 'TECHNIQUE_IRON_BLADES', 'BUILDING_G_TOWN_CENTER_LEVEL_3'))
        # The nature chain runs 26 -> 28 -> 27 by its previous links, not by record order.
        self.assertEqual((claw['previous'], data['techniques']['TECHNIQUE_STRENGTH_OF_THE_WILD']['previous']),
                         ('TECHNIQUE_STRENGTH_OF_THE_CLAW', 'TECHNIQUE_STRENGTH_OF_NATURE'))
        self.assertEqual(data['techniques']['TECHNIQUE_EAGLE_FLETCHING']['bonuses']['UNIT_G_ELVEN_ARCHER']['range_ppm'], 1_200_000)
        self.assertEqual(data['techniques']['TECHNIQUE_FLAMING_AXE']['bonuses']['UNIT_G_DWARVEN_AXETHROWER']['missile_dps_milli'], 10350)
        self.assertEqual(data['techniques']['TECHNIQUE_DWARVEN_ENGINEERING']['building_health_ppm'], 1_200_000)
        self.assertEqual(data['unsimulated']['TECHNIQUE_ELVEN_CLOAKS']['reason'], 'effect not in the source tables')
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
        self.assertEqual(data['unavailable']['TECHNIQUE_SHIELD_BREAK'], 'grants an ability')
        self.assertEqual(data['unavailable']['TECHNIQUE_LOOKOUT_TOWER'], 'changes sight; there is no fog of war yet')


if __name__ == '__main__':
    unittest.main()
