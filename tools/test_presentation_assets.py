"""Synthetic presentation assets; no original install or disassembler required."""
import json
from pathlib import Path
import struct
import tempfile
import unittest

import cth
import lfx
import lpo
import presentation_assets as assets
import texture_animation as ta


def p(fmt, *values):
    return struct.pack('<' + fmt, *values)


def uva(version=3):
    return b'LIQDUVAD' + p('fi4I4f11I', version, 7, 2, 4, 125, 0,
                           .25, .5, .125, .25, *([0] * 11)) + (b'\1\0\xcc\xcc' if version >= 2 else b'')


def record(size, values=()):
    raw = bytearray(p('f', 1) + bytes(size - 4))
    for offset, fmt, value in values:
        struct.pack_into('<' + fmt, raw, offset, value)
    return bytes(raw)


def transform():
    return record(56, [(52, 'I', 1)]) + record(144, [(8, 'f', .25)])


def common():
    base = record(148, [(68, 'I', 500)])
    return base + transform()


def emitter():
    return record(96, [(92, 'I', 1)]) + record(140, [(8, 'f', .75)])


def effect():
    header = b'LIQDEFTD' + p('f10I', 1, 1000, 0, 1, 1, 1, 1, 1, 1, 1, 0)
    visual = p('fI3B', 3, 1, 0, 1, 0) + common() + record(52, [(8, 'f', .5)]) + uva()
    trail = p('fI', .01, 1) + common() + record(28, [(8, 'f', .125)])
    beam = p('fI5I2B', .03, 1, *([0] * 7)) + common() + record(64, [(8, 'f', .375)]) + uva(1)
    auxiliary = p('f3I', 1, 0, 0, 1) + common() + record(80, [(8, 'f', .625)])
    particles = p('fI', 1, 0) + emitter() + p('fI', 1, 1) + record(40)
    meshes = p('f', 1) + emitter() + transform()
    return header + visual + trail + beam + auxiliary + particles + meshes


def action(version):
    raw = p('ii', 2, 17)
    if version != 1:
        raw += p('I', 0)
    raw += bytes(20 if version in (1, 2) else 36)
    if version == 6:
        raw += bytes(6)
    return raw + p('iI', 2, 42)


def projectile(version=6):
    resource = p('IIi2fI', 1, 0, 3, 1, 0, 0)
    raw = b'LIQDPROJ' + p('fI', version, 60000)
    raw += p('I', 0) + p('If', 1, 0) + resource + p('I', 0)
    raw += p('i', -1)
    raw += bytes(5) if version >= 5 else b''
    raw += bytes(25) + (bytes(5) if version >= 5 else b'')
    raw += bytes(56) if version == 1 else action(version)
    return raw + p('I', 1) + action(version) + p('I', 0)


