"""Parser for Liquid Entertainment .lwt skin weights (LIQD0LWT 1.0).

Layout (little endian):
    char[8]  "LIQD0LWT"
    f32      version (1.0)
    u32      position_count (P)     matches the .lms position count
    u32      influence_count (I)
    u32      joint_count (J)        matches the .lsk joint count
    u32[P][2] (first influence, influence count) per mesh position
    (u32 joint, f32 weight)[I]
    f32[J][4] joint bind positions (same as the skeleton's, w = 1)
"""
import struct


def parse(data):
    if data[:8] != b'LIQD0LWT':
        raise ValueError('not an LWT file')
    version, npos, ninf, njoints = struct.unpack_from('<fIII', data, 8)
    if version != 1.0:
        raise ValueError('unsupported LWT version %s' % version)
    p = 0x18
    ranges = [struct.unpack_from('<II', data, p + 8 * i) for i in range(npos)]
    p += 8 * npos
    influences = [struct.unpack_from('<If', data, p + 8 * i) for i in range(ninf)]
    p += 8 * ninf + 16 * njoints
    if p != len(data):
        raise ValueError('size mismatch: expected %d, got %d' % (p, len(data)))
    weights = []
    for first, count in ranges:
        inf = influences[first:first + count]
        if len(inf) != count or any(j >= njoints for j, _ in inf):
            raise ValueError('bad influence range')
        weights.append(inf)
    return weights, njoints
