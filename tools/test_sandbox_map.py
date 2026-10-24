"""The Sandbox test map's empty field (build-56), on a synthetic world; runs without original assets."""
import struct
import unittest
from types import SimpleNamespace

import sandbox_map
import skirmish_map


def world(grid=(4, 4), heights=None, colors=None):
    count = grid[0] * grid[1]
    heights = heights or [float(i % 3) for i in range(count)]
    colors = colors or [0xff000000 | (i * 0x010101) for i in range(count)]
    vertices = b''.join(struct.pack('<f4I', h, c, 7, 8, 9) for h, c in zip(heights, colors))
    return SimpleNamespace(metadata={'terrain': {'grid': list(grid), 'height_min': min(heights), 'height_max': max(heights)}},
                           heights=list(heights), vertices=vertices)


def scene():
    objects = [{'object_id': i, 'database_name': name, 'position': [10.0 * i, 5.0, 3.0, 1.0]} for i, name in enumerate(
        ['OBJECT_RESOURCE_FOOD_WELL', 'OBJECT_TREE', 'OBJECT_RESOURCE_ORE_PILE', 'OBJECT_RESOURCE_FOOD_WELL',
         'OBJECT_RESOURCE_ORE_PILE', 'OBJECT_PLACE_OF_POWER_HEALTH', 'OBJECT_RESOURCE_ORE_PILE'])]
    return {'world': world(), 'terrain': {'layers': [{'texture_ids': [5, 5, 7, 9, -1, 9]}]},
            'placements': {'objects': objects, 'other_placeables': [{'effect': 'fire'}]},
            'water': {'lakes': [{'name': 'Lake'}]}, 'tail': {'decals': [{'name': 'mud'}]},
            'pathing': {'categories': [{'name': 'PATHING_CATEGORY_WALKER', 'painted': {'count': 40, 'set_count': 1, 'words': [1, 0]}},
                                       {'name': 'PATHING_CATEGORY_BUILDING', 'painted': {'count': 32, 'set_count': 0, 'words': [0]}},
                                       {'name': 'PATHING_CATEGORY_OTHER', 'painted': {'count': 8, 'set_count': 0, 'words': [0]}}]}}


class Textures:
    files = {5: 'Textures/Grasslands/Binary_DT/Grasslands_ttdt_00.dds', 7: 'Textures\\Grasslands\\Primary\\Grasslands_tttt_02.dds',
             9: 'Textures/Grasslands/Primary/Grasslands_tttt_00.dds'}

    def texture(self, rid):
        return {'file': self.files[rid], 'exists': True}


class FieldTests(unittest.TestCase):
    def test_terrain_is_level_with_one_tint(self):
        source = scene(); sandbox_map.flatten(source, Textures())
        rows = list(struct.iter_unpack('<f4I', source['world'].vertices))
        self.assertEqual({r[0] for r in rows}, {sandbox_map.GROUND})
        self.assertEqual(len({r[1] for r in rows}), 1)
        self.assertEqual({r[2:] for r in rows}, {(7, 8, 9)})
        self.assertEqual(source['world'].heights, [sandbox_map.GROUND] * 16)
        self.assertEqual(source['world'].metadata['terrain']['height_max'], sandbox_map.GROUND)

    def test_ground_scatters_only_plain_grass_tiles(self):
        source = scene(); sandbox_map.flatten(source, Textures())
        ids = source['terrain']['layers'][0]['texture_ids']
        self.assertEqual(len(ids), 6)
        self.assertEqual(set(ids), {7, 9})
        again = scene(); sandbox_map.flatten(again, Textures())
        self.assertEqual(again['terrain']['layers'][0]['texture_ids'], ids)

    def test_commonest_tile_without_plain_grass(self):
        source = scene(); sandbox_map.flatten(source)
        self.assertEqual(set(source['terrain']['layers'][0]['texture_ids']), {5})

    def test_only_one_well_and_ore_pile_per_base_remain(self):
        source = scene(); points = sandbox_map.flatten(source, Textures())
        kept = source['placements']['objects']
        self.assertEqual([o['object_id'] for o in kept], [0, 2, 3, 4])
        by_id = {o['object_id']: o['position'] for o in kept}
        (x0, y0), (x1, y1) = sandbox_map.STARTS
        self.assertEqual(by_id[0], [x0 - 20.0, y0 + 10.0, sandbox_map.GROUND, 1.0])
        self.assertEqual(by_id[3], [x1 + 20.0, y1 - 10.0, sandbox_map.GROUND, 1.0])
        self.assertEqual(by_id[2][:2], [x0 - 20.0, y0 - 10.0])
        self.assertEqual(by_id[4][:2], [x1 + 20.0, y1 + 10.0])
        self.assertEqual(source['placements']['other_placeables'], [])
        self.assertEqual(source['water']['lakes'], [])
        self.assertEqual(source['tail']['decals'], [])
        self.assertEqual([p['name'] for p in points], ['Start Point 0', 'Start Point 1'])
        self.assertTrue(all(p['kind'] == skirmish_map.MARKER_START for p in points))

    def test_walker_and_building_ground_open_everywhere(self):
        source = scene(); sandbox_map.flatten(source, Textures())
        walker, building, other = source['pathing']['categories']
        self.assertEqual(walker['painted'], {'count': 40, 'set_count': 40, 'words': [0xFFFFFFFF, 0xFF]})
        self.assertEqual(building['painted'], {'count': 32, 'set_count': 32, 'words': [0xFFFFFFFF]})
        self.assertEqual(other['painted']['set_count'], 0)

    def test_too_few_sources_are_refused(self):
        source = scene(); source['placements']['objects'] = source['placements']['objects'][:3]
        with self.assertRaises(ValueError):
            sandbox_map.flatten(source, Textures())


if __name__ == '__main__':
    unittest.main()
