"""Skirmish start layout decoding; synthetic cases run without original assets."""
import struct
import unittest
from types import SimpleNamespace

import lqr
import prepare_prototype
import skirmish_map
import wor


def marker_block(records, names, declared=None):
    """Flag byte, count, records of 96 bytes, one trailing byte; returns (data, metadata)."""
    body = bytearray()
    for kind, name, position in records:
        record = bytearray(96)
        struct.pack_into('<II', record, 0, kind, names.index(name))
        struct.pack_into('<3f', record, 16, *position)
        body += record
    count = len(records) if declared is None else declared
    block = bytes([1]) + struct.pack('<I', count) + bytes(body) + b'\0'
    data = b'prefix--' + block + b'suffix'
    metadata = {'pre_terrain_records': {'present': True, 'count': len(records), 'record_bytes': 96},
                'sections': [{'name': 'pre_terrain_records', 'offset': 8, 'byte_length': len(block)}]}
    return data, metadata


class MarkerTests(unittest.TestCase):
    names = ['Start Point 1', 'Start Point 0', 'Creature Spawn Area 0']

    def test_markers_decode_positions_and_order_start_points(self):
        data, metadata = marker_block([(1, 'Start Point 1', (160.5, 157.25, 0)), (1, 'Start Point 0', (38.5, 40.0, 1.5)),
                                       (2, 'Creature Spawn Area 0', (49.0, 44.0, 0))], self.names)
        markers = skirmish_map.parse_markers(data, metadata, self.names)
        self.assertEqual(len(markers), 3)
        points = skirmish_map.start_points(markers)
        self.assertEqual([p['name'] for p in points], ['Start Point 0', 'Start Point 1'])
        self.assertEqual(points[0]['position'], [38.5, 40.0, 1.5])

    def test_header_and_record_validation(self):
        data, metadata = marker_block([(1, 'Start Point 0', (1, 2, 3))], self.names, declared=2)
        with self.assertRaisesRegex(ValueError, 'header'):
            skirmish_map.parse_markers(data, metadata, self.names)
        data, metadata = marker_block([(1, 'Start Point 0', (1, 2, 3))], self.names)
        with self.assertRaisesRegex(ValueError, 'invalid marker'):
            skirmish_map.parse_markers(data, metadata, self.names[:0] + ['x'])
        data, metadata = marker_block([(1, 'Start Point 0', (float('nan'), 2, 3))], self.names)
        with self.assertRaisesRegex(ValueError, 'invalid marker'):
            skirmish_map.parse_markers(data, metadata, self.names)
        metadata['pre_terrain_records']['record_bytes'] = 64
        with self.assertRaisesRegex(ValueError, 'layout'):
            skirmish_map.parse_markers(data, metadata, self.names)

    def test_waypoint_and_water_ripple_markers_are_not_start_points(self):
        # MP04_Crosswoods, MP04_Spire_of_Mordor and MP08_Crazy_Corners carry kinds 4 and 5 beside their start points.
        names = self.names + ['Script Waypoint 0', 'Water Ripple Generator 0']
        data, metadata = marker_block([(1, 'Start Point 0', (1, 2, 3)), (4, 'Script Waypoint 0', (4, 5, 6)),
                                       (5, 'Water Ripple Generator 0', (7, 8, 9))], names)
        markers = skirmish_map.parse_markers(data, metadata, names)
        self.assertEqual([m['kind'] for m in markers], [1, 4, 5])
        self.assertEqual([p['name'] for p in skirmish_map.start_points(markers)], ['Start Point 0'])
        data, metadata = marker_block([(3, 'Start Point 0', (1, 2, 3))], names)
        with self.assertRaisesRegex(ValueError, 'invalid marker'):
            skirmish_map.parse_markers(data, metadata, names)

    def test_map_names(self):
        self.assertEqual(skirmish_map.map_file(None), skirmish_map.MAP_FILE)
        self.assertEqual(skirmish_map.map_file('MP04_Wasteland'), 'Multiplayer/MP04_Wasteland.wor')
        self.assertEqual(skirmish_map.map_file('MP06_Wheel of Fate.wor'), 'Multiplayer/MP06_Wheel of Fate.wor')
        for bad in ['../MP04_Wasteland', 'Multiplayer/MP04_Wasteland', 'C01_Mission', 'MP04_Wasteland.lqr', '..']:
            with self.assertRaises(ValueError):
                skirmish_map.map_file(bad)
        self.assertEqual(skirmish_map.map_title('Multiplayer/MP04_Under_the_Canopy.wor'), 'Under the Canopy')
        self.assertEqual(prepare_prototype.skirmish_cache(None), wor.ROOT / 'converted/skirmish')
        self.assertEqual(prepare_prototype.skirmish_cache('MP08_Rift'), wor.ROOT / 'converted/skirmish-maps/MP08_Rift')

    def test_map_without_marker_block_has_no_start_points(self):
        self.assertEqual(skirmish_map.parse_markers(b'', {'sections': [], 'pre_terrain_records': {'present': False}}, []), [])
        with self.assertRaisesRegex(ValueError, 'no usable start'):
            skirmish_map.start_points([])


