"""Parser for Liquid Entertainment .lmd models (LIQD0LMD, versions 1.2 to 1.7).

A model links database records: skeleton (Skeletons.lqr), meshes (Meshes.lqr),
weights (Weights.lqr), materials (Materials.lqr), textures (Textures.lqr) and
attached models / effects (Models.lqr, Effects.lqr). Little endian. "str" is
u8 0, u32 char count incl. NUL, UTF-16LE (as in .lqr). Nested objects start
with a u32 class version (0x64..0x6a). Layout as far as decoded:

    char[8]  "LIQD0LMD"
    f32      version (1.2 .. 1.7)
    u32      model class (ModelClasses.lqr: 0 object, 1 unit, 2 building,
             3 ambient creature, 5 static, 6 ambient)
    u8, u8   unknown (1, 0)
    u32[F]   counts, F = 13 (1.2), 14 (1.3), 16 (1.4), 17 (1.5, 1.6), 16 (1.7)
             [0] animation states, [1] skeletons, [2] meshes, [3] materials,
             [4] textures, [6] attached models, [7] attached effects; in
             1.7 animated models the last field is the joint count
    u32[c1]  skeleton record ids
    joint sets: f32 2.0, f32 1.0/1.1, u32 n, u32[n] joint indices;
             inner v1.1 adds str name ("Full Skeleton") and u32[2] flags
    joint bounds: f32 1.0, f32[4], repeated for the header's joint count
             joint set count / joint count: fields [12]/[15] in v1.7,
             [13]/last in v1.4..1.6; neither block exists in v1.2/1.3
    c2 x mesh slot:     ref, u32 state_mask, u32 0, u32 0x64, i32 weight id
                        (str weight path instead if the mesh ref is a path)
    c3 x material slot: ref, u32 state_mask, u32 0, u32 0x64
    c4 x texture slot:  ref, u32 state_mask, u32 0, u32 0x65, u32 n,
                        u8[n] channel flags (one is set: the texture channel,
                        0 diffuse, the team colour mask is usually channel 1)
    c0 animation states: u32 0x64, u32 state id, u32 total weight,
             u32 choice count, u32 flags, weighted choices (lmd_states.py)
    if c0 > 0: u32 n, i32[n] state-id to record-index lookup (-1 absent)
    c6 + c7 attachments (models first, then effects):
             u32 0x6a, u8 has_id, u32 record id (or str path when has_id is 0),
             u32 attach_to_joint, i32 joint index (into the skeleton, -1 none),
             u8 shown (0 = hidden until the game shows it: arrows in hand,
             worker sacks, missiles), u32 x3, u32 state_mask (as for slots:
             blood has an extra bit, weapon upgrade levels one bit each),
             i32, f32[8] offset (quaternion w x y z,
             position x y z, scale), then u32 0x65 + 15 bytes (models)
             or u32 0x64 (effects)
    c8 cloth attachments: as above, tail u32 0x65 + u32 or u32 0x64
    bounding boxes: u32 0x64, u32 n, u32[n] boxes per BBoxes.lqr layer,
             each u32 0x65, u32 attach_to_joint, i32 joint, u8, f32[12]
    cloth simulation records (header [13] in 1.7, [14] in 1.4..1.6) and
             their mesh slots: not decoded; when present, events stay unread
    c10 secondary events: u32 0x64, u32 value, f32 time
    c11 clip events: u32 1, u32 type, f32 time (0..1 of the clip),
             u32 size, u32 offset, u8 gameplay, u8 presentation
    u8[c9]   event payloads; each starts with u32 type (see EVENT_TYPES)
    ...      a final block (reader 0x5ed201), not decoded

    ref:     u32 0x66, u8 has_id, u32 1, u32 n, u32[n] (0 or -1),
             i32 record id, or str file path when has_id is 0

state_mask is a bit set of model configurations (unit upgrade levels,
variations), separate from the decoded animation-state IDs and visibility
masks. Bit names come from ModelFlags.lqr; full gameplay flag operations are
separate from the level/blood preview policy in model_appearance.py.
Joint names come from the skeleton (.lsk), see resolve_model.py.

Usage:
    python lmd.py <model.lmd> [...] [--json]
"""
import argparse
import json
import struct
import sys
from dataclasses import asdict, dataclass, field

