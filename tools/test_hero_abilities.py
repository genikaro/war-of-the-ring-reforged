"""Spell linkage traversal fails closed rather than hanging or truncating."""
import unittest
from types import SimpleNamespace
from hero_abilities import linked, export


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


if __name__ == '__main__':
    unittest.main()
