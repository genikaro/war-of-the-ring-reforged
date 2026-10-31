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
        buildings = {i: (name, {'IsUnitProducer': 0, 'AdditionalPopAdded': 0, 'IsTownCenter': 0, **produced}) for i, name in enumerate(skirmish_map.SKIRMISH_BUILDINGS)}
        buildings[0] = (skirmish_map.CITADEL, {'IsUnitProducer': 1, 'AdditionalPopAdded': 7, 'IsTownCenter': 1, **produced,
                                               'UnitProduced1': 2, 'UnitProduced2': 1, 'UnitProduced3': 3, 'UnitProduced4': 4})
        buildings[99] = ('BUILDING_G_ELSEWHERE', {})
        required = {'BuildingRequired%d' % i: -1 for i in range(1, 5)}

        class FakeResolver:
            db = {'Buildings': FakeTable(buildings),
                  'Units': FakeTable({1: ('UNIT_G_HERO', {**required, 'PowerCost': 0}),
                                      2: ('UNIT_G_WORKER', {**required, 'PowerCost': 0, 'Name': 'Worker', 'FoodCost': 30, 'OreCost': 5,
                                          'PopulationCost': 2, 'UnitTrainingTime': 12001, 'IsUnique': 0, 'UniqueGroup': -1}),
                                      3: ('UNIT_G_CALLED', {**required, 'PowerCost': 3, 'Name': 'Called', 'FoodCost': 0, 'OreCost': 0,
                                          'PopulationCost': 0, 'UnitTrainingTime': 36000, 'IsUnique': 1, 'UniqueGroup': 0}),
                                      4: ('UNIT_G_LATER', {**required, 'BuildingRequired1': 99, 'PowerCost': 0})}),
                  'WorldVariables': FakeTable({0: ('world', {'DefaultInitialPopulationCapacity': 1, 'MaxPopulationCap': 90, 'MaxPowerPoints': 20,
                      'PowerLevelPerPoint': 500, 'PowerByDamageRate': 0.7999999523162842, 'PowerReductionScalar': 1.0999999046325684,
                      'MaxPowerDistanceMultiplier': 1.4499999284744263, 'PowerAcquisitionRateLow': 0.75, 'PowerAcquisitionRateMedium': 1.0,
                      'PowerAcquisitionRateHigh': 1.3499999046325684, 'WarpostPowerAcquisitionRate': 0.0001})})}
        for name, values in FakeResolver.db['Buildings'].by_key.items():
            values.setdefault('UniqueGroupBuildingAffects', -1); values.setdefault('AmountBuildingAffectsUniqueGroup', 0)
        data = skirmish_production.production_data(FakeResolver(), ['UNIT_G_WORKER', 'UNIT_G_CALLED', 'UNIT_G_LATER'])
        # Build-58: a Fate call is trainable, with its source PowerCost and hero group.
        self.assertEqual(data['producers'], {skirmish_map.CITADEL: {'units': ['UNIT_G_WORKER', 'UNIT_G_CALLED'], 'unavailable': {
            'UNIT_G_HERO': 'model and rules not exported', 'UNIT_G_LATER': 'requires BUILDING_G_ELSEWHERE'}}})
        self.assertEqual(data['units']['UNIT_G_WORKER'], {'display_name': 'Worker', 'food_cost': 30, 'ore_cost': 5, 'fate_cost': 0, 'population': 2,
                                                          'duration_ticks': 241, 'unique': False, 'unique_group': -1, 'prerequisites': []})
        self.assertEqual((data['units']['UNIT_G_CALLED']['fate_cost'], data['units']['UNIT_G_CALLED']['unique_group'], data['max_fate']), (3, 0, 20))
        self.assertEqual(data['population_provided'][skirmish_map.CITADEL], 7)
        self.assertEqual((data['initial_population_capacity'], data['max_population']), (1, 90))
        # Build-76: the source floats become millionths; only buildings flagged IsTownCenter centre the distance bonus.
        fate = data['fate']
        self.assertEqual((fate['max_points'], fate['level_per_point'], fate['by_damage_ppm'], fate['reduction_ppm'], fate['max_distance_ppm']),
                         (20, 500, 800_000, 1_100_000, 1_450_000))
        self.assertEqual(fate['acquisition_ppm'], {'low': 750_000, 'medium': 1_000_000, 'high': 1_350_000})
        self.assertEqual(fate['town_centers'], [skirmish_map.CITADEL])

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Units.lqr').exists(), 'owner install not available')
    def test_original_citadel_training(self):
        import prepare_prototype
        data = skirmish_production.production_data(Resolver(), prepare_prototype.SKIRMISH_ROSTER, ('good',))
        citadel = data['producers'][skirmish_map.CITADEL]
        # Build-58: the four Fate heroes follow the Worker and Frodo in the source UnitProduced order.
        self.assertEqual(citadel['units'], ['UNIT_G_HUMAN_WORKER', 'UNIT_G_HERO_FRODO', 'UNIT_G_HERO_GIMLI', 'UNIT_G_HERO_ARAGORN',
                                            'UNIT_G_HERO_LEGOLAS', 'UNIT_G_HERO_GANDALF'])
        self.assertEqual(citadel['unavailable'], {})
        self.assertEqual(data['units']['UNIT_G_HERO_FRODO'], {'display_name': 'Frodo', 'food_cost': 150, 'ore_cost': 100, 'fate_cost': 0,
                         'population': 0, 'duration_ticks': 720, 'unique': True, 'unique_group': -1, 'prerequisites': ['BUILDING_G_TOWN_CENTER_LEVEL_1']})
        # Compendium: Gimli, Aragorn and Legolas cost 3 Fate at Citadel 2, Gandalf 4 at Citadel 5; the first three share group 0.
        heroes = {name: (data['units'][name]['fate_cost'], data['units'][name]['unique_group'], data['units'][name]['prerequisites'])
                  for name in ['UNIT_G_HERO_GIMLI', 'UNIT_G_HERO_ARAGORN', 'UNIT_G_HERO_LEGOLAS', 'UNIT_G_HERO_GANDALF']}
        self.assertEqual(heroes, {'UNIT_G_HERO_GIMLI': (3, 0, ['BUILDING_G_TOWN_CENTER_LEVEL_2']), 'UNIT_G_HERO_ARAGORN': (3, 0, ['BUILDING_G_TOWN_CENTER_LEVEL_2']),
                                  'UNIT_G_HERO_LEGOLAS': (3, 0, ['BUILDING_G_TOWN_CENTER_LEVEL_2']), 'UNIT_G_HERO_GANDALF': (4, -1, ['BUILDING_G_TOWN_CENTER_LEVEL_5'])})
        self.assertEqual(data['unique_group_capacity'], {'BUILDING_G_TOWN_CENTER_LEVEL_%d' % level: {'group': 0, 'amount': amount}
                                                         for level, amount in [(2, 1), (3, 2), (4, 3), (5, 3)]})
        self.assertEqual(data['max_fate'], 20)
        # Build-76: Rings.exe reads these WorldVariables when battle earns Fate; Citadels 1-5 are the Good town centres.
        self.assertEqual({key: value for key, value in data['fate'].items() if key != 'adapter'}, {
            'max_points': 20, 'level_per_point': 500, 'by_damage_ppm': 800_000, 'reduction_ppm': 1_100_000, 'max_distance_ppm': 1_450_000, 'warpost_ppm': 100,
            'acquisition_ppm': {'low': 750_000, 'medium': 1_000_000, 'high': 1_350_000},
            'town_centers': ['BUILDING_G_TOWN_CENTER_LEVEL_%d' % level for level in range(1, 6)]})
        # Tier 3-4 recruitment (atlas): Archer 80/75/3, Lightbearer 100/85/3 at Citadel 4, Beorning 100/70/3, Huorn 235/200/5 at Citadel 4.
        self.assertEqual(data['producers']['BUILDING_G_ELF_BARRACKS']['units'], ['UNIT_G_ELVEN_ARCHER', 'UNIT_G_ELVEN_LIGHTBRINGER'])
        self.assertEqual(data['producers']['BUILDING_G_STABLES']['units'], ['UNIT_G_BEORNING', 'UNIT_G_HUORN'])
        costs = {name: (data['units'][name]['food_cost'], data['units'][name]['ore_cost'], data['units'][name]['population'], data['units'][name]['prerequisites'])
                 for name in ['UNIT_G_ELVEN_ARCHER', 'UNIT_G_ELVEN_LIGHTBRINGER', 'UNIT_G_BEORNING', 'UNIT_G_HUORN']}
        self.assertEqual(costs, {'UNIT_G_ELVEN_ARCHER': (80, 75, 3, []), 'UNIT_G_ELVEN_LIGHTBRINGER': (100, 85, 3, ['BUILDING_G_TOWN_CENTER_LEVEL_4']),
                                 'UNIT_G_BEORNING': (100, 70, 3, []), 'UNIT_G_HUORN': (235, 200, 5, ['BUILDING_G_TOWN_CENTER_LEVEL_4'])})
        self.assertEqual(data['producers']['BUILDING_G_TOWN_CENTER_LEVEL_2']['units'], citadel['units'])
        barracks = data['producers']['BUILDING_G_GONDORIAN_BARRACKS']
        self.assertEqual((barracks['units'], barracks['unavailable']), (['UNIT_G_GONDOR_SWORDSMAN', 'UNIT_G_ROHAN_RIDER'], {}))
        # Tier 2 recruitment captured on October 22: Rider 85/30/3 (skirmish-007), Shieldbreaker 90/65/3 (-021), Ranger 50/40/2 (-025).
        self.assertEqual(data['units']['UNIT_G_ROHAN_RIDER'], {'display_name': 'Rohan Rider', 'food_cost': 85, 'ore_cost': 30, 'fate_cost': 0,
                         'population': 3, 'duration_ticks': 480, 'unique': False, 'unique_group': -1, 'prerequisites': ['BUILDING_G_TOWN_CENTER_LEVEL_2']})
        self.assertEqual(data['units']['UNIT_G_DWARVEN_SHIELDBREAKER']['prerequisites'], ['BUILDING_G_SKILLS_STRUCTURE'])
        self.assertEqual((data['units']['UNIT_G_DWARVEN_SHIELDBREAKER']['food_cost'], data['units']['UNIT_G_DWARVEN_SHIELDBREAKER']['ore_cost']), (90, 65))
        self.assertEqual(data['producers']['BUILDING_G_RANGER_BARRACKS']['units'], ['UNIT_G_RANGER'])
        self.assertEqual((data['units']['UNIT_G_RANGER']['food_cost'], data['units']['UNIT_G_RANGER']['ore_cost'], data['units']['UNIT_G_RANGER']['population']), (50, 40, 2))
        self.assertEqual(data['units']['UNIT_G_GONDOR_SWORDSMAN'], {'display_name': 'Gondor Swordsman', 'food_cost': 50, 'ore_cost': 0, 'fate_cost': 0,
                         'population': 2, 'duration_ticks': 280, 'unique': False, 'unique_group': -1, 'prerequisites': []})
        hall = data['producers']['BUILDING_G_DWARF_BARRACKS']
        self.assertEqual((hall['units'], hall['unavailable']), (['UNIT_G_DWARVEN_AXETHROWER', 'UNIT_G_DWARVEN_SHIELDBREAKER'], {}))
        self.assertEqual(data['units']['UNIT_G_DWARVEN_AXETHROWER'], {'display_name': 'Dwarf Axethrower', 'food_cost': 60, 'ore_cost': 10, 'fate_cost': 0,
                         'population': 2, 'duration_ticks': 340, 'unique': False, 'unique_group': -1, 'prerequisites': []})
        self.assertEqual(data['units']['UNIT_G_HUMAN_WORKER'], {'display_name': 'Human Worker', 'food_cost': 35, 'ore_cost': 0, 'fate_cost': 0,
                         'population': 1, 'duration_ticks': 240, 'unique': False, 'unique_group': -1, 'prerequisites': []})
        self.assertEqual(data['population_provided'][skirmish_map.CITADEL], 10)
        # Captured: a Camp adds 11 and its War Camp upgrade 15 (local skirmish-015, 21 to 25 after the upgrade).
        self.assertEqual((data['population_provided']['BUILDING_G_WORKER_STRUCTURE'], data['population_provided']['BUILDING_G_POPULATION_STRUCTURE_UPGRADE']), (11, 15))
        self.assertEqual((data['initial_population_capacity'], data['max_population']), (0, 100))


if __name__ == '__main__':
    unittest.main()
