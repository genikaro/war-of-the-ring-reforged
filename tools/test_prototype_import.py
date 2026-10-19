"""Importer connectivity and source-folder guards without original assets."""
import unittest
import struct

import prepare_prototype
import prototype_assets
import wor


class PrototypeImportTests(unittest.TestCase):
    def test_largest_connected_area_has_no_row_wrap(self):
        self.assertEqual(prepare_prototype.connected(bytes([0, 0, 1, 1, 0, 0]), 3, 2), {2})
        self.assertEqual(prepare_prototype.connected(bytes([1, 1, 0, 0, 1, 0]), 3, 2), {0, 1, 4})

    def test_empty_grid_has_no_component(self):
        self.assertEqual(prepare_prototype.connected(bytes(9), 3, 3), set())

    def test_demo_squads_are_connected_separate_and_repeatable(self):
        width, height, anchor = 80, 70, 35*80+25
        allowed = bytes([1]) * (width*height)
        spawns, groups, camera = prepare_prototype.demo_layout(allowed, width, height, anchor)
        self.assertEqual((len(spawns), len(groups)), (36, 6))
        self.assertEqual([sum(s['group'] == g['id'] for s in spawns) for g in groups], [6]*6)
        self.assertEqual(sum(s['team'] == 0 for s in spawns), 18)
        self.assertEqual(len({s['cell'] for s in spawns}), 36)
        self.assertTrue(allowed[camera])
        for a in spawns:
            for b in spawns:
                if a['group'] == b['group']:
                    continue
                distance = (a['cell'] % width - b['cell'] % width)**2 + (a['cell']//width - b['cell']//width)**2
                self.assertGreaterEqual(distance, (12 if a['team'] != b['team'] else 8)**2)
        self.assertEqual((spawns, groups, camera), prepare_prototype.demo_layout(allowed, width, height, anchor))

    def test_demo_layout_rejects_insufficient_space(self):
        with self.assertRaisesRegex(ValueError, 'no separated'):
            prepare_prototype.demo_layout(bytes([1])*100, 10, 10, 22)

    def test_original_and_extraction_cannot_be_cache_outputs(self):
        for folder in ['TLOTR - War of the Ring/cache', 'extracted/cache']:
            with self.subTest(folder=folder), self.assertRaisesRegex(ValueError, 'outside'):
                prepare_prototype.prepare(wor.ROOT / folder)

    def test_root_motion_removes_horizontal_travel_from_both_roots(self):
        raw = bytearray(); views = []; accessors = []
        def acc(values, width):
            start = len(raw)
            for value in values:
                raw.extend(struct.pack('<%df' % width, *(value if width > 1 else [value])))
            views.append({'byteOffset': start, 'byteLength': len(raw) - start})
            accessors.append({'bufferView': len(views)-1, 'componentType': 5126, 'count': len(values)})
            return len(accessors)-1
        a = acc([0, 1], 1); b = acc([[0, 0, 0], [0, 0, 4]], 3)
        c = acc([0, 0.5, 1], 1); d = acc([[0, 1, 0], [0, 1.1, 2], [0, 1, 4]], 3)
        e = acc([[0, 0.5, 0], [0, 0.6, 0]], 3)
        animation = {'name': 'Unit_Run', 'samplers': [{'input':a, 'output':b}, {'input':c, 'output':d}, {'input':a, 'output':e}],
                     'channels': [{'sampler':i, 'target':{'node':i, 'path':'translation'}} for i in range(3)]}
        gltf = {'nodes':[{'name':'J_Position'}, {'name':'Pelvis', 'children':[2]}, {'name':'Spine'}], 'skins':[{'joints':[0,1,2]}],
                'bufferViews':views, 'accessors':accessors, 'animations':[animation]}
        result, changed = prototype_assets.in_place(gltf, raw)
        self.assertEqual(changed, ['Unit_Run'])
        for index in [b, d]:
            start = views[accessors[index]['bufferView']]['byteOffset']
            for i in range(accessors[index]['count']):
                x, y, z = struct.unpack_from('<3f', result, start + i*12)
                self.assertEqual((x, z), (0, 0))
        middle = views[accessors[d]['bufferView']]['byteOffset'] + 12
        self.assertAlmostEqual(struct.unpack_from('<3f', result, middle)[1], 1.1)
        child = views[accessors[e]['bufferView']]
        self.assertEqual(result[child['byteOffset']:], raw[child['byteOffset']:])
        animation['name'] = 'Unit_Attack'
        self.assertEqual(prototype_assets.in_place(gltf, raw), (bytes(raw), []))


if __name__ == '__main__':
    unittest.main()
