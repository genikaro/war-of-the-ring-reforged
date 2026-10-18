"""Appearance evaluation and GLB scene regressions without game assets."""
from copy import deepcopy
from dataclasses import asdict
import json
from pathlib import Path
import struct
import tempfile
from types import SimpleNamespace
import unittest

import lmd
import lms2gltf
import lsk
import model_appearance as appearance
from test_asset_formats import mesh_bytes
from test_lmd_states import model_bytes


FLAGS = [SimpleNamespace(id=i, name='MODEL_FLAG_' + name) for i, name in
         [(0, 'WEAPON_BLOOD_RIGHT'), (1, 'WEAPON_BLOOD_LEFT'), (5, 'LEVEL_1'),
          (6, 'LEVEL_2'), (7, 'LEVEL_3'), (18, 'LEVEL_4'), (22, 'CRITICAL_DEATH_BLOOD')]]


def resolved(mesh_file=None):
    model = lmd.parse(model_bytes())
    states = [asdict(s) for s in model.animation_states]
    for state in states:
        state['name'] = 'IDLE' if state['id'] == 35 else 'ATTACK'
        for choice in state['choices']:
            choice['clip_name'] = ['Idle', 'Idle2', 'Attack'][choice['clip']]
    part = {'mesh': {'file': mesh_file, 'exists': bool(mesh_file)}}
    return {'model': 'MODEL_TEST', 'parts': [deepcopy(part), deepcopy(part)],
            'default_animation_state': 'IDLE', 'animation_states': states,
            'attachments': [{'kind': 'model', 'shown': False, 'state_mask': 0,
                             'joint': -1, 'offset': [1, 0, 0, 0, 0, 0, 0, 1],
                             'model': {'model': 'CHILD_%d' % i, 'parts': [deepcopy(part)]}}
                            for i in range(2)]}


def data():
    root = appearance.instance(resolved())
    root['parts'] = [10, 11]
    children = []
    for i in range(2):
        child = appearance.instance({'model': 'Child', 'parts': [{}]}, 0, i)
        child['parts'] = [12 + i]
        children.append(child)
    return {'version': 1, 'flags': appearance.flag_policy(FLAGS),
            'config': {'level': 1, 'blood': False, 'allAttachments': False},
            'models': [root] + children}


class AppearanceTests(unittest.TestCase):
    def test_declared_idle_and_hidden_attachment_override(self):
        d = data()
        self.assertEqual(d['models'][0]['defaultState'], 35)
        self.assertEqual(appearance.default_choice(d['models'][0])['clip'], 'Idle')
        self.assertEqual(appearance.visible_nodes(d), {10: True, 11: False, 12: True, 13: False})

    def test_arrow_keys_backwards_seek_and_loop_reset(self):
        d = data()
        attack = d['models'][0]['states'][1]['choices'][0]
        for time, arrow in [(0, False), (.249, False), (.25, True), (.749, True),
                            (.75, False), (1, False), (0, False), (.5, True), (.1, False)]:
            with self.subTest(time=time):
                self.assertEqual(appearance.visible_nodes(d, attack, time),
                                 {10: True, 11: True, 12: arrow, 13: True})

    def test_parameter_only_keys_do_not_change_visibility(self):
        self.assertEqual(appearance.visibility_tracks([[{'time': 0, 'shown': True, 'flags': [0, 0, 1]},
                                                       {'time': .5, 'shown': False, 'flags': [1, 0, 0]}]]),
                         [[[.5, False]]])

    def test_named_level_and_blood_policy_keeps_untagged_gear(self):
        d = data()
        policy, config = d['flags'], d['config']
        for mask, expected in [(0, True), (1 << 24, True), (1, False), (1 << 22, False),
                               (1 << 5, True), (1 << 6, False), (sum(policy['levels']), True)]:
            self.assertEqual(appearance.configuration_visible(mask, policy, config), expected)
        config.update(level=2, blood=True)
        self.assertTrue(appearance.configuration_visible(1 | (1 << 6), policy, config))
        self.assertFalse(appearance.configuration_visible(1 << 5, policy, config))

    def test_nested_detached_meshes_obey_parent_visibility(self):
        d = data()
        grandchild = appearance.instance({'model': 'Grandchild', 'parts': [{}]}, 2, 0)
        grandchild['parts'] = [14]
        d['models'][2]['attachments'] = [{'shown': True, 'flags': 0}]
        d['models'].append(grandchild)
        self.assertFalse(appearance.visible_nodes(d)[14])
        d['config']['allAttachments'] = True
        self.assertTrue(all(appearance.visible_nodes(d)[node] for node in [12, 13, 14]))

    def test_absent_default_and_no_states_fallback(self):
        res = resolved()
        res['default_animation_state'] = 'MISSING'
        model = appearance.instance(res)
        self.assertEqual(model['defaultState'], 35)
        self.assertEqual(model['declaredDefaultState'], 'MISSING')
        self.assertIsNone(appearance.default_choice(appearance.instance({'parts': []})))

    def test_missing_slots_keep_their_source_indices(self):
        d = data()
        d['models'][0]['parts'][0] = None
        self.assertEqual(appearance.visible_nodes(d), {11: False, 12: True, 13: False})

    def test_static_clips_keep_their_duration_for_visibility_keys(self):
        for track in [lsk.Track(()), lsk.Track((0,), positions=[(1, 2, 3)])]:
            skeleton = lsk.Skeleton([lsk.Joint('Root', -1, (1, 2, 3), (0, 0, 0), (1, 0, 0, 0), 0)],
                                    [lsk.Clip('StaticIdle', 2.5, [track])])
            exporter = lms2gltf.Exporter()
            exporter.skeleton(skeleton)
            animation = exporter.animations[0]
            self.assertEqual(animation['name'], 'StaticIdle')
            sampler = animation['samplers'][0]
            view = exporter.views[exporter.accessors[sampler['input']]['bufferView']]
            self.assertEqual(struct.unpack_from('<2f', exporter.bin, view['byteOffset']), (0, 2.5))
            self.assertEqual(animation['channels'][0]['target'], {'node': 0, 'path': 'translation'})

    def test_glb_default_scene_and_retained_hidden_geometry(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            mesh = path / 'triangle.lms'
            mesh.write_bytes(mesh_bytes(1.0))
            resolver = SimpleNamespace(resolve=lambda key: resolved(str(mesh)), db={'ModelFlags': FLAGS})
            out = path / 'model.glb'
            report = lms2gltf.convert_model(resolver, 'MODEL_TEST', out)
            raw = out.read_bytes()
            magic, version, length, json_size, chunk_type = struct.unpack_from('<5I', raw)
            self.assertEqual((magic, version, length, chunk_type), (0x46546c67, 2, len(raw), 0x4e4f534a))
            gltf = json.loads(raw[20:20 + json_size])
            extra = gltf['extras']['wotrAppearance']
            self.assertEqual((report['meshes'], report['warnings']), (4, []))
            self.assertEqual(len(extra['hiddenNodes']), 2)
            connected = set(gltf['scenes'][0]['nodes'])
            for node in gltf['nodes']:
                connected.update(node.get('children', []))
            for node, shown in appearance.visible_nodes(extra).items():
                self.assertEqual(node in connected, shown)
            # Every retained node and buffer remains available to the viewer.
            self.assertEqual(len(gltf['meshes']), 4)
            self.assertEqual(extra['models'][0]['states'][0]['choices'][1]['weight'], 30)


if __name__ == '__main__':
    unittest.main()
