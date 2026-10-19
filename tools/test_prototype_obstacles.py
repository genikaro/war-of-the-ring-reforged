"""Synthetic footprint rotation, clearance and separate-floor regressions."""
import unittest

import prototype_obstacles as obstacles
import prototype_navigation as nav
from test_prototype_navigation import rectangle


class PrototypeObstacleTests(unittest.TestCase):
    def box(self, center=(0, 0, 0), extent=(1, 2, 3), quaternion=(1, 0, 0, 0)):
        return {'joint': -1, 'flag': 0, 'vectors': [*extent, 1, *center, 1, *quaternion]}

    def placement(self):
        return {'name': 'box', 'object_id': 7, 'position': [10, 20, 30, 1],
                'scale': [1, 1, 1, 1], 'angles_degrees': [0, 0, 90]}

    def test_box_scale_rotation_and_translation(self):
        result = obstacles.footprint(self.box(), self.placement(), [2, 1, 1])
        self.assertEqual(result['y_min'], 27); self.assertEqual(result['y_max'], 33)
        self.assertAlmostEqual(min(p[0] for p in result['polygon']), 8)
        self.assertAlmostEqual(max(p[1] for p in result['polygon']), 22)

    def test_box_quaternion_precedes_placement_rotation(self):
        p = self.placement(); p['angles_degrees'] = [0, 0, 0]
        result = obstacles.footprint(self.box(quaternion=(2**-.5, 0, 0, 2**-.5)), p)
        self.assertAlmostEqual(min(v[0] for v in result['polygon']), 8)
        self.assertAlmostEqual(max(v[1] for v in result['polygon']), 21)

    def test_unknown_or_animated_boxes_are_rejected(self):
        for field, value in [('joint', 0), ('flag', 1)]:
            box = self.box(); box[field] = value
            with self.assertRaises(ValueError): obstacles.footprint(box, self.placement())
        with self.assertRaises(ValueError):
            obstacles.footprint(self.box(quaternion=(0, 0, 0, 0)), self.placement())

    def test_sat_uses_rotated_polygon_not_its_axis_aligned_bounds(self):
        diamond = [(0, 1), (1, 0), (0, -1), (-1, 0)]
        self.assertFalse(obstacles.intersects(diamond, .8, .9, .8, .9))
        self.assertTrue(obstacles.intersects(diamond, -.1, .1, -.1, .1))

    def test_body_clearance_keeps_underpass_and_upper_floor(self):
        item = {'polygon': [(0, 0), (1, 0), (1, 1), (0, 1)], 'y_min': 3, 'y_max': 5}
        self.assertFalse(obstacles.blocked([.5, .5, 0], [.5, .5, 0], [item], 1))
        self.assertTrue(obstacles.blocked([.5, .5, 2], [.5, .5, 2], [item], 1))
        self.assertFalse(obstacles.blocked([.5, .5, 5], [.5, .5, 5], [item], 1))

    def test_ground_mask_inflates_obstacle_without_mutating_source(self):
        item = {'name': 'wall', 'object_id': 1, 'polygon': [(2, 0), (3, 0), (3, 1), (2, 1)], 'y_min': 0, 'y_max': 2}
        source = bytes([1]*6)
        allowed, report = obstacles.ground_mask(6, 1, 1, source, [0]*14, [item])
        self.assertEqual(list(allowed), [1, 0, 0, 0, 1, 1])
        self.assertEqual(report['ground_cells_blocked'], 3)
        self.assertEqual(source, bytes([1]*6))

    def test_obstacle_filters_only_the_intersecting_mesh_floor(self):
        item = {'polygon': [(0, 0), (1, 0), (1, 1), (0, 1)], 'y_min': 3, 'y_max': 5}
        surfaces = [{'name': str(y), 'triangles': rectangle(0, 1, 0, 1, y, y)} for y in [0, 2, 6]]
        result = nav.build(1, 1, 1, [1], [0]*4, surfaces, [item])
        self.assertEqual([n['height_mm'] for n in result['surface_nodes']], [0, 6000])

    def test_swept_height_cannot_pass_through_a_box_between_levels(self):
        item = {'polygon': [(0, 0), (1, 0), (1, 1), (0, 1)], 'y_min': 3, 'y_max': 5}
        self.assertTrue(obstacles.blocked([.5, .5, 0], [.5, .5, 6], [item], 1))


if __name__ == '__main__':
    unittest.main()