from lmd_states import read_states

HEADER_FIELDS = {12: 13, 13: 14, 14: 16, 15: 17, 16: 17, 17: 16}  # version * 10


@dataclass
class Ref:
    id: int = -1          # record id, -1 if none
    path: str = ''        # raw file path when the slot is not in the database
    state_mask: int = 0


@dataclass
class MeshSlot:
    mesh: Ref
    weight: int           # Weights.lqr id, -1 for static meshes
    weight_path: str = ''


@dataclass
class TextureSlot:
    texture: Ref
    channels: list        # set channel indices


@dataclass
class Attachment:
    kind: str             # 'model' or 'effect'
    id: int               # Models.lqr / Effects.lqr id, -1 if given by path
    path: str
    joint: int            # skeleton joint index, -1 if not attached to a joint
    offset: tuple         # quaternion w x y z, position x y z, scale
    shown: bool = True    # visible by default
    state_mask: int = 0


@dataclass
class Model:
    version: float
    model_class: int
    counts: list
    skeletons: list
    meshes: list = field(default_factory=list)
    materials: list = field(default_factory=list)
    textures: list = field(default_factory=list)
    attachments: list = field(default_factory=list)
    warnings: list = field(default_factory=list)
    joint_sets: list = field(default_factory=list)
    joint_bounds: list = field(default_factory=list)
    animation_states: list = field(default_factory=list)
    state_lookup: list = field(default_factory=list)
    cloths: list = field(default_factory=list)            # cloth attachments, header count [8]
    bounding_boxes: list = field(default_factory=list)
    # Clip events (choice event_indices / secondary_event_indices point here).
    # None when an undecoded cloth simulation block precedes them.
    secondary_events: list = field(default_factory=list)
    events: list = field(default_factory=list)

    def common_state_bits(self):
        masks = [s.mesh.state_mask for s in self.meshes] + [s.texture.state_mask for s in self.textures]
        common = ~0
        for m in masks:
            common &= m
        return common if masks else 0

    def animation_state(self, state_id):
        """Decoded gameplay state by its sparse ID, or None when absent."""
        if not 0 <= state_id < len(self.state_lookup):
            return None
        index = self.state_lookup[state_id]
        return self.animation_states[index] if index >= 0 else None

    def states(self):
        """{state bit: (mesh slot indices, texture slot indices)} for the
        non-common state bits; empty when every slot is always used."""
        common = self.common_state_bits()
        bits = 0
        for s in self.meshes:
            bits |= s.mesh.state_mask
        for s in self.textures:
            bits |= s.texture.state_mask
        bits &= ~common
        out = {}
        for b in range(32):
            if bits >> b & 1:
                meshes = [i for i, s in enumerate(self.meshes) if s.mesh.state_mask >> b & 1
                          or not s.mesh.state_mask & ~common]
                texs = [i for i, s in enumerate(self.textures) if s.texture.state_mask >> b & 1
                        or not s.texture.state_mask & ~common]
                out[b] = (meshes, texs)
        return out

    def textures_for_mesh(self, mesh_index):
        """Texture slot indices that can be used with a mesh slot (shared state bits)."""
        common = self.common_state_bits()
        m = self.meshes[mesh_index].mesh.state_mask & ~common
        out = []
        for i, t in enumerate(self.textures):
            tm = t.texture.state_mask & ~common
            if not m or not tm or m & tm:
                out.append(i)
        return out


