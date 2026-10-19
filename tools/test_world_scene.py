"""Synthetic map sections, corruption checks and glTF instance regressions."""
from pathlib import Path
import tempfile
import unittest
import zlib

import lqr
import lms
import map_decals
from lms2gltf import Exporter
import mpl
import terrain_set
from test_wor import pack, string, table, world_bytes
from test_asset_formats import mesh_bytes
import wor
import wor2gltf
import world_scene as ws


def fixture():
    prefix = world_bytes(); meta = wor.parse(prefix).metadata
    s = meta['sections'][4]; prefix = prefix[:s['offset'] + s['byte_length']]
    terrain = pack('B2I', 0, 1, 2) + pack('3I2B2i', 102, 1, 1, 0, 1, 7, 7)
    terrain += pack('6I', 1, 101, 1, 1, 2, 0) + pack('8fB', 0, 0, 1, 0, 0, 1, 1, 1, 0)
    terrain += pack('3I', 101, 1, 1) + b'DGDGGD'
    terrain += pack('5I', 100, 1, 1, 0xffffffff, 0xffffffff)
    terrain += bytes(16) + pack('2I', 0, 0) + bytes(94)
    data = prefix + terrain + table(['Oak 0', 'WP_Start', 'CWP_Intro', 'CP_Flyover', 'Берёза 1'])
    obj = pack('4I8f3f7I2BIB2I', 0, 2, 4, 2, 1, 2, 3, 1, 1, 2, 3, 1,
               0, 0, 90, 0, 0, 100, 0xffffffff, 1, 0, 0, 0, 1, 42, 0, 7, 8)
    data += b'\0\1' + pack('I', 1) + obj + b'\0\0'
    data += pack('BIBI', 1, 1, 1, 0) + string('PATHING_CATEGORY_WALKER') + pack('2f', 1, 30)
    data += pack('B5I', 1, 2, 1, 1, 2, 1) + pack('B3I', 1, 2, 2, 3) + b'\0\0\0'
    data += pack('BII', 1, 1, 3) + string('Pond') + pack('f6IB11IB', 2, *([0] * 6), 1, *([0] * 11), 1)
    data += pack('I4f4I4f', 101, 1, 1, 1, 1, 1, 1, 2, 2, 0, 0, 2, 1)
    vertices = b''.join(pack('f4I', 0, 0x803060a0, 0xff000000, f, 0) for f in [0, 1, 1, 1])
    data += pack('3I', len(vertices), len(vertices), zlib.crc32(vertices)) + vertices
    data += pack('4I', 0, 0, 0, 0) + pack('IBI', 100, 0, 0) + pack('I', 0) + bytes(40)
    data += b'LIQDMATL' + pack('f', 1) + bytes(0x204) + pack('I', 3)
    # Empty tail arrays; 2x2 half-resolution grid for the 3x2 vertex map.
    tail = bytes(2) + bytes(4 * 4) + bytes(33) + b'\0' + bytes(33)
    tail += b'\0' + pack('BI', 1, 1)
    tail += pack('2I8f5I3B', 0, 1, 1, 2, 3, 1, 4, 5, 6, 1, 11, 0, 0, 0, 0, 0, 0, 0)
    tail += pack('IB4I', 0, 0, 0, 0, 0, 0)
    return data + tail


