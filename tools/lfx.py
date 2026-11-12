"""Sequential LIQDEFTD effect decoder (all 405 shipped files).

Six arrays contain texture, trail, beam and auxiliary tracks, particle
emitters and mesh emitters. Fixed records retain unnamed words by offset;
padding and editor heap bytes are not interpreted as floats. No scan for
magic values is used. Executable reader addresses and layouts are documented
in docs/NOTES-presentation-assets.md. This is data export, not a particle simulator.
"""
import struct

from asset_io import Reader
from texture_animation import read_uva


def _record(r, size, versions=(1,)):
    start = r.pos
    version = r.version(versions)
    data = r.take(size - 4)
    return {'version': version, 'words': list(struct.unpack('<%dI' % (len(data) // 4), data))}, start


def _transform(r):
    record, _ = _record(r, 56)
    count = record['words'][-1]
    if count > (len(r.data) - r.pos) // 144:
        raise ValueError('effect transform key count exceeds data')
    keys = []
    for _ in range(count):
        version = r.version((1, 1.01))
        raw = r.take(140 if version == 1 else 148)
        # Time is the second word, following the padded interpolation flag.
        keys.append({'version': version, 'time': struct.unpack_from('<f', raw, 4)[0],
                     'interpolate': bool(raw[0]), 'words': list(struct.unpack('<%dI' % (len(raw)//4), raw))})
    record['keys'] = keys
    return record


def _base(r):
    r.version((1,))
    raw = r.take(144)
    name = raw[:64].decode('utf-16-le', errors='replace').split('\0', 1)[0]
    duration, delay, parameter = struct.unpack_from('<3I', raw, 64)
    return {'name': name, 'duration_ms': duration, 'delay_ms': delay,
            'parameter': parameter, 'flags': list(raw[76:78]),
            'bindings': list(struct.unpack_from('<16i', raw, 80)), 'transform': _transform(r)}


def _emitter(r):
    record, _ = _record(r, 96)
    count = record['words'][-1]
    if count > (len(r.data) - r.pos) // 140:
        raise ValueError('emitter key count exceeds data')
    raw = b''.join(struct.pack('<I', v) for v in record['words'])
    record['name'] = raw[:64].decode('utf-16-le', errors='replace').split('\0', 1)[0]
    record['keys'] = [_record(r, 140)[0] for _ in range(count)]
    for key in record['keys']:
        key['time'] = struct.unpack('<f', struct.pack('<I', key['words'][1]))[0]
    return record


def parse(data):
    r = Reader(data)
    version = r.header(b'LIQDEFTD', (1,))
    header = r.read('10I')
    out = {'version': version, 'duration_ms': header[0], 'start_ms': header[1],
           'loop': bool(header[2] & 255), 'header_parameter': header[9]}
    for kind, count in zip(('texture_tracks', 'trail_tracks', 'beam_tracks', 'auxiliary_tracks'), header[5:9]):
        if count > len(data) // 200:
            raise ValueError('effect track count exceeds data')
        tracks = []
        for _ in range(count):
            v = r.version((0.01, 1, 1.01, 2, 2.01, 3) if kind == 'texture_tracks' else
                          (0.01, 0.02, 0.03, 1) if kind == 'beam_tracks' else (0.01, 1))
            n = r.count()
            fields, flags = [], []
            if kind == 'texture_tracks':
                flags = list(r.take((1 if v >= 1.01 else 0) + (2 if v >= 2.01 else 0)))
            elif kind == 'beam_tracks':
                fields = r.read('5I')
                flags = list(r.take(1 if v == 0.02 else 2 if v == 0.03 else 0))
            elif kind == 'auxiliary_tracks':
                # This header is version, parameter, flag+padding, key count.
                fields, n = [n, r.read('I')], r.count()
            track = {'version': v, 'track_flags': flags, 'parameters': fields, **_base(r), 'keys': []}
            for _ in range(n):
                if kind == 'texture_tracks':
                    key, _ = _record(r, 52)
                    w = key['words']
                    key.update(time=struct.unpack('<f', struct.pack('<I', w[1]))[0],
                               texture_kind=w[2], texture=w[3] if w[3] < 0x80000000 else w[3] - 2**32)
                    if key['texture_kind'] == 0:
                        key['uva'] = read_uva(r)
                elif kind == 'trail_tracks':
                    # Older trail keys use 0.01 in their first word; loader
                    # 0x6579ce does not version-dispatch this 28-byte record.
                    raw = r.take(28)
                    key = {'time': struct.unpack_from('<f', raw, 8)[0],
                           'words': list(struct.unpack('<7I', raw))}
                elif kind == 'beam_tracks':
                    raw = r.take(64)
                    key = {'time': struct.unpack_from('<f', raw, 8)[0],
                           'words': list(struct.unpack('<16I', raw)), 'uva': read_uva(r)}
                else:
                    key, _ = _record(r, 80)
                    key['time'] = struct.unpack('<f', struct.pack('<I', key['words'][1]))[0]
                track['keys'].append(key)
            tracks.append(track)
        out[kind] = tracks
    out['particle_emitters'] = []
    for _ in range(header[3]):
        r.version((1,))
        parameter = r.read('I')
        emitter = _emitter(r)
        r.version((1,))
        emitter.update(parameter=parameter, particle_keys=[_record(r, 40)[0] for _ in range(r.count(40))])
        out['particle_emitters'].append(emitter)
    out['mesh_emitters'] = []
    for _ in range(header[4]):
        r.version((1,))
        out['mesh_emitters'].append({'emitter': _emitter(r), 'transform': _transform(r)})
    r.finish()
    return out
