"""Source-backed raw ore and worker capacity import; no rendering required."""
import unittest

import skirmish_economy
from resolve_model import Resolver
import wor


class FakeTable:
    def __init__(self, records):
        self.by_name = records
        self.records = list(records.values())

    def get(self, key):
        return self.by_name[key]


class EconomyImportTests(unittest.TestCase):
    def test_values_come_from_tables_and_rates_convert_to_integer_ticks(self):
        class FakeResolver:
            db = {'Units': FakeTable({'UNIT_G_HUMAN_WORKER': {'MaxCarriedFood': 13, 'MaxCarriedOre': 7}}),
                  'WorldVariables': FakeTable({'world': {'DefaultInitialFoodCapacity': 100, 'DefaultInitialOreCapacity': 200}}),
                  'Objects': FakeTable({'OBJECT_RESOURCE_ORE_PILE': {'ResourceGatherRateRaw': .6, 'MaxGatherPercentageRaw': .25},
                                        'OBJECT_RESOURCE_FOOD_WELL': {'ResourceGatherRateRaw': 0, 'MaxGatherPercentageRaw': 0}})}
        data = skirmish_economy.economy_data(FakeResolver())
        self.assertEqual(data['workers']['UNIT_G_HUMAN_WORKER'], {'food_capacity': 13, 'ore_capacity': 7})
        self.assertEqual(data['sources']['ore'], {'rate_milli_per_tick': 30, 'raw_fraction_ppm': 250000})
        self.assertEqual(data['sources']['food'], {'rate_milli_per_tick': 0, 'raw_fraction_ppm': 0})
        self.assertEqual((data['food_capacity'], data['ore_capacity']), (100, 200))

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Units.lqr').exists(), 'owner install not available')
    def test_original_worker_and_resource_definitions(self):
        data = skirmish_economy.economy_data(Resolver())
        self.assertEqual(data['workers']['UNIT_G_HUMAN_WORKER'], {'food_capacity': 10, 'ore_capacity': 9})
        self.assertEqual(data['sources']['ore'], {'rate_milli_per_tick': 10, 'raw_fraction_ppm': 550000})
        self.assertEqual(data['sources']['food'], {'rate_milli_per_tick': 0, 'raw_fraction_ppm': 0})
        self.assertEqual((data['food_capacity'], data['ore_capacity']), (99999, 99999))


if __name__ == '__main__':
    unittest.main()
