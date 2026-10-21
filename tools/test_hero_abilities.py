"""Spell linkage traversal fails closed rather than hanging or truncating."""
import unittest
from types import SimpleNamespace
from hero_abilities import linked, export, HEROES
from wor import ROOT


class HeroAbilityTests(unittest.TestCase):
    def test_chain_preserves_action_order(self):
        rows = {1: {'next': 2}, 2: {'next': -1}}
        self.assertEqual(linked(rows, 1, 'next'), [rows[1], rows[2]])
        self.assertEqual(linked(rows, -1, 'next'), [])

    def test_cycle_rejected(self):
        with self.assertRaisesRegex(ValueError, 'cycle'):
            linked({1: {'next': 2}, 2: {'next': 1}}, 1, 'next')

    def test_broken_link_rejected(self):
        with self.assertRaisesRegex(ValueError, 'missing'):
            linked({1: {'next': 7}}, 1, 'next')

    def test_other_units_have_no_invented_hero_abilities(self):
        self.assertEqual(export(None, SimpleNamespace(name='UNIT_G_ELF_KNIGHT'), None, None), [])

    def test_frodo_maps_his_three_source_battle_gear(self):
        self.assertEqual(HEROES['UNIT_G_HERO_FRODO'], {7: 'The One Ring', 12: 'Heroic Aura', 50: 'Sting'})

    @unittest.skipUnless((ROOT / 'TLOTR - War of the Ring/Database/Units.lqr').exists(), 'owner install not available')
    def test_original_frodo_abilities(self):
        import lsk
        from resolve_model import Resolver
        resolver = Resolver(); unit = resolver.db['Units'].get('UNIT_G_HERO_FRODO')
        model = resolver.resolve(unit.name, recurse=False)
        skeleton = lsk.parse((ROOT / model['skeletons'][0]['file']).read_bytes())
        ring, sting, aura = export(resolver, unit, model, skeleton)
        self.assertEqual((ring['Slot'], ring['Kind'], ring['Passive'], ring['DrainMilliPerTick'], ring['AlphaPpm'], ring['SoundEvents']),
                         (0, 3, False, 100, 500000, [98, 99]))
        self.assertEqual((sting['Slot'], sting['Kind'], sting['BonusScale'], sting['BonusTargets']), (1, 4, 1350000, [22, 21, 34, 49]))
        self.assertEqual((aura['Slot'], aura['Kind'], aura['RangeMm'], aura['RechargeScalePpm']), (2, 5, 15000, 1500000))


if __name__ == '__main__':
    unittest.main()
