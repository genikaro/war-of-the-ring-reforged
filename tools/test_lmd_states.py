"""Synthetic model/state fixtures; no original game files or disassembler needed."""
import contextlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from types import SimpleNamespace

import lmd
import lmd_states
import resolve_model


def pack(fmt, *values):
    return struct.pack('<' + fmt, *values)


def string(value):
    raw = (value + '\0').encode('utf-16-le')
    return b'\0' + pack('I', len(raw) // 2) + raw


def visibility(size, indices):
    words = [0] * ((size + 31) // 32)
    for index in indices:
        words[index // 32] |= 1 << (index % 32)
    return pack('II', len(indices), size) + pack('%dI' % len(words), *words)


def property_key(kind, shown):
    flags = (1, 0, 0) if kind in ('model', 'effect') else (1, 0)
    data = pack('I', 1) + bytes(flags) + pack('IB', 1, shown)
    if kind in ('model', 'effect'):
        data += pack('IB', 1, 0)
    count, params = (5, (7, -1)) if kind == 'effect' else (11, (7, -1, -1, -1, -1))
    return data + pack('I', 1) + bytes(count) + pack('%di' % len(params), *params) + pack('f', 1)


def tracks(kind, rows=None):
    if rows is None:
        return b'\0'
    data = b'\1'
    for row in rows:
        data += pack('I', len(row))
        for time, shown in row:
            data += pack('f', time)
            data += pack('IBIB', 1, 0, 1, shown) if kind == 'auxiliary' else property_key(kind, shown)
    return data


def choice(version=2, clip=0, weight=10000, materials=(0, -1), visible=(0,), keys=False):
    events, secondary = ([9], [3]) if keys else ([], [])
    data = pack('5Ii', version, weight, len(materials), len(events), len(secondary), clip)
    data += pack('%di' % len(materials), *materials)
    data += pack('%dI' % len(events), *events) + pack('%dI' % len(secondary), *secondary)
    data += visibility(2, visible) + visibility(1, [0]) + visibility(0, [])
    if version == 1:
        return data
    data += visibility(1 if keys else 0, [0] if keys else [])
    data += tracks('mesh', [[(0, True)]] + [[] for _ in materials[1:]] if keys and materials else None)
    data += tracks('auxiliary', [[(0, True)]] if keys else None)
    data += tracks('model', [[(0, False), (0.25, True), (0.75, False)], []] if keys else None)
    data += tracks('effect', [[(0.5, True)]] if keys else None)
    data += tracks('mesh')  # no cloth slots
    data += pack('I', 1 if keys else 0)
    if keys:
        data += pack('fIIiB', 0.3, 1, 0, 42, 1)
    data += pack('I', 1 if keys else 0)
    if keys:
        data += pack('fIII2B2i2B', 0.4, 1, 1, 2, 1, 0, 5, -1, 2, 3)
    return data


def state(state_id, choices, total=10000, flags=3):
    return pack('5I', 100, state_id, total, len(choices), flags) + b''.join(choices)


def state_block(version=2, materials=True):
    mesh = (0, -1) if materials else ()
    idle = state(35, [choice(version, 0, 70, mesh), choice(version, 1, 30, mesh)], total=100)
    attack = state(2, [choice(version, 2, materials=(-1, 1) if materials else (), visible=(1,), keys=version == 2)])
    lookup = [-1] * 36
    lookup[35], lookup[2] = 0, 1
    return idle + attack + pack('I36i', 36, *lookup)


def reference(record_id):
    return pack('IBII', 0x66, 1, 1, 1) + pack('IiII', 0, record_id, 0x1000000, 0)


def attachment(record_id, effect=False, legacy=False):
    data = pack('IBiIiB3IIi', 0x69 if legacy else 0x6a, 1, record_id, 1, 3, 0, 0, 0, 0, 0x1000000, 0)
    if not legacy:
        data += pack('8f', 1, 0, 0, 0, 0, 0, 0, 1)
    return data + (pack('I', 0x64) if effect else pack('I', 0x65) + bytes(15))


def trailer(events=False):
    """Bounding boxes, then (optionally) one secondary event and two clip events."""
    if not events:
        return pack('II', 0x64, 0)
    data = pack('III', 0x64, 1, 1) + pack('IIiB', 0x65, 1, 4, 0) + pack('12f', *range(12))
    data += pack('IIf', 0x64, 1, 0.6)
    data += pack('IIfIIBB', 1, 6, 0.25, 8, 0, 0, 1) + pack('IIfIIBB', 1, 4, 0.5, 12, 8, 1, 0)
    return data + pack('Ii', 6, 3) + pack('Ifi', 4, 1.0, 7)


def model_bytes(legacy=False, placeholder=False, events=False):
    counts = [2, int(placeholder), 0 if placeholder else 2, 0 if placeholder else 2,
              0 if placeholder else 2, 0, 2, 1, 0, 20 * events, int(events), 2 * events,
              int(placeholder), 0, 0, 105 if placeholder else 0]
    if legacy:
        counts = counts[:13]
    data = b'LIQD0LMD' + pack('fI2B', 1.2 if legacy else 1.7, 1, 1, 0)
    data += pack('%dI' % len(counts), *counts)
    if placeholder:
        data += pack('IffI', 17, 2, 1.1, 105) + pack('105I', *range(105))
        data += string('Full Skeleton') + pack('II', 0, 0)
        data += pack('5f', 1, 0, 0, 0, 1) * 105
    else:
        for record in [11, 12]:
            data += reference(record) + pack('Ii', 0x64, -1)
        for record in [21, 22]:
            data += reference(record) + pack('I', 0x64)
        for record in [31, 32]:
            data += reference(record) + pack('II2B', 0x65, 2, 1, 0)
    data += state_block(1 if legacy else 2, materials=not placeholder)
    data += attachment(501, legacy=legacy) + attachment(502, legacy=legacy)
    return data + attachment(601, effect=True, legacy=legacy) + trailer(events)


class StateTests(unittest.TestCase):
    def test_cloth_collision_shapes_and_groups_do_not_hide_events(self):
        data = bytearray(model_bytes(events=True))
        struct.pack_into('<II', data, 0x12 + 13 * 4, 1, 1)
        # Insert the collision block between bounding boxes and event tables.
        event_table_size = 12 + 2 * 22 + 20
        at = len(data) - event_table_size
        collision = pack('fi', 1, 0) + string('Cloth sphere') + pack('8f2i', 0, 0, 0, 1, 1, 0, 0, 0, 0, -1)
        collision += pack('IfiBI6f', 100, .95, 0, 1, 100, 0, 0, 0, 1, .5, .1)
        group = pack('fII', 1.1, 1, 0) + string('Cloth collision group') + pack('2If', 0, 0, 1)
        data[at:at] = collision + group
        model = lmd.parse(bytes(data))
        self.assertEqual(model.cloth_collisions[0]['name'], 'Cloth sphere')
        self.assertEqual(model.cloth_collision_groups[0]['indices'], [0])
        self.assertEqual([event['name'] for event in model.events], ['footstep', 'melee_hit'])

    def test_sparse_ids_weighted_choices_and_visibility(self):
        model = lmd.parse(model_bytes())
        self.assertEqual([s.id for s in model.animation_states], [35, 2])
        idle = model.animation_state(35)
        self.assertEqual(idle.total_weight, 100)
        self.assertEqual([c.weight for c in idle.choices], [70, 30])
        self.assertEqual([c.clip for c in idle.choices], [0, 1])
        self.assertEqual(idle.choices[0].mesh_selections, [0, -1])
        attack = model.animation_state(2).choices[0]
        self.assertEqual(attack.mesh_selections, [-1, 1])
        self.assertEqual(attack.models.indices, [1])
        self.assertEqual((attack.event_indices, attack.secondary_event_indices), ([9], [3]))
        self.assertIsNone(model.animation_state(3))
        self.assertIsNone(model.animation_state(-1))
        self.assertIsNone(model.animation_state(100))
        self.assertEqual([a.id for a in model.attachments], [501, 502, 601])
        self.assertFalse(model.attachments[1].shown)

    def test_all_property_track_types_and_event_payloads(self):
        attack = lmd.parse(model_bytes()).animation_state(2).choices[0]
        self.assertEqual([(k['time'], k['shown']) for k in attack.model_tracks[0]],
                         [(0, False), (0.25, True), (0.75, False)])
        self.assertEqual(attack.model_tracks[1], [])
        self.assertEqual(attack.effect_tracks[0][0]['time'], 0.5)
        self.assertEqual(attack.mesh_tracks[0][0]['parameters']['values'], [7, -1, -1, -1, -1])
        self.assertTrue(attack.auxiliary_tracks[0][0]['value'])
        self.assertEqual(attack.event_keys[0]['value'], 42)
        self.assertEqual(attack.secondary_keys[0]['values'], [5, -1])
        self.assertEqual(attack.secondary_keys[0]['types'], [2, 3])

    def test_bounding_boxes_and_typed_clip_events(self):
        model = lmd.parse(model_bytes(events=True))
        self.assertEqual(model.bounding_boxes[0]['layer'], 0)
        self.assertEqual(model.bounding_boxes[0]['joint'], 4)
        self.assertEqual(model.secondary_events, [{'value': 1, 'time': 0.6000000238418579}])
        step, hit = model.events
        self.assertEqual((step['name'], step['joint'], step['flags']), ('footstep', 3, [0, 1]))
        self.assertEqual((hit['name'], hit['damage_scale'], hit['joint'], hit['flags']), ('melee_hit', 1.0, 7, [1, 0]))
        self.assertEqual(model.animation_states[1].choices[0].event_indices, [9])
        # A payload must start with its record's type.
        bad = bytearray(model_bytes(events=True))
        bad[-12] = 5
        with self.assertRaises(ValueError):
            lmd.parse(bytes(bad))

    def test_legacy_choice_and_attachment_versions(self):
        model = lmd.parse(model_bytes(legacy=True))
        choice = model.animation_state(2).choices[0]
        self.assertEqual(choice.version, 1)
        self.assertEqual(choice.models.indices, [1])
        self.assertEqual(choice.model_tracks, [])
        self.assertEqual(choice.auxiliary.size, 0)
        self.assertEqual(model.attachments[0].offset, (1, 0, 0, 0, 0, 0, 0, 1))

    def test_joint_data_cannot_be_mistaken_for_state_headers(self):
        model = lmd.parse(model_bytes(placeholder=True))
        self.assertEqual(model.meshes, [])
        self.assertEqual(model.joint_sets[0]['joints'], list(range(105)))
        self.assertEqual(len(model.joint_bounds), 105)
        self.assertEqual(model.animation_state(35).choices[0].mesh_selections, [])
        self.assertEqual(model.attachments[0].id, 501)

    def test_bad_lookup_and_truncation_are_errors(self):
        blob = state_block()
        with self.assertRaisesRegex(ValueError, 'lookup mismatch'):
            lmd_states.read_states(lmd._Reader(blob[:-4] + pack('i', 1), 0), 2)
        for length in [10, 50, len(blob) - 1]:
            with self.subTest(length=length), self.assertRaises(ValueError):
                lmd_states.read_states(lmd._Reader(blob[:length], 0), 2)

    def test_unknown_versions_and_duplicate_state_ids_are_errors(self):
        blob = state_block()
        with self.assertRaisesRegex(ValueError, 'choice version'):
            lmd_states.read_states(lmd._Reader(blob[:20] + pack('I', 3) + blob[24:], 0), 2)
        duplicate = state(0, []) * 2 + pack('Ii', 1, 0)
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            lmd_states.read_states(lmd._Reader(duplicate, 0), 2)

    def test_visibility_spans_words_and_rejects_invalid_bits(self):
        decoded = lmd_states._visibility(lmd._Reader(visibility(35, [0, 31, 34]), 0))
        self.assertEqual((decoded.size, decoded.indices), (35, [0, 31, 34]))
        for data in [pack('3I', 2, 1, 1), pack('3I', 1, 1, 2)]:
            with self.assertRaises(ValueError):
                lmd_states._visibility(lmd._Reader(data, 0))

    def test_empty_raw_weight_path_is_valid(self):
        self.assertEqual(lmd._Reader(b'\0' + pack('I', 0), 0).str(), '')

    def test_json_inspector_includes_decoded_choices(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fixture.lmd'
            path.write_bytes(model_bytes())
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                self.assertEqual(lmd.main(['lmd.py', str(path), '--json']), 0)
            doc = json.loads(output.getvalue())
            self.assertEqual(doc['animation_states'][1]['choices'][0]['models']['indices'], [1])
            self.assertEqual(doc['attachments'][1]['id'], 502)

    def test_resolver_names_states_and_clips_without_replacing_raw_ids(self):
        class Database:
            files = {'unitanimstates': 'unused'}
            def __getitem__(self, name):
                return SimpleNamespace(get=lambda key: SimpleNamespace(name='UNIT_STATE_%d' % key))
        resolver = resolve_model.Resolver.__new__(resolve_model.Resolver)
        resolver.db = Database()
        resolver.clips = lambda _: ['Idle 1', 'Idle 2', 'Attack']
        states = resolver.animation_states(lmd.parse(model_bytes()), 'fixture.lsk')
        self.assertEqual(states[0]['name'], 'UNIT_STATE_35')
        self.assertEqual([c['clip_name'] for c in states[0]['choices']], ['Idle 1', 'Idle 2'])
        self.assertEqual(states[1]['choices'][0]['clip'], 2)
        self.assertEqual(states[1]['choices'][0]['clip_name'], 'Attack')

    def test_resolver_uses_the_database_class_metadata(self):
        class Database:
            files = {'modelclasses': 'unused', 'customstates': 'unused'}
            def __getitem__(self, name):
                if name == 'ModelClasses':
                    return SimpleNamespace(get=lambda _: {'AnimationStateTable': 'CustomStates',
                                                          'DefaultAnimState': 'CUSTOM_IDLE'})
                return SimpleNamespace(get=lambda key: SimpleNamespace(name='CUSTOM_%d' % key))
        resolver = resolve_model.Resolver.__new__(resolve_model.Resolver)
        resolver.db = Database()
        resolver.clips = lambda _: []
        self.assertEqual(resolver.animation_class(1), ('CustomStates', 'CUSTOM_IDLE'))
        states = resolver.animation_states(lmd.parse(model_bytes()), None)
        self.assertEqual(states[0]['name'], 'CUSTOM_35')
        self.assertIsNone(states[0]['choices'][0]['clip_name'])


if __name__ == '__main__':
    unittest.main()
