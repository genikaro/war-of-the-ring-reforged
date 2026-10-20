import base64
from types import SimpleNamespace
import unittest

from combat_rules import collision_profile
import prototype_clearance


class UnitCollisionTests(unittest.TestCase):
    def box(self):
        return {'layer':0,'joint':0,'flag':0,'vectors':[.5772516,.5772516,.9152003,1,0,0,.8560292,1,1,0,0,0]}

    def test_main_box_applies_scale_and_rounds_outward(self):
        radius,height,source = collision_profile({'scale':[1.2]*3},SimpleNamespace(joints=[SimpleNamespace(name='J_Position ')]),[self.box()])
        self.assertEqual((radius,height),(693,2197))
        self.assertEqual(source['Layer'],'BBOX_LAYER_MAIN')

    def test_offsets_expand_body_and_other_layers_do_not_supply_it(self):
        box = self.box(); box['vectors'][4] = .3
        radius,_,_ = collision_profile({'scale':[1]*3},SimpleNamespace(joints=[SimpleNamespace(name='J_Position')]),[box])
        self.assertEqual(radius,878)
        box['layer'] = 1
        with self.assertRaisesRegex(ValueError,'one main'):
            collision_profile({'scale':[1]*3},SimpleNamespace(joints=[]),[box])

    def test_unsupported_animated_or_rotated_boxes_fail(self):
        for joint,flag,quat in [('Spine',0,[1,0,0,0]),('J_Position',1,[1,0,0,0]),('J_Position',0,[0,0,0,1])]:
            box=self.box(); box['flag']=flag; box['vectors'][8:]=quat
            with self.assertRaises(ValueError):
                collision_profile({'scale':[1]*3},SimpleNamespace(joints=[SimpleNamespace(name=joint)]),[box])

    def test_mesh_width_and_headroom_are_body_specific(self):
        # Narrow deck centered in a 5x5 map, at 2 m above terrain.
        triangles=[[(2,0,2),(3,0,2),(2,5,2)],[(3,0,2),(3,5,2),(2,5,2)]]
        nav={'width':5,'height':5,'cell_size_mm':1000,'painted_allowed_base64':base64.b64encode(bytes([1]*25)).decode(),
             'surface_nodes':[{'cell':12,'height_mm':2000,'surface':'bridge','links':[]}],
             'picking_surfaces':[{'name':'bridge','triangles':triangles}],'runtime_obstacles':[]}
        profiles=prototype_clearance.build(nav,[0]*36,[
            {'BodyRadiusMm':350,'BodyHeightMm':700},{'BodyRadiusMm':700,'BodyHeightMm':2500}])
        self.assertNotIn(25,profiles[0]['nodes'])
        self.assertNotIn(12,profiles[0]['nodes'])
        self.assertIn(25,profiles[1]['nodes'])
        self.assertIn(12,profiles[1]['nodes'])

    def test_protected_clearance_outputs_rejected(self):
        for folder in ['extracted/cache','TLOTR - War of the Ring/cache']:
            with self.assertRaisesRegex(ValueError,'outside'):
                prototype_clearance.refresh(prototype_clearance.ROOT/folder)


if __name__ == '__main__': unittest.main()
