"""Source-backed training rules and population capacity; no rendering required."""
import unittest

import skirmish_map
import skirmish_production
from resolve_model import Resolver
import wor


class Record(dict):
    def __init__(self, name, values):
        super().__init__(values)
        self.name = name


class FakeTable:
    def __init__(self, records):
        self.by_key = {}
        for key, (name, values) in records.items():
            self.by_key[key] = self.by_key[name] = Record(name, values)
        self.records = [self.by_key[key] for key in records]

    def get(self, key):
        return self.by_key[key]


class ProductionImportTests(unittest.TestCase):
    def test_producers_keep_source_order_and_split_unavailable_units(self):
        produced = {'UnitProduced%d' % i: -1 for i in range(1, 7)}
        buildings = {i: (name, {'IsUnitProducer': 0, 'AdditionalPopAdded': 0, **produced}) for i, name in enumerate(skirmish_map.TIER1_BUILDINGS)}
        buildings[0] = (skirmish_map.CITADEL, {'IsUnitProducer': 1, 'AdditionalPopAdded': 7, **produced,
                                               'UnitProduced1': 2, 'UnitProduced2': 1, 'UnitProduced3': 3, 'UnitProduced4': 4})
        buildings[99] = ('BUILDING_G_TOWN_CENTER_LEVEL_2', {})
        required = {'BuildingRequired%d' % i: -1 for i in range(1, 5)}

        class FakeResolver:
            db = {'Buildings': FakeTable(buildings),
                  'Units': FakeTable({1: ('UNIT_G_HERO', {**required, 'PowerCost': 0}),
                                      2: ('UNIT_G_WORKER', {**required, 'PowerCost': 0, 'Name': 'Worker', 'FoodCost': 30, 'OreCost': 5,
                                          'PopulationCost': 2, 'UnitTrainingTime': 12001}),
                                      3: ('UNIT_G_CALLED', {**required, 'PowerCost': 3}),
                                      4: ('UNIT_G_LATER', {**required, 'BuildingRequired1': 99, 'PowerCost': 0})}),
                  'WorldVariables': FakeTable({0: ('world', {'DefaultInitialPopulationCapacity': 1, 'MaxPopulationCap': 90})})}
        data = skirmish_production.production_data(FakeResolver(), ['UNIT_G_WORKER', 'UNIT_G_CALLED', 'UNIT_G_LATER'])
        self.assertEqual(data['producers'], {skirmish_map.CITADEL: {'units': ['UNIT_G_WORKER'], 'unavailable': {
            'UNIT_G_HERO': 'model and rules not exported', 'UNIT_G_CALLED': 'requires fate points',
            'UNIT_G_LATER': 'requires BUILDING_G_TOWN_CENTER_LEVEL_2'}}})
        self.assertEqual(data['units']['UNIT_G_WORKER'], {'display_name': 'Worker', 'food_cost': 30, 'ore_cost': 5, 'population': 2,
                                                          'duration_ticks': 241})
        self.assertEqual(data['population_provided'][skirmish_map.CITADEL], 7)
        self.assertEqual((data['initial_population_capacity'], data['max_population']), (1, 90))

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Units.lqr').exists(), 'owner install not available')
    def test_original_citadel_training(self):
        import prepare_prototype
        data = skirmish_production.production_data(Resolver(), prepare_prototype.SKIRMISH_ROSTER)
        citadel = data['producers'][skirmish_map.CITADEL]
        self.assertEqual(citadel['units'], ['UNIT_G_HUMAN_WORKER'])
        self.assertEqual(citadel['unavailable']['UNIT_G_HERO_FRODO'], 'model and rules not exported')
        self.assertEqual(citadel['unavailable']['UNIT_G_HERO_ARAGORN'], 'requires BUILDING_G_TOWN_CENTER_LEVEL_2')
        self.assertEqual(data['producers']['BUILDING_G_GONDORIAN_BARRACKS']['unavailable']['UNIT_G_GONDOR_SWORDSMAN'], 'model and rules not exported')
        self.assertEqual(data['units']['UNIT_G_HUMAN_WORKER'], {'display_name': 'Human Worker', 'food_cost': 35, 'ore_cost': 0,
                         'population': 1, 'duration_ticks': 240})
        self.assertEqual(data['population_provided'][skirmish_map.CITADEL], 10)
        self.assertEqual((data['initial_population_capacity'], data['max_population']), (0, 100))


if __name__ == '__main__':
    unittest.main()