class ResourceTests(unittest.TestCase):
    def test_each_start_gets_its_nearest_pair(self):
        nodes = [{'id': 1, 'kind': 'ore', 'position': [10, 10, 0]}, {'id': 2, 'kind': 'food', 'position': [14, 8, 0]},
                 {'id': 3, 'kind': 'ore', 'position': [90, 90, 0]}, {'id': 4, 'kind': 'food', 'position': [88, 95, 0]},
                 {'id': 5, 'kind': 'ore', 'position': [50, 50, 0]}, {'id': 6, 'kind': 'food', 'position': [52, 48, 0]}]
        points = [{'position': [12, 12, 0]}, {'position': [92, 92, 0]}]
        self.assertEqual(skirmish_map.assign_resources(nodes, points), [{'ore': 1, 'food': 2}, {'ore': 3, 'food': 4}])
        with self.assertRaisesRegex(ValueError, 'no food'):
            skirmish_map.assign_resources([n for n in nodes if n['kind'] == 'ore'], points)


class SelectableObjectTests(unittest.TestCase):
    def test_source_selectability_excludes_scenery_and_click_through_objects(self):
        records = [lqr.Record(i, name, {'Name': name, 'IsSelectable': selectable, 'IgnoresMouseClicks': ignores,
            'Portrait': -1, 'HasHealth': hp, 'MaxHealth': 300, 'ArmorValue': 2})
            for i, (name, selectable, ignores, hp) in enumerate([
                ('RESOURCE', 1, 0, 0), ('INTERACTABLE', 1, 0, 1), ('SCENERY', 0, 0, 1), ('CLICK_THROUGH', 1, 1, 1)])]
        scene = {'placements': {'objects': [dict(database_table='Objects', reference=i, object_id=i, name=r.name)
            for i, r in enumerate(records)]+[dict(database_table='Buildings', reference=0)]}}
        resolver = SimpleNamespace(db={'Objects': SimpleNamespace(get=lambda i: records[i]), 'UVAs': SimpleNamespace(get=lambda i: None)})
        selection = skirmish_map.selectable_objects(scene, resolver)
        self.assertEqual([p['id'] for p in selection['placements']], [0, 1])
        self.assertEqual(selection['definitions']['RESOURCE']['max_health'], 0)
        self.assertFalse(selection['definitions']['RESOURCE']['has_health'])
        self.assertEqual(selection['definitions']['INTERACTABLE']['max_health'], 300)


