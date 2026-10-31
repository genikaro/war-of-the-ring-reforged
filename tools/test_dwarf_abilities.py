import unittest
import tempfile
from pathlib import Path

import dwarf_abilities
from resolve_model import Resolver
import wor


@unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Techniques.lqr').exists(), 'owner install not available')
class DwarfAbilityImportTests(unittest.TestCase):
    def test_shield_break_comes_from_its_child_spell(self):
        resolver = Resolver()
        ability, = dwarf_abilities.export(resolver, resolver.db['Units'].get('UNIT_G_DWARVEN_SHIELDBREAKER'))
        self.assertEqual((ability['Kind'], ability['Slot'], ability['Passive'], ability['ArmorReductionMilli'],
                          ability['DurationTicks'], ability['MaxStacks']), (12, 3, True, 1000, 400, 5))
        self.assertEqual(ability['RequiredTechnique'], 'TECHNIQUE_SHIELD_BREAK')
        self.assertEqual(ability['Icon'], 'BattleGear_Shield_Break_Borderless')

    def test_axes_keep_separate_research_locks_and_source_weapons(self):
        resolver = Resolver()
        edge, fire = dwarf_abilities.export(resolver, resolver.db['Units'].get('UNIT_G_DWARVEN_AXETHROWER'))
        self.assertEqual((edge['Kind'], edge['Slot'], edge['ChainTargets'], edge['RangeMm'], edge['BonusScale']),
                         (13, 2, 1, 5000, 700000))
        self.assertEqual((fire['Kind'], fire['Slot'], fire['FirePpmPerSecond'], fire['ChainTargets']), (14, 3, 350000, 1))
        self.assertEqual((edge['RequiredTechnique'], fire['RequiredTechnique']),
                         ('TECHNIQUE_RAZORED_EDGE', 'TECHNIQUE_FLAMING_AXE'))
        self.assertEqual(fire['Source']['Weapon']['AssociatedProjectile'], 33)

    def test_building_fire_uses_original_animated_atlas_frames(self):
        from healing_visual import export_sprite_effect
        traced = []
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            effect = export_sprite_effect(output, Resolver(), traced.append, 'EFFECT_BUILDING_FIRE_TORCH', 'building-fire')
            layers = {layer['Name']: layer for layer in effect['Layers']}
            self.assertEqual(effect['Source']['Id'], 8)
            self.assertEqual(len(layers['MidFire']['Frames']), 8)
            self.assertEqual(len(layers['Smoke']['Frames']), 4)
            self.assertTrue(all((output / frame).is_file() for layer in layers.values() for frame in layer['Frames']))
            self.assertTrue(traced)

    def test_flame_axe_reuses_mesh_and_adds_original_torch_effect(self):
        import combat_rules
        resolver = Resolver()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            normal = combat_rules.projectile_presentation(output,resolver,'UNIT_G_DWARVEN_AXETHROWER',lambda _: None,17)
            flame = combat_rules.projectile_presentation(output,resolver,'UNIT_G_DWARVEN_AXETHROWER',lambda _: None,33,include_sprite_effects=True)
            def model(projectile):
                return next(k['id'] for k in projectile['Source']['Data']['resource_keys'] if k['kind'] == 0)
            self.assertEqual(model(normal), model(flame))
            self.assertEqual(model(flame), 1319)
            self.assertEqual(normal['SpriteEffects'], [])
            self.assertEqual(normal['Spin'], {'AxisPpm': [1000000, 0, 0], 'MilliDegreesPerSecond': 1080000})
            self.assertEqual(flame['Spin'], normal['Spin'])
            torch, = flame['SpriteEffects']
            self.assertEqual((torch['Source']['Id'], torch['Source']['Name']), (252, 'EFFECT_GRISHNAKH_TORCH'))
            self.assertEqual({l['Name'] for l in torch['Layers']}, {'MidFire', 'MovingFlames', 'Flame01', 'Smoke', 'Embers'})
            self.assertTrue(all((output / f).is_file() for layer in torch['Layers'] for f in layer['Frames']))

    def test_both_hands_and_flame_variants_retain_source_spin(self):
        import lpo
        resolver = Resolver()
        for projectile_id in (17, 18, 32, 33):
            record = resolver.db['Projectiles'].get(projectile_id)
            projectile = lpo.parse(Path(resolver.index.find('Projectiles',record['Filename'])).read_bytes())
            spin = lpo.spin_parameters(projectile)
            self.assertEqual(spin['DegreesPerSecond'], 1080)
            axis = (-1,0,0) if projectile_id in (17,33) else (0,0,1)
            for expected, actual in zip(axis, spin['Axis']):
                self.assertAlmostEqual(expected,actual,places=5)