class WorldSceneTests(unittest.TestCase):
    def test_map_sections_and_painted_water_are_sequential(self):
        scene = ws.parse(fixture())
        self.assertEqual(scene['terrain']['layers'][0]['texture_ids'], [7, 7])
        self.assertEqual(scene['terrain']['compositions']['values'], list(b'DGDGGD'))
        obj = scene['placements']['objects'][0]
        self.assertEqual((obj['name'], obj['model_variant'], obj['owner']), ('Oak 0', 2, 0))
        self.assertEqual((obj['position'], obj['angles_degrees'], obj['pairs']), ([1, 2, 3, 1], [0, 0, 90], [[7, 8]]))
        category = scene['pathing']['categories'][0]
        self.assertEqual(category['painted'], {'count': 2, 'set_count': 1, 'words': [1]})
        self.assertEqual(category['max_water_depth'], 1)
        lake = scene['water']['lakes'][0]
        self.assertEqual((lake['name'], lake['surface']['grid']), ('Pond', [2, 2]))
        self.assertTrue(scene['sections'][-1]['decoded'])
        self.assertEqual(scene['sections'][-1]['offset'] + scene['sections'][-1]['byte_length'], len(fixture()))
        self.assertEqual(scene['tail']['decals'][0]['decal_id'], 1)
        self.assertEqual(scene['tail']['decals'][0]['object_id'], 0)
        for a, b in zip(scene['sections'][1:], scene['sections'][2:]):
            self.assertEqual(a['offset'] + a['byte_length'], b['offset'])

    def test_saved_model_event_variant_resolves_the_selected_resource(self):
        scene = ws.parse(fixture())
        record = lambda rid, name, values: lqr.Record(rid, name, values)
        db = {'Objects': {4: record(4, 'OBJECT_OAK', {'ModelEvent': 3})},
              'ModelEvents': {3: record(3, 'EVENT_OAK', {'AssociatedModelResource1': 9, 'AssociatedModelResource2': 10})},
              'Models': {9: record(9, 'WRONG_VARIANT', {}), 10: record(10, 'SELECTED_VARIANT', {})},
              'MeshPathLookups': {}}
        self.assertEqual(ws.resolve_objects(scene, db), {'SELECTED_VARIANT': 1})

    def test_bounds_versions_and_bitset_population_fail_closed(self):
        for payload in [pack('I', 0xffffffff), b'\1\2']:
            with self.assertRaises(ValueError):
                ws.array(wor.Reader(payload), 100)
        with self.assertRaisesRegex(ValueError, 'population mismatch'):
            ws.bitset(wor.Reader(pack('3I', 0, 2, 1)))
        with self.assertRaisesRegex(ValueError, 'section version'):
            ws.version(wor.Reader(pack('I', 99)), [100])
        data = fixture()
        surface = data.index(b'LIQDMATL')
        with self.assertRaisesRegex(ValueError, 'truncated|invalid array size'):
            ws.parse(data[:surface + 30])

    def test_surface_blob_checks_crc_before_using_data(self):
        raw = bytes(80)
        with self.assertRaisesRegex(ValueError, 'CRC'):
            ws.blob(wor.Reader(pack('3I', 80, 80, 1) + raw), 80)
        with self.assertRaisesRegex(ValueError, 'byte size'):
            ws.blob(wor.Reader(pack('3I', 80, 80, 0) + raw), 100)

    def test_original_float_reciprocal_rounding_preserves_24_cell_tiles(self):
        self.assertEqual(ws.texture_grid(456, 360, 24, 24), [18, 14])
        self.assertEqual(ws.texture_grid(296, 200, 16, 16), [19, 13])
        self.assertEqual(ws.texture_grid(352, 260, 4, 4), [88, 65])

    def test_water_uses_cell_flag_rather_than_neighbor_vertex_flags(self):
        scene = ws.parse(fixture()); ex = Exporter()
        report = wor2gltf.water_meshes(ex, scene)
        self.assertEqual(report[0]['triangles'], 2)
        scene['water']['lakes'][0]['surface']['vertices'][0][3] = 1
        with self.assertRaisesRegex(ValueError, 'no visible surface'):
            wor2gltf.water_meshes(Exporter(), scene)

    def test_terrain_uvs_rectangular_grid_and_shared_texture(self):
        from PIL import Image
        scene = ws.parse(fixture())
        with tempfile.TemporaryDirectory() as temp:
            texture = Path(temp) / 'tile.png'; Image.new('RGB', (2, 2), 'green').save(texture)
            class Resolver:
                def texture(self, rid):
                    return {'exists': True, 'file': str(texture)}
            ex = Exporter(); report = wor2gltf.terrain_mesh(ex, scene, Resolver())
            self.assertEqual(report, {'vertices': 6, 'triangles': 4, 'tiles': 2, 'textures': 1})
            self.assertEqual(len(ex.meshes[0]['primitives']), 1)
            positions = ex.accessors[0]
            self.assertEqual(positions['min'], [-2, -2, 0])
            self.assertEqual(positions['max'], [0, 12, 1])

    def test_skin_instances_share_meshes_but_have_separate_joint_nodes(self):
        ex = Exporter()
        template = {'mesh_offset': 5, 'accessor_offset': 7, 'source': {
            'scene': 0, 'scenes': [{'nodes': [0, 2]}],
            'nodes': [{'children': [1]}, {'name': 'joint'}, {'mesh': 0, 'skin': 0}],
            'skins': [{'joints': [1], 'inverseBindMatrices': 0}]}}
        for _ in range(2):
            parent = ex.node({'name': 'placement', 'translation': [1, 2, 3]})
            wor2gltf.instance_model(ex, template, parent)
        self.assertEqual([s['joints'] for s in ex.skins], [[2], [6]])
        self.assertEqual([n['mesh'] for n in ex.nodes if 'mesh' in n], [5, 5])
        self.assertIn(3, ex.scene); self.assertIn(7, ex.scene)
        self.assertEqual(ex.nodes[0]['children'], [1])
        self.assertEqual(ex.nodes[4]['children'], [5])

    def test_rotation_uses_the_same_basis_as_model_geometry(self):
        self.assertEqual(wor2gltf.rotation([0, 0, 0]), [0, 0, 0, 1])
        q = wor2gltf.rotation([0, 0, 90])
        self.assertAlmostEqual(q[1], 2 ** -0.5)
        self.assertAlmostEqual(q[3], 2 ** -0.5)

    def test_source_install_and_input_folder_cannot_be_outputs(self):
        with self.assertRaisesRegex(ValueError, 'outside'):
            wor2gltf.export(wor.ROOT / 'extracted/Levels/Map.wor', wor.ROOT / 'extracted/Levels/generated')
        with self.assertRaisesRegex(ValueError, 'outside'):
            wor2gltf.export(wor.ROOT / 'extracted/Levels/Map.wor', wor.ROOT / 'TLOTR - War of the Ring/generated')

    def test_mesh_lookup_matrix_sizes_codes_and_trailing_bytes(self):
        raw = pack('6I', 1, 100, 0, 2, 1, 2) + bytes([0xe4, 0, 0, 0])
        cat = mpl.parse(raw)['categories'][0]
        self.assertEqual((cat['matrix_size'], cat['codes']), (2, [0, 1, 2, 3]))
        for changed in [raw[:-1], raw + b'\0', raw[:20] + pack('I', 1) + raw[24:]]:
            with self.assertRaises(ValueError):
                mpl.parse(changed)

    def test_terrain_palette_maps_painted_character_to_composition(self):
        data = pack('2I', 4, 0) + string('TERRAINSET_TEST') + pack('I', 1)
        data += string('Dirt') + b'D' + string('TERRAIN_COMPOSITION_DIRT') + pack('3I', 0, 0xff000000, 1) + string('Primary')
        pal = terrain_set.parse(data)
        self.assertEqual(pal['palette'][0]['character'], 'D')
        self.assertEqual(pal['folders'], ['Primary'])
        with self.assertRaisesRegex(ValueError, 'trailing'):
            terrain_set.parse(data + b'\0')

    def test_full_mesh_footer_excludes_flagged_triangles_from_navigation(self):
        mesh = lms.parse(mesh_bytes(2.0), read_footer=True)
        self.assertEqual(mesh.triangle_flags, [0x2a2])
        self.assertEqual(mpl.graph(mesh)['excluded_triangles'], 1)
        self.assertEqual(mpl.graph(mesh)['triangles'], [])
        for data in [mesh_bytes(2.0)[:-1], mesh_bytes(2.0) + b'\0']:
            with self.assertRaisesRegex(ValueError, 'truncated|trailing'):
                lms.parse(data, read_footer=True)

    def test_navigation_uses_position_indices_and_destination_major_codes(self):
        mesh = lms.Mesh('', '', False, [(0, 0, 0)] * 4, [], [],
                        [(0, 1, 2), (3, 4, 5)], [0, 1, 2, 2, 1, 3], [], [], [],
                        triangle_flags=[0, 0])
        nav = mpl.graph(mesh)
        self.assertEqual(nav['neighbors'], [[None, 1, None], [0, None, None]])
        cat = {'category': 0, 'matrix_size': 2, 'codes': [3, 0, 1, 3]}
        self.assertEqual(mpl.route(cat, nav, 0, 1), [0, 1])
        self.assertEqual(mpl.route(cat, nav, 1, 0), [1, 0])
        self.assertEqual(mpl.verify({'categories': [cat]}, nav)[0]['reachable_pairs'], 4)
        cat['codes'][2] = 3
        self.assertIsNone(mpl.route(cat, nav, 0, 1))
        cat['codes'][2] = 0
        with self.assertRaisesRegex(ValueError, 'boundary'):
            mpl.verify({'categories': [cat]}, nav)

    def test_navigation_cycles_are_rejected(self):
        cat = {'category': 0, 'matrix_size': 3, 'codes': [3, 3, 3, 3, 3, 3, 0, 0, 3]}
        nav = {'triangles': [[], [], []], 'neighbors': [[1, None, None], [0, None, None], [None] * 3]}
        with self.assertRaisesRegex(ValueError, 'cycle'):
            mpl.verify({'categories': [cat]}, nav)

    def test_decal_clips_to_rectangle_on_sloping_terrain(self):
        world = ws.parse(fixture())['world']
        decal = {'width': 0.6, 'height': 0.4, 'position': [1, 0.5, 0],
                 'angles_degrees': [0, 0, 30], 'color_argb': 0xffffffff,
                 'alpha': 0.5, 'definition_alpha': 0.8, 'blends_with_terrain': True}
        surface = map_decals.project(world, decal)
        self.assertTrue(surface['indices'])
        for uv, rgba, normal in zip(surface['uvs'], surface['colors'], surface['normals']):
            self.assertTrue(all(-1e-7 <= v <= 1 + 1e-7 for v in uv))
            self.assertAlmostEqual(rgba[3], 0.4)
            self.assertAlmostEqual(sum(v*v for v in normal), 1)
        # Every output point lies on one of the source terrain triangles.
        for p in surface['positions']:
            x, y, h = -p[0], p[2], p[1] - 0.015
            ix = int(x); dx = x - ix
            a, b = world.heights[ix:ix + 2]
            c, d = world.heights[3 + ix:3 + ix + 2]
            expected = a + dx*(b-a) + y*(c-a) if dx+y <= 1 else d + (1-dx)*(c-d) + (1-y)*(b-d)
            self.assertAlmostEqual(h, expected)
        decal['position'] = [100, 100, 0]
        self.assertEqual(map_decals.project(world, decal)['indices'], [])

    def test_tail_truncation_and_trailing_bytes_fail(self):
        for data in [fixture()[:-1], fixture() + b'\0']:
            with self.assertRaisesRegex(ValueError, 'truncated|trailing'):
                ws.parse(data)


if __name__ == '__main__':
    unittest.main()
