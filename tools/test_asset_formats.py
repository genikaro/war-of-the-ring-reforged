"""Synthetic legacy asset fixtures; run: python -m unittest -v test_asset_formats."""
import math
from pathlib import Path
import struct
import tempfile
import unittest

import lms
import lms2gltf
import lsk
import resolve_model


def pack(fmt, *values):
    return struct.pack('<' + fmt, *values)


def string(value):
    raw = (value + '\0').encode('utf-16-le')
    return b'\0' + pack('I', len(raw) // 2) + raw


def skeleton_bytes(version, children=(1,), parents=(-1, 0, -1), duration=2.0):
    data = b'LIQD0LSK' + pack('f', version)
    if version == 1.0:
        data += pack('4I', 3, len(children), 1, 0) + pack('%dI' % len(children), *children)
    else:
        data += pack('II', 3, 1)
    for vectors in [((1, 2, 3, 1), (0, 0, 1, 1), (-1, 0, 0, 1)),
                    ((1, 2, 3, 1), (1, 2, 4, 1), (-1, 0, 0, 1)),
                    ((1, 0, 0, 0),) * 3]:
        for vector in vectors:
            data += pack('4f', *vector)
    first_child = 0
    for i, name in enumerate(['Root', 'Child', 'Корень']):
        count = parents.count(i)
        data += string(name) + pack('IiII', 32 if i == 0 else 0,
                                    parents[i], count, first_child if count else 0)
        first_child += count
    data += string('Walk') + pack('f', duration)
    data += pack('IBB2f', 2, 1, 1, 0, 1)
    data += pack('8f', 1, 2, 3, 1, 2, 2, 3, 1)
    data += pack('8f', 1, 0, 0, 0, math.sqrt(0.5), 0, 0, math.sqrt(0.5))
    data += pack('IBBf4f', 1, 0, 1, 0.5, 1, 0, 0, 0)
    data += pack('IBB', 0, 0, 0)
    return data


def mesh_bytes(version, textured=True, bad_usage=False):
    channels = [3, 0] if textured else []
    data = b'LIQD0LMS' + pack('fIIII', version, 1, 1, 3, 1)
    data += pack('I', len(channels)) + pack('%dI' % len(channels), *([2] * len(channels)))
    data += string('') + pack('4I', 3, 1, 1, 0)
    if version == 2.0:
        data += pack('%dI' % len(channels), *channels)
    else:
        data += pack('4I', 0, 0, 0, 0)
        for count in channels:
            data += pack('II', count, 0)
    for position in [(0, 0, 0, 1), (1, 0, 0, 1), (0, 1, 0, 1)]:
        data += pack('4f', *position)
    data += pack('4fi', 0, 0, 1, 1, -1)
    if textured:
        data += pack('6f', 0, 0, 1, 0, 0, 1)
    data += pack('3H', 0, 1, 2)
    for indices in [(0, 1, 2), (0, 0, 0), (0, 0, 0)] + ([(0, 1, 2)] if textured else []):
        data += pack('3I', *indices)
    for usage in [(2, 1, 1) if bad_usage else (1, 1, 1), (3,), (3,)] + ([(1, 1, 1)] if textured else []):
        data += pack('%dI' % len(usage), *usage) + pack('3I', 0, 1, 2)
    data += string('Triangle') + pack('IIfI', 1, 0, 0, 0)
    if version == 2.0:
        data += pack('4I', 0, 0, 0, 0) + pack('%dI' % len(channels), *([0] * len(channels)))
    if version >= 1.1:
        data += pack('BI', 1, 0x2a2)
    return data


class SkeletonTests(unittest.TestCase):
    def test_versions_decode_the_same_hierarchy_and_animation(self):
        legacy = lsk.parse(skeleton_bytes(1.0))
        self.assertEqual(legacy, lsk.parse(skeleton_bytes(2.0)))
        self.assertEqual([j.parent for j in legacy.joints], [-1, 0, -1])
        self.assertEqual(legacy.joints[1].offset, (0, 0, 1))
        self.assertEqual(legacy.joints[1].bind_position, (1, 2, 4))
        self.assertEqual(legacy.joints[0].flags, 32)
        self.assertEqual(legacy.joint_index('Корень'), 2)
        clip = legacy.clips[0]
        self.assertEqual((clip.name, clip.duration), ('Walk', 2))
        self.assertEqual(clip.tracks[0].times, (0, 1))
        self.assertEqual(clip.tracks[0].positions, [(1, 2, 3), (2, 2, 3)])
        self.assertAlmostEqual(clip.tracks[0].rotations[1][0], math.sqrt(0.5))
        self.assertEqual(clip.tracks[1].positions, [])
        self.assertEqual(clip.tracks[2].times, ())

    def test_legacy_child_lists_must_match_parents(self):
        for children in [(), (2,), (99,), (1, 1)]:
            with self.subTest(children=children), self.assertRaisesRegex(ValueError, 'child index'):
                lsk.parse(skeleton_bytes(1.0, children=children))

    def test_legacy_child_order_can_differ_from_joint_order(self):
        skeleton = lsk.parse(skeleton_bytes(1.0, children=(2, 1), parents=(-1, 0, 0)))
        self.assertEqual([j.parent for j in skeleton.joints], [-1, 0, 0])

    def test_legacy_leaf_only_skeleton_has_no_child_indices(self):
        skeleton = lsk.parse(skeleton_bytes(1.0, children=(), parents=(-1, -1, -1)))
        self.assertEqual([j.parent for j in skeleton.joints], [-1, -1, -1])

    def test_static_zero_duration_clip_exports_one_key_at_zero(self):
        data = b'LIQD0LSK' + pack('f4I', 1.0, 1, 0, 1, 0)
        data += pack('12f', 1, 2, 3, 1, 1, 2, 3, 1, 1, 0, 0, 0)
        data += string('Flag') + pack('IiII', 0, -1, 0, 0)
        data += string('Static') + pack('fIBBf4f', 0, 1, 1, 0, 1, 1, 2, 3, 1)
        skeleton = lsk.parse(data)
        self.assertEqual(skeleton.clips[0].tracks[0].times, (1,))
        exporter = lms2gltf.Exporter()
        exporter.skeleton(skeleton)
        sampler = exporter.animations[0]['samplers'][0]
        accessor = exporter.accessors[sampler['input']]
        self.assertEqual(accessor['count'], 1)
        self.assertEqual((accessor['min'], accessor['max']), ([0], [0]))

    def test_invalid_parent_is_rejected_in_both_versions(self):
        for version in [1.0, 2.0]:
            for parent in [-2, 1, 9]:
                with self.subTest(version=version, parent=parent), self.assertRaisesRegex(ValueError, 'parent'):
                    lsk.parse(skeleton_bytes(version, parents=(-1, parent, -1)))

    def test_trailing_or_truncated_data_is_rejected(self):
        for version in [1.0, 2.0]:
            with self.subTest(version=version):
                with self.assertRaisesRegex(ValueError, 'trailing bytes'):
                    lsk.parse(skeleton_bytes(version) + b'\0')
                with self.assertRaises((ValueError, struct.error)):
                    lsk.parse(skeleton_bytes(version)[:-1])

    def test_joint_names_use_the_parser_for_both_versions(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'skeleton.lsk'
            for version in [1.0, 2.0]:
                path.write_bytes(skeleton_bytes(version))
                self.assertEqual(resolve_model.joint_names(path), ['Root', 'Child', 'Корень'])
            path.write_bytes(skeleton_bytes(1.0, children=(99,)))
            self.assertIsNone(resolve_model.joint_names(path))
            self.assertIsNone(resolve_model.joint_names(Path(root) / 'missing.lsk'))

    def test_legacy_tracks_reach_gltf_with_seconds_and_axis_conversion(self):
        exporter = lms2gltf.Exporter()
        nodes, skin = exporter.skeleton(lsk.parse(skeleton_bytes(1.0)))
        self.assertEqual(len(nodes), 3)
        self.assertEqual(exporter.skins[skin]['joints'], nodes)
        self.assertEqual(exporter.nodes[0]['children'], [1])
        self.assertEqual(exporter.scene, [0, 2])
        clip = exporter.animations[0]
        self.assertEqual(clip['name'], 'Walk')
        self.assertEqual(len(clip['channels']), 3)
        channel = next(c for c in clip['channels'] if c['target'] == {'node': 0, 'path': 'translation'})
        sampler = clip['samplers'][channel['sampler']]
        def floats(accessor, count):
            view = exporter.views[exporter.accessors[accessor]['bufferView']]
            return struct.unpack_from('<%df' % count, exporter.bin, view['byteOffset'])
        self.assertEqual(floats(sampler['input'], 2), (0, 2))
        self.assertEqual(floats(sampler['output'], 6), (-1, 3, 2, -2, 3, 2))


class MeshTests(unittest.TestCase):
    def test_zero_normals_repaired_from_geometry_and_degenerate_fallback(self):
        mesh = lms.parse(mesh_bytes(2.0))
        mesh.normals[0] = (0, 0, 0)
        positions, normals, uvs, triangles, _ = lms2gltf.flatten(mesh)
        self.assertTrue(all(abs(sum(v * v for v in normal) - 1) < 1e-6 for normal in normals))
        a, b, c = [positions[i] for i in triangles[0]]
        edge1 = [b[k] - a[k] for k in range(3)]
        edge2 = [c[k] - a[k] for k in range(3)]
        face = (edge1[1]*edge2[2] - edge1[2]*edge2[1], edge1[2]*edge2[0] - edge1[0]*edge2[2],
                edge1[0]*edge2[1] - edge1[1]*edge2[0])
        self.assertGreater(sum(x*y for x, y in zip(face, normals[0])), 0)
        mesh.positions = [(0, 0, 0)] * len(mesh.positions)
        self.assertEqual(lms2gltf.flatten(mesh)[1], [(0, 1, 0)] * 3)

    def test_all_versions_decode_the_same_geometry(self):
        mesh = lms.parse(mesh_bytes(1.0))
        for version in [1.1, 2.0]:
            self.assertEqual(mesh, lms.parse(mesh_bytes(version)))
        self.assertEqual(mesh.name, 'Triangle')
        self.assertEqual(mesh.positions, [(0, 0, 0), (1, 0, 0), (0, 1, 0)])
        self.assertEqual(mesh.uvs, [(0, 0), (1, 0), (0, 1)])
        self.assertEqual(mesh.triangles, [(0, 1, 2)])
        self.assertEqual(mesh.pos_index, [0, 1, 2])
        self.assertTrue(mesh.skinned)

    def test_untextured_meshes_keep_the_default_uv(self):
        for version in [1.0, 1.1, 2.0]:
            with self.subTest(version=version):
                mesh = lms.parse(mesh_bytes(version, textured=False))
                self.assertEqual(mesh.uvs, [(0, 0)])
                self.assertEqual(mesh.uv_index, [0, 0, 0])

    def test_legacy_usage_validation_is_preserved(self):
        with self.assertRaisesRegex(ValueError, 'usage table'):
            lms.parse(mesh_bytes(1.0, bad_usage=True))

    def test_unknown_versions_are_rejected(self):
        for parser, data in [(lsk.parse, skeleton_bytes(1.0)), (lms.parse, mesh_bytes(1.0))]:
            with self.assertRaisesRegex(ValueError, 'unsupported'):
                parser(data[:8] + pack('f', 3.0) + data[12:])


if __name__ == '__main__':
    unittest.main()