def cloth(version=103):
    config = bytearray(112)
    struct.pack_into('<HH', config, 96, 2, 2)
    config = bytes(config) + p('I', 0)
    raw = p('I', version) + (config if version >= 102 else b'') + p('IIBI', 6, 4, 0, 4)
    for i in range(4):
        raw += p('H16ffBI', i, i % 2, i // 2, 0, 1, *([0, 0, 0, 1] * 2), 0, 0, 1, 1, 1, 1, 0xffffffff)
        raw += p('I', 0xcccccccc) if version >= 102 else p('4H', 65535, 65535, 65535, 65535)
    raw += p('IIHH3f', 1, 0, 0, 1, 1, .5, .25)
    raw += config if version == 100 else b''
    return raw + (p('8ii', 4, -1, -1, -1, -1, -1, -1, -1, 1) if version >= 102 else p('ii', 4, 4))


class PresentationTests(unittest.TestCase):
    def test_known_projectile_launch_sound_payload(self):
        asset = {'events':[{'id':7,'type':0,'name_bytes':p('ii',46,172).hex()},
                           {'id':7,'type':0,'name_bytes':p('ii',47,99).hex()},
                           {'id':5,'type':2,'name_bytes':p('i',38).hex()}]}
        self.assertEqual(lpo.launch_sound_events(asset),[172])

    def test_uva_versions_and_legacy_filename(self):
        for version in (1, 2, 3):
            m = ta.parse_uva(uva(version))
            self.assertEqual((m['texture'], m['frame_count'], m['frames_per_row'], m['frame_ms']), (7, 2, 4, 125))
            self.assertEqual(m['rect'], [.25, .5, .125, .25])
        filename = 'atlas.tga\0'.encode('utf-16-le')
        legacy = p('7I6f', 3, 1, 1, 0, 0, 0, 0, 0, .25, .5, .125, .25, 0)
        legacy += b'\0' + p('I', len(filename) // 2) + filename + p('I', 0)
        self.assertEqual(ta.parse_uva(legacy)['texture_path'], 'atlas.tga')

    def test_texture_sequence_wrap_and_seek(self):
        m = ta.parse_lta(b'LIQDTANM' + p('f3I3i', 1, 3, 100, 1, 9, 7, 5))
        self.assertEqual([ta.sequence_texture(m, t) for t in (0, .1, .2, .3, .05, -.1)], [9, 7, 5, 9, 9, 9])

    def test_effect_all_six_arrays_and_embedded_uvas(self):
        m = lfx.parse(effect())
        self.assertEqual(m['duration_ms'], 1000)
        self.assertTrue(m['loop'])
        self.assertEqual(m['texture_tracks'][0]['track_flags'], [0, 1, 0])
        self.assertEqual(m['texture_tracks'][0]['keys'][0]['time'], .5)
        self.assertEqual(m['beam_tracks'][0]['keys'][0]['uva']['texture'], 7)
        self.assertEqual(m['trail_tracks'][0]['keys'][0]['time'], .125)
        self.assertEqual(m['particle_emitters'][0]['keys'][0]['time'], .75)
        self.assertEqual(len(m['mesh_emitters'][0]['transform']['keys']), 1)

    def test_projectile_versions_keep_resource_binding_and_actions(self):
        for version in (1, 2, 5, 6):
            m = lpo.parse(projectile(version))
            self.assertEqual(m['resource_keys'][0]['id'], 3)
            self.assertEqual((m['actions'][0]['id'], m['actions'][0]['shape_parameters']), (17, [42]))

    def test_projectile_spin_rejects_invalid_axes_and_changing_motion(self):
        def bits(value):
            return struct.unpack('<I', struct.pack('<f',value))[0]
        def source(axis, speed):
            return {'motion_keys': [{'time': 0,'values': [[bits(v) for v in [*axis,1]], bits(speed), *([0]*11)]}]}
        self.assertIsNone(lpo.spin_parameters({'motion_keys': []}))
        self.assertIsNone(lpo.spin_parameters(source([1,0,0],0)))
        self.assertEqual(lpo.spin_parameters(source([2,0,0],1080)), {'Axis': [1,0,0],'DegreesPerSecond': 1080})
        for invalid in (source([0,0,0],1080),source([float('nan'),0,0],1080),source([1,0,0],float('inf'))):
            with self.assertRaises(ValueError):
                lpo.spin_parameters(invalid)
        changing = source([1,0,0],1080)
        changing['motion_keys'] *= 2
        with self.assertRaisesRegex(ValueError,'constant'):
            lpo.spin_parameters(changing)

    def test_cloth_versions_topology_and_spring_validation(self):
        for version in (100, 102, 103):
            m = cth.parse(cloth(version))
            positions, uvs, triangles = cth.surface(m)
            self.assertEqual(positions[3], [1, 1, 0])
            self.assertEqual(uvs[3], [1, 1])
            self.assertEqual(triangles, [[0, 1, 2], [1, 3, 2]])
            self.assertEqual((m['springs'][0]['a'], m['springs'][0]['b']), (0, 1))
            self.assertEqual(m['material'], None if version == 100 else 1)

    def test_truncation_and_trailing_bytes_are_errors(self):
        fixtures = [(ta.parse_uva, uva()), (lfx.parse, effect()), (lpo.parse, projectile()), (cth.parse, cloth())]
        for parse, raw in fixtures:
            for truncated in (raw[:8], raw[:20], raw[:-1], raw + b'\0'):
                with self.subTest(parser=parse.__module__, size=len(truncated)), self.assertRaises(ValueError):
                    parse(truncated)

    def test_export_safety_and_per_file_errors(self):
        with tempfile.TemporaryDirectory() as tmp:
            source, out = Path(tmp) / 'source', Path(tmp) / 'out'
            for kind, (suffix, _) in assets.FORMATS.items():
                path = source / kind / ('sample' + suffix)
                path.parent.mkdir(parents=True)
                raw = {'Effects': effect(), 'Projectiles': projectile(), 'Cloths': cloth(), 'UVAs': uva(),
                       'TextureAnimations': b'LIQDTANM' + p('f3Ii', 1, 1, 100, 1, 7)}[kind]
                path.write_bytes(raw)
            with self.assertRaises(ValueError):
                assets.export(source, source / 'overwriting')
            report = assets.export(source, out)
            self.assertEqual(report['errors'], [])
            saved = (out / 'Effects/sample.json').read_bytes()
            assets.export(source, out)
            self.assertEqual((out / 'Effects/sample.json').read_bytes(), saved)
            (source / 'Cloths/sample.cth').write_bytes(b'bad')
            report = assets.export(source, out)
            self.assertEqual(len(report['errors']), 1)
            self.assertEqual(json.loads((out / 'index.json').read_text())['counts']['Effects']['exported'], 1)


if __name__ == '__main__':
    unittest.main()