class _Reader:
    def __init__(self, data, pos):
        self.data = data
        self.pos = pos

    def unpack(self, fmt):
        size = struct.calcsize('<' + fmt)
        if self.pos + size > len(self.data):
            raise ValueError('truncated model data at 0x%x' % self.pos)
        vals = struct.unpack_from('<' + fmt, self.data, self.pos)
        self.pos += size
        return vals[0] if len(vals) == 1 else vals

    def ints(self, n):
        if n > (len(self.data) - self.pos) // 4:
            raise ValueError('integer array exceeds model data at 0x%x' % self.pos)
        vals = list(struct.unpack_from('<%dI' % n, self.data, self.pos))
        self.pos += 4 * n
        return vals

    def signed(self, n):
        return [value if value < 0x80000000 else value - (1 << 32) for value in self.ints(n)]

    def count(self):
        count = self.unpack('I')
        if count > (len(self.data) - self.pos) // 4:
            raise ValueError('record count exceeds model data at 0x%x' % (self.pos - 4))
        return count

    def str(self):
        if self.pos + 5 > len(self.data) or self.data[self.pos] != 0:
            raise ValueError('bad string marker at 0x%x' % self.pos)
        n = struct.unpack_from('<I', self.data, self.pos + 1)[0]
        start = self.pos + 5
        if start + 2 * n > len(self.data):
            raise ValueError('truncated model string at 0x%x' % self.pos)
        self.pos = start + 2 * n
        if n and self.data[self.pos - 2:self.pos] != b'\0\0':
            raise ValueError('unterminated model string at 0x%x' % start)
        return self.data[start:self.pos].decode('utf-16-le').rstrip('\0')

    def expect(self, value, what):
        got = self.unpack('I')
        if got != value:
            raise ValueError('%s: expected 0x%x at 0x%x, got 0x%x' % (what, value, self.pos - 4, got))

    def ref(self):
        self.expect(0x66, 'ref')
        has_id, one, n = self.unpack('BII')
        if one != 1 or n > 4:
            raise ValueError('bad ref at 0x%x' % (self.pos - 9))
        self.ints(n)
        if has_id:
            r = Ref(id=self.unpack('i'))
        else:
            r = Ref(path=self.str())
        r.state_mask, _ = self.unpack('II')
        return r


def _attachment(r, kind):
    version, has_id = r.unpack('IB')
    if not 0x67 <= version <= 0x6a:
        raise ValueError('attachment: bad version 0x%x' % version)
    rid, path = (r.unpack('i'), '') if has_id else (-1, r.str())
    to_joint, joint = r.unpack('Ii')
    shown, _a, _b, _c, state_mask, _d = r.unpack('B4Ii')
    offset = r.unpack('8f') if version >= 0x6a else (1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0)
    tag = r.unpack('I')
    if tag == 0x65:
        r.unpack('I' if kind == 'cloth' else '15B')
    elif tag != 0x64:
        raise ValueError('attachment: unknown tail 0x%x at 0x%x' % (tag, r.pos - 4))
    return Attachment(kind, rid, path, joint if to_joint else -1, offset, bool(shown), state_mask)


# Clip event types identified from their payloads and the states that use
# them: type -> (name, payload fields after the leading type word). Payload
# references: joint = skeleton joint index, attachment = model-attachment index,
# sound = SoundEvents.lqr, speech = SpeechFXEventTypes.lqr, effect = Effects.lqr,
# projectile = Projectiles.lqr. Other types keep their raw payload.
EVENT_TYPES = {
    0: ('show_attachment', ('attachment',)),     # pick up a sack, draw a sword
    1: ('hide_attachment', ('attachment',)),     # put down, throw the rock
    4: ('melee_hit', ('damage_scale', 'joint')),
    6: ('footstep', ('joint',)),
    9: ('sound', ('sound',)),
    13: ('missile_release', ('joint',)),
    26: ('sound', ('sound',)),
    27: ('speech', ('speech',)),                 # voice cue: attack yell, idle
    34: ('projectile', ('joint', 'projectile')),
    44: ('effect_at_joint', ('effect', 'joint')),
    45: ('effect', ('effect',)),
}
_EVENT_FORMATS = {'damage_scale': 'f'}


