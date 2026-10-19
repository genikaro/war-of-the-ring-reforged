"""Parser for Liquid Entertainment .lms meshes (LIQD0LMS, versions 1.0, 1.1 and 2.0).

Layout (little endian), as reverse engineered from War of the Ring files:
    char[8]  "LIQD0LMS"
    f32      version (1.0, 1.1 or 2.0)
    u32      mesh group count
    u32      triangle_count (T)
    u32      vertex_count (V)       render vertices referenced by triangles
    u32      flags (bit 0 = skinned)
    u32      n (UV channel slots), u32[n] components per channel (2)
    u8       0
    str      attach joint name      u32 char count incl. NUL, UTF-16LE
    u32      position_count (P), normal_count (N), group_count (G), 0
    v2.0:    u32[n] uv count per channel (0 = unused)
    v1.0/1.1: u32[4] 0, then n x (u32 uv count, u32 0)
    f32[P][4] positions (w = 1)
    f32[N][4] normals (w = 1)
    i32      -1
    f32[U][2] uvs, for each used channel
    u16[T][3] triangles (render vertex indices)
    u32[V]   position index, normal index, group index, then uv index per channel
    then for positions, normals, groups, each uv channel:
             u32[count] usage counts + u32[V] vertex lists
    u8       0
    str      mesh name
    group metadata, optional frame arrays, then per-triangle flags (v1.1+)
"""
import struct
from dataclasses import dataclass, field


@dataclass
class Mesh:
    name: str
    attach: str
    skinned: bool
    positions: list
    normals: list
    uvs: list
    triangles: list
    pos_index: list
    nrm_index: list
    grp_index: list
    uv_index: list
    groups: list = field(default_factory=list)
    triangle_flags: list = field(default_factory=list)


class Reader:
    def __init__(self, data):
        self.d = data
        self.p = 0

    def take(self, fmt):
        if struct.calcsize('<' + fmt) > len(self.d) - self.p:
            raise ValueError('truncated LMS at 0x%x' % self.p)
        vals = struct.unpack_from('<' + fmt, self.d, self.p)
        self.p += struct.calcsize('<' + fmt)
        return vals

    def u32(self):
        return self.take('I')[0]

    def string(self):
        n = self.u32()
        if n > (len(self.d) - self.p) // 2:
            raise ValueError('truncated LMS string at 0x%x' % self.p)
        s = self.d[self.p:self.p + 2 * n].decode('utf-16-le').rstrip('\0')
        self.p += 2 * n
        return s


def group(r, version, components, legacy_counts, legacy_uv):
    r.take('B')
    name = r.string()
    frames, flags, parameter = r.take('IIf')
    if not frames:
        raise ValueError('LMS group has no frames')
    states = list(r.take('%dI' % frames))
    counts = r.take('4I') if version == 2.0 else legacy_counts
    uv_counts = r.take('%dI' % len(components)) if version == 2.0 else legacy_uv

    def arrays(mask):
        result = {}
        for bit, count, fmt, key in zip((1, 2, 4, 16), counts,
                                        ('4f', '4f', 'I', 'I'),
                                        ('positions', 'normals', 'groups', 'words')):
            if mask & bit:
                result[key] = [r.take(fmt) for _ in range(count)]
        if mask & 8:
            result['uvs'] = [[r.take('%df' % c) for _ in range(count)]
                             for c, count in zip(components, uv_counts)]
        return result

    base = arrays(flags >> 16)
    extra = [arrays(flags) for _ in range(frames - 1)]
    return {'name': name, 'flags': flags, 'parameter': parameter, 'states': states,
            'counts': list(counts), 'uv_counts': list(uv_counts), 'base': base, 'frames': extra}


def parse(data, *, read_footer=False):
    """Read render geometry; opt into strict group/triangle footer decoding.

    Navigation requires the footer. The strict reader does not yet support
    nine Obelisk effect meshes' animated group UV tails, so geometry-only
    reading remains available to the model converter.
    """
    r = Reader(data)
    magic = data[:8]
    if magic != b'LIQD0LMS':
        raise ValueError('not an LMS file')
    r.p = 8
    version, group_count, tris, verts, flags = r.take('fIIII')
    version = round(version, 2)
    if version not in (1.0, 1.1, 2.0):
        raise ValueError('unsupported LMS version %s' % version)
    skinned = flags & 1
    n = r.u32()
    components = r.take('%dI' % n)
    r.take('B')
    attach = r.string()
    npos, nnrm, ngrp, _zero = r.take('4I')
    if version == 2.0:
        channels = r.take('%dI' % n)
        legacy_counts, legacy_uv = (), ()
    else:
        legacy_counts = r.take('4I')
        channel_pairs = r.take('%dI' % (2 * n))
        channels, legacy_uv = channel_pairs[0::2], channel_pairs[1::2]
    channels = [c for c in channels if c]
    positions = [r.take('4f')[:3] for _ in range(npos)]
    normals = [r.take('4f')[:3] for _ in range(nnrm)]
    marker = r.take('i')[0]
    if marker != -1:
        raise ValueError('expected -1 before UVs, got %d at 0x%x' % (marker, r.p - 4))
    uv_sets = [[r.take('2f') for _ in range(c)] for c in channels]
    # Untextured meshes have no channel: give every vertex UV (0, 0).
    uvs = uv_sets[0] if uv_sets else [(0.0, 0.0)]
    nuv = len(uvs)
    flat = r.take('%dH' % (3 * tris))
    triangles = [flat[i:i + 3] for i in range(0, len(flat), 3)]
    pos_index = list(r.take('%dI' % verts))
    nrm_index = list(r.take('%dI' % verts))
    grp_index = list(r.take('%dI' % verts))
    uv_indices = [list(r.take('%dI' % verts)) for _ in channels]
    uv_index = uv_indices[0] if uv_indices else [0] * verts
    for count in [npos, nnrm, ngrp] + channels:
        usage = r.take('%dI' % count)
        if sum(usage) != verts:
            raise ValueError('usage table does not sum to vertex count at 0x%x' % r.p)
        r.take('%dI' % verts)
    groups, triangle_flags = [], []
    if read_footer:
        groups = [group(r, version, components, legacy_counts, legacy_uv) for _ in range(group_count)]
        name = groups[0]['name'] if groups else ''
        triangle_flags = [0] * tris
        if version >= 1.1:
            present = r.take('B')[0]
            if present not in (0, 1):
                raise ValueError('invalid LMS triangle flag presence byte')
            if present:
                triangle_flags = list(r.take('%dI' % tris))
        if r.p != len(data):
            raise ValueError('trailing LMS bytes at 0x%x' % r.p)
    else:
        r.take('B')
        name = r.string()
    for idx, limit in ((pos_index, npos), (nrm_index, nnrm), (grp_index, ngrp), (uv_index, nuv)):
        if idx and max(idx) >= limit:
            raise ValueError('index out of range')
    if any(max(t) >= verts for t in triangles):
        raise ValueError('triangle index out of range')
    return Mesh(name, attach, bool(skinned), positions, normals, uvs, triangles,
                pos_index, nrm_index, grp_index, uv_index, groups, triangle_flags)
