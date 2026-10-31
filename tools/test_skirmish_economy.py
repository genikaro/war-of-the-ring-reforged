"""Source-backed raw ore and worker capacity import; no rendering required."""
import unittest
from unittest import mock

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
                  'Objects': FakeTable({'OBJECT_RESOURCE_ORE_PILE': {'ResourceGatherRateRaw': .6, 'MaxGatherPercentageRaw': .25,
                                             'ResourceGatherRateWithBuilding': 1.2, 'MaxGatherPercentageWithBuilding': .8, 'MinBuildingPlacementRadius': 6.0},
                                        'OBJECT_RESOURCE_FOOD_WELL': {'ResourceGatherRateRaw': 0, 'MaxGatherPercentageRaw': 0,
                                             'ResourceGatherRateWithBuilding': .4, 'MaxGatherPercentageWithBuilding': 1, 'MinBuildingPlacementRadius': 7.5}})}
        with mock.patch.object(skirmish_economy, 'worker_animations', return_value={'mine': 'clips'}):
            data = skirmish_economy.economy_data(FakeResolver(), ('good',))
        self.assertEqual(data['workers']['UNIT_G_HUMAN_WORKER'], {'food_capacity': 13, 'ore_capacity': 7, 'raw_ore_load': 5,
                                                                  'inside_ticks': 40, 'animations': {'mine': 'clips'}})
        self.assertEqual(data['sources']['ore'], {'rate_milli_per_tick': 30, 'raw_fraction_ppm': 250000,
                         'improved_rate_milli_per_tick': 60, 'improved_fraction_ppm': 800000, 'building_clearance_mm': 6000})
        self.assertEqual(data['sources']['food'], {'rate_milli_per_tick': 0, 'raw_fraction_ppm': 0,
                         'improved_rate_milli_per_tick': 20, 'improved_fraction_ppm': 1000000, 'building_clearance_mm': 7500})
        self.assertEqual((data['food_capacity'], data['ore_capacity']), (100, 200))

    def test_gather_clips_round_up_to_ticks_and_hit_on_the_supply_sound(self):
        def state(name, clip, events=()):
            return {'name': name, 'choices': [{'weight': 0, 'clip_name': 'unused', 'event_indices': []},
                                              {'weight': 100, 'clip_name': clip, 'event_indices': list(events)}]}
        names = skirmish_economy.WORKER_STATES
        model = {'events': [{'name': 'show_attachment', 'time': .1}, {'name': 'sound', 'time': .5}],
                 'animation_states': [state(names['mine'], 'swing', [0, 1])]
                 + [state(names[key], key) for key in names if key != 'mine']}
        durations = {'swing': 1.01, **{key: 1.6 for key in names}}
        clips = skirmish_economy.gather_animations(model, durations)
        self.assertEqual(clips['mine'], {'state': names['mine'], 'clip': 'swing', 'duration_ticks': 21, 'hit_tick': 11,
                                         'hit_source_fraction': .5})
        self.assertEqual(clips['ore_put_down'], {'state': names['ore_put_down'], 'clip': 'ore_put_down', 'duration_ticks': 32})
        durations['food_put_down'] = 2
        with self.assertRaises(ValueError):
            skirmish_economy.gather_animations(model, durations)

    @unittest.skipUnless((wor.ROOT / 'TLOTR - War of the Ring/Database/Units.lqr').exists(), 'owner install not available')
    def test_original_worker_and_resource_definitions(self):
        data = skirmish_economy.economy_data(Resolver())
        worker = data['workers']['UNIT_G_HUMAN_WORKER']
        self.assertEqual({k: worker[k] for k in ['food_capacity', 'ore_capacity', 'raw_ore_load', 'inside_ticks']},
                         {'food_capacity': 10, 'ore_capacity': 9, 'raw_ore_load': 5, 'inside_ticks': 40})
        clips = worker['animations']
        self.assertEqual((clips['mine']['clip'], clips['mine']['duration_ticks'], clips['mine']['hit_tick']),
                         ('G_Human_Worker_Resource_Gather', 28, 16))
        self.assertEqual([clips[k]['duration_ticks'] for k in ['ore_pick_up', 'ore_put_down', 'food_put_down']], [51, 33, 33])
        self.assertEqual((clips['ore_walk']['state'], clips['food_walk']['clip']),
                         ('UNIT_ANIM_STATE_RESOURCE_ORE_WALK', 'G_Human_Worker_Resource_Walk'))
        self.assertEqual((clips['build']['state'], clips['build']['clip']), ('UNIT_ANIM_STATE_REPAIR', 'G_Human_Worker_Repair'))
        self.assertEqual(data['sources']['ore'], {'rate_milli_per_tick': 10, 'raw_fraction_ppm': 550000,
                         'improved_rate_milli_per_tick': 20, 'improved_fraction_ppm': 1000000, 'building_clearance_mm': 6000})
        self.assertEqual(data['sources']['food'], {'rate_milli_per_tick': 0, 'raw_fraction_ppm': 0,
                         'improved_rate_milli_per_tick': 20, 'improved_fraction_ppm': 1000000, 'building_clearance_mm': 7000})
        self.assertEqual((data['food_capacity'], data['ore_capacity']), (99999, 99999))


if __name__ == '__main__':
    unittest.main()