def _event(kind, payload):
    name, fields = EVENT_TYPES.get(kind, (None, ()))
    fmt = '<' + ''.join(_EVENT_FORMATS.get(f, 'i') for f in fields)
    event = {'type': kind, 'name': name}
    if fields and len(payload) >= struct.calcsize(fmt):
        event.update(zip(fields, struct.unpack_from(fmt, payload)))
        payload = payload[struct.calcsize(fmt):]
    if payload:
        event['raw'] = payload.hex()
    return event


def _bounding_boxes(r):
    """Per BBoxes.lqr layer (main collision, selection, visual, ...): boxes."""
    r.expect(0x64, 'bounding boxes')
    boxes = []
    for layer, count in enumerate(r.ints(r.count())):
        for _ in range(count):
            r.expect(0x65, 'bounding box')
            to_joint, joint, flag = r.unpack('IiB')
            boxes.append({'layer': layer, 'joint': joint if to_joint else -1, 'flag': flag,
                          'vectors': list(r.unpack('12f'))})   # three vec4, meaning not checked
    return boxes


def _trailer(r, m):
    """Blocks after the attachments, in the order of the Rings.exe loader (0x5ec642):
    cloth attachments, bounding boxes (0x5c7db5), cloth simulation records
    (0x5eb75f, header [13] in 1.7, [14] in 1.4..1.6) and their meshes, then the
    clip events: secondary events (0x5c837b), event records (0x5b65e5) and the
    payload blob. Each payload starts with its record's type."""
    c = m.counts
    m.cloths = [_attachment(r, 'cloth') for _ in range(c[8])]
    m.bounding_boxes = _bounding_boxes(r)
    if (c[13] if m.version >= 1.7 else c[14] if m.version >= 1.4 else 0):
        m.secondary_events = m.events = None     # type-dispatched records, not decoded yet
        return
    m.secondary_events = []
    for _ in range(c[10]):
        r.expect(0x64, 'secondary event')
        value, time = r.unpack('If')
        m.secondary_events.append({'value': value, 'time': time})
    records = [r.unpack('IIfIIBB') for _ in range(c[11])]
    if c[9] > len(r.data) - r.pos:
        raise ValueError('event payload exceeds model data')
    blob = r.data[r.pos:r.pos + c[9]]
    r.pos += c[9]
    m.events = []
    for version, kind, time, size, offset, sim, fx in records:
        payload = blob[offset:offset + size]
        if version != 1 or size < 4 or len(payload) != size or struct.unpack_from('<I', payload)[0] != kind:
            raise ValueError('bad clip event record (type %d at payload 0x%x)' % (kind, offset))
        # sim/fx: the two flag bytes; gameplay events (hits, releases) set the
        # first, presentation (footsteps, sounds, voice) the second.
        m.events.append({'time': time, 'flags': [sim, fx], **_event(kind, payload[4:])})


