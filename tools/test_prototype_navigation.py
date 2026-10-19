"""Layered navigation geometry tests without original game assets."""
import unittest

import prototype_navigation as nav


def rectangle(x0, x1, z0, z1, y0=0, y1=0):
    a, b, c, d = [x0, z0, y0], [x1, z0, y1], [x0, z1, y0], [x1, z1, y1]
    return [[a, b, c], [b, d, c]]


class PrototypeNavigationTests(unittest.TestCase):
    def test_placement_applies_scale_rotation_translation(self):
        obj = {'position': [5, 6, 7, 1], 'scale': [2, 3, 4, 1], 'angles_degrees': [0, 0, 90]}
        point = nav.transform([1, 2, 3], obj, [2, 1, .5])
        for a, b in zip(point, [-1, 10, 13]):
            self.assertAlmostEqual(a, b)

    def test_segment_crosses_shared_triangle_edge(self):
        self.assertTrue(nav.covers_segment([.1, .5, .1], [1.9, .5, 1.9], rectangle(0, 2, 0, 1, 0, 2)))

    def test_segment_cannot_cross_a_tiny_gap(self):
        triangles = rectangle(0, .499, 0, 1) + rectangle(.501, 1, 0, 1)
        self.assertFalse(nav.covers_segment([0, .5, 0], [1, .5, 0], triangles))

    def test_projected_coverage_cannot_join_different_heights(self):
        triangles = rectangle(0, 1, 0, 1, 4, 4)
        self.assertFalse(nav.covers_segment([0, .5, 0], [1, .5, 0], triangles))
        self.assertIsNone(nav.height_at(2, .5, triangles[0]))

    def test_bridge_has_only_height_matched_terrain_entrances(self):
        result = nav.build(4, 1, 1, [1, 1, 1, 1], [0]*10,
                           [{'name': 'ramp', 'triangles': rectangle(0, 4, 0, 1, 0, 2)}])
        nodes = result['surface_nodes']
        self.assertEqual([n['height_mm'] for n in nodes], [250, 750, 1250, 1750])
        self.assertEqual([n['cell'] for n in nodes], [0, 1, 2, 3])
        self.assertEqual([n for n in nodes[0]['links'] if n < 4], [0])
        self.assertTrue(all(all(link >= 4 for link in n['links']) for n in nodes[1:]))
        self.assertIn(5, nodes[0]['links']); self.assertIn(4, nodes[1]['links'])

    def test_stacked_decks_remain_separate(self):
        result = nav.build(2, 1, 1, [1, 1], [0]*6,
                           [{'name': str(y), 'triangles': rectangle(0, 2, 0, 1, y, y)} for y in [2, 6]])
        nodes = result['surface_nodes']
        self.assertEqual(len(nodes), 4)
        for node in nodes:
            self.assertTrue(all(n >= 2 and nodes[n-2]['height_mm'] == node['height_mm'] for n in node['links']))

    def test_raster_graph_rejects_gap_between_cell_centers(self):
        result = nav.build(2, 1, 1, [0, 0], [0]*6,
                           [{'name': 'gap', 'triangles': rectangle(0, .9, 0, 1) + rectangle(1.1, 2, 0, 1)}])
        self.assertEqual([n['links'] for n in result['surface_nodes']], [[], []])


if __name__ == '__main__':
    unittest.main()