class WorkerCellTests(unittest.TestCase):
    def test_cells_are_deterministic_spread_and_in_ring(self):
        width = height = 60
        allowed = bytes([1]) * (width*height)
        centre = (30.0, 30.0)
        cells = skirmish_map.worker_cells(allowed, width, height, 1.0, centre)
        self.assertEqual(cells, skirmish_map.worker_cells(allowed, width, height, 1.0, centre))
        self.assertEqual(len(set(cells)), 4)
        for cell in cells:
            distance = ((cell % width + .5 - 30)**2 + (cell // width + .5 - 30)**2) ** .5
            self.assertTrue(9 <= distance <= 13, distance)
        for a in cells:
            for b in cells:
                if a != b:
                    self.assertGreaterEqual(((a % width-b % width)**2 + (a//width-b//width)**2) ** .5, 2.5)

    def test_blocked_ground_is_skipped_and_exhaustion_is_rejected(self):
        width = height = 60
        allowed = bytearray([1]) * (width*height)
        for cell in range(width*height):
            if cell % width > 30:
                allowed[cell] = 0
        cells = skirmish_map.worker_cells(bytes(allowed), width, height, 1.0, (30.0, 30.0))
        self.assertTrue(all(c % width <= 30 for c in cells))
        with self.assertRaisesRegex(ValueError, 'no free ground'):
            skirmish_map.worker_cells(bytes(width*height), width, height, 1.0, (30.0, 30.0))


class RosterTests(unittest.TestCase):
    def test_skirmish_roster_extends_the_demo_roster_without_reordering(self):
        roster = prepare_prototype.SKIRMISH_ROSTER
        self.assertEqual(roster[:4], prepare_prototype.combat_rules.UNITS)
        self.assertIn('UNIT_G_HUMAN_WORKER', roster[4:])

    def test_skirmish_cache_cannot_target_original_or_extracted_data(self):
        for folder in ['TLOTR - War of the Ring/cache', 'extracted/cache']:
            with self.subTest(folder=folder), self.assertRaisesRegex(ValueError, 'outside'):
                prepare_prototype.prepare_skirmish(wor.ROOT / folder)


@unittest.skipUnless((wor.ROOT / 'extracted/Levels' / skirmish_map.MAP_FILE).is_file(), 'owner install not extracted')
class BilbosBluffsTests(unittest.TestCase):
    def test_all_nine_interactables_keep_original_portraits_without_resource_health(self):
        import world_scene
        from resolve_model import Resolver
        resolver = Resolver()
        scene = world_scene.parse((wor.ROOT / 'extracted/Levels' / skirmish_map.MAP_FILE).read_bytes())
        world_scene.resolve_objects(scene, resolver.db)
        selection = skirmish_map.selectable_objects(scene, resolver)
        self.assertEqual([p['id'] for p in selection['placements']], [2, 3, 4, 5, 6, 7, 8, 9, 521])
        definitions = selection['definitions']
        self.assertEqual(definitions['OBJECT_RESOURCE_ORE_PILE']['portrait'], 'Object_Ore_Pile_Border')
        self.assertEqual(definitions['OBJECT_RESOURCE_FOOD_WELL']['portrait'], 'Object_Well_Border')
        self.assertFalse(any(d['has_health'] for d in definitions.values()))

    def test_start_points_and_resources_match_the_captured_map(self):
        import world_scene
        data = (wor.ROOT / 'extracted/Levels' / skirmish_map.MAP_FILE).read_bytes()
        scene = world_scene.parse(data)
        from resolve_model import Resolver
        world_scene.resolve_objects(scene, Resolver().db)
        metadata = scene['world'].metadata
        points = skirmish_map.start_points(skirmish_map.parse_markers(data, metadata, metadata['name_table']['names']))
        self.assertEqual([p['name'] for p in points], ['Start Point 0', 'Start Point 1'])
        self.assertAlmostEqual(points[0]['position'][0], 38.3, delta=.1)
        self.assertAlmostEqual(points[1]['position'][1], 157.3, delta=.1)
        nodes = skirmish_map.resource_nodes(scene)
        self.assertEqual(sorted(n['kind'] for n in nodes), ['food']*4 + ['ore']*4)
        self.assertEqual({n['initial'] for n in nodes}, {4500})
        near = skirmish_map.assign_resources(nodes, points)
        self.assertEqual(len({i for pair in near for i in pair.values()}), 4)


if __name__ == '__main__':
    unittest.main()