def parse(data):
    if data[:8] != b'LIQD0LMD':
        raise ValueError('not a LIQD0LMD file')
    r = _Reader(data, 8)
    version, model_class = r.unpack('fI')
    nf = HEADER_FIELDS.get(round(version * 10))
    if nf is None:
        raise ValueError('unsupported lmd version %.2f' % version)
    r = _Reader(data, 0x12)
    counts = r.ints(nf)
    skeletons = r.ints(counts[1])
    m = Model(round(version, 2), model_class, counts, skeletons)

    # Header positions changed when the unused legacy field was removed in 1.7.
    joint_sets = counts[12] if m.version >= 1.7 else counts[13] if m.version >= 1.4 else 0
    for _ in range(joint_sets):
        outer, inner = r.unpack('ff')
        if outer != 2.0 or round(inner, 1) not in (1.0, 1.1):
            raise ValueError('unsupported joint set version %s/%s' % (outer, inner))
        joints = r.ints(r.count())
        name, flags = (r.str(), r.unpack('II')) if inner > 1.05 else ('', (0, 0))
        m.joint_sets.append({'joints': joints, 'name': name, 'flags': flags})
    for _ in range(counts[-1] if m.version >= 1.4 else 0):
        if r.unpack('f') != 1.0:
            raise ValueError('unsupported joint bounds version')
        m.joint_bounds.append(r.unpack('4f'))

    nmesh, nmat, ntex = counts[2:5]
    if nmesh + nmat + ntex:
        for _ in range(nmesh):
            ref = r.ref()
            r.expect(0x64, 'mesh slot')
            if ref.id >= 0 or not ref.path:
                m.meshes.append(MeshSlot(ref, r.unpack('i')))
            else:                                 # mesh by path: weights by path too
                m.meshes.append(MeshSlot(ref, -1, r.str()))
        for _ in range(nmat):
            ref = r.ref()
            r.expect(0x64, 'material slot')
            m.materials.append(ref)
        for _ in range(ntex):
            ref = r.ref()
            r.expect(0x65, 'texture slot')
            n = r.unpack('I')
            if n > len(data) - r.pos:
                raise ValueError('texture channel flags exceed model data')
            flags = data[r.pos:r.pos + n]
            r.pos += n
            m.textures.append(TextureSlot(ref, [i for i, b in enumerate(flags) if b]))

    m.animation_states, m.state_lookup = read_states(r, counts[0])
    # The state lookup ends exactly where attachments begin; no pattern search.
    want = counts[6] + counts[7] if nf > 7 else 0
    if want:
        for i in range(want):
            item = _attachment(r, 'model' if i < counts[6] else 'effect')
            if not -1 <= item.joint < 1024:
                raise ValueError('attachment joint index out of range')
            m.attachments.append(item)
    _trailer(r, m)
    return m


def load(path):
    with open(path, 'rb') as f:
        return parse(f.read())


def _ref(ref):
    return ('0x%x' % ref.id) if ref.id >= 0 else (ref.path or '-')


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    parser = argparse.ArgumentParser(description='Inspect model resources and decoded animation states.')
    parser.add_argument('paths', nargs='+', metavar='MODEL.lmd')
    parser.add_argument('--json', action='store_true', help='include all state choices and property keys as JSON')
    args = parser.parse_args(argv[1:])
    if args.json:
        rows = [{'file': path, **asdict(load(path))} for path in args.paths]
        print(json.dumps(rows[0] if len(rows) == 1 else rows, ensure_ascii=False, allow_nan=False, indent=2))
        return 0
    for path in args.paths:
        m = load(path)
        print('%s: v%.1f class %d, skeletons %s, counts %s' % (
            path, m.version, m.model_class, ['0x%x' % s for s in m.skeletons], m.counts))
        for i, s in enumerate(m.meshes):
            print('  mesh %d: %s weight %s states 0x%x textures %s' % (
                i, _ref(s.mesh), ('0x%x' % s.weight) if s.weight >= 0 else '-',
                s.mesh.state_mask, m.textures_for_mesh(i)))
        for i, s in enumerate(m.materials):
            print('  material %d: %s' % (i, _ref(s)))
        for i, s in enumerate(m.textures):
            print('  texture %d: %s channel %s states 0x%x' % (
                i, _ref(s.texture), s.channels, s.texture.state_mask))
        for a in m.attachments:
            print('  attached %s %s joint %d' % (a.kind, ('0x%x' % a.id) if a.id >= 0 else a.path, a.joint))
        for state in m.animation_states:
            print('  animation state %d: %d choices, flags 0x%x' % (state.id, len(state.choices), state.flags))
            for choice in state.choices:
                print('    clip %d, weight %d, mesh selections %s, models %s, effects %s, events %s' % (
                    choice.clip, choice.weight, choice.mesh_selections, choice.models.indices,
                    choice.effects.indices, choice.event_indices))
        for i, e in enumerate(m.events or []):
            print('  event %d: t=%.3f %s' % (i, e['time'], {k: v for k, v in e.items() if k != 'time'}))
        if m.events is None:
            print('  events: not decoded (cloth simulation records precede them)')
        for w in m.warnings:
            print('  warning:', w)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
