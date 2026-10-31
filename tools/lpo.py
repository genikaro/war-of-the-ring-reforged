"""LIQDPROJ projectile timelines, versions 1, 2, 5 and 6.

Keys bind Models/Effects, motion parameters and collision actions. Unknown
gameplay enums/parameters retain their original values. All 37 shipped files
are read sequentially to EOF. See NOTES-presentation-assets.md for readers.
"""
from asset_io import Reader
import struct
import math


def spin_parameters(projectile):
    """Bounded constant-spin adapter: the axe files' first motion vec4 is the local rotation axis,
    followed by its angular speed, read as degrees per second. Preserve all other motion fields as raw data."""
    keys = projectile.get('motion_keys', [])
    if not keys:
        return None
    if len(keys) != 1 or keys[0]['time'] != 0:
        raise ValueError('only constant projectile spin is supported')
    values = keys[0]['values']
    if len(values) != 13 or not isinstance(values[0], list) or len(values[0]) != 4:
        raise ValueError('invalid projectile spin fields')
    def number(word):
        return struct.unpack('<f', struct.pack('<I', word))[0]
    speed = number(values[1])
    axis = [number(word) for word in values[0][:3]]
    if not all(math.isfinite(v) for v in [speed, *axis]) or abs(speed) > 36000:
        raise ValueError('invalid projectile spin')
    if speed == 0:
        return None
    length = math.sqrt(sum(v*v for v in axis))
    if length < 1e-6:
        raise ValueError('zero projectile spin axis')
    return {'Axis': [v/length for v in axis], 'DegreesPerSecond': speed}


def launch_sound_events(projectile):
    """Known launch-event payload: event 7, opcode 46, SoundEvents ID.

    Preserve the raw payload in parse(); unknown opcodes remain uninterpreted.
    The mapping agrees across Elven, ordinary, fire, ballista and axe launches.
    """
    result = []
    for event in projectile.get('events', []):
        payload = bytes.fromhex(event.get('name_bytes', ''))
        if event['id'] == 7 and event['type'] == 0 and len(payload) == 8:
            opcode, sound = struct.unpack('<ii', payload)
            if opcode == 46 and sound >= 0 and sound not in result:
                result.append(sound)
    return result


def _action(r, version):
    result = {'type': r.read('i')}
    if version == 1:
        result['id'] = r.read('i')
    else:
        result['id'] = r.read('i')
        result['name_bytes'] = r.take(r.count(1)).hex()
    result['parameters'] = r.read('5I' if version in (1, 2) else '9I')
    if version == 6:
        result['flags'] = list(r.take(6))
    kind = result['shape'] = r.read('i')
    result['shape_parameters'] = [r.read('I') for _ in range({0: 4, 1: 6, 2: 1, 3: 2}.get(kind, 0))]
    return result


def _behavior(r, version):
    kind = r.read('i')
    result = {'type': kind, 'parameters': [r.read('I') for _ in range(1 if kind == 0 else 9 if kind == 1 else 0)]}
    result['flag'] = r.read('B') if version >= 5 else 0
    if version >= 5:
        result['value_bits'] = r.read('I')
    result['enabled'] = r.read('B')
    result['values'] = r.read('6I')
    if version >= 5:
        result['flags'] = list(r.take(5))
    if version == 1:
        result['legacy_action_words'] = r.read('14I')
    else:
        result['action'] = _action(r, version)
    return result


def _resource(r):
    version = r.read('I')
    if version not in (1, 2):
        raise ValueError('unsupported projectile resource key version')
    kind = r.read('i')
    result = {'version': version, 'kind': kind, 'id': r.read('i') if kind in (0, 1) else -1}
    if version == 2 and kind == 1:
        result['flag'] = r.read('B')
    result['scale'], result['value'] = r.read('ff')
    result['bindings'] = []
    for _ in range(r.count(9 if version == 2 else 8)):
        item = {'source': r.read('i'), 'target': r.read('i')}
        if version == 2:
            item['flag'] = r.read('B')
        result['bindings'].append(item)
    return result


def _motion(r):
    r.expect(1)
    flag = r.read('B')
    # Eight vec4s, four scalar parameters and ten trailing parameters.
    values = []
    for fmt in ('4I', 'I', '4I', 'I', '4I', '4I', '4I', 'I', '4I', 'I', '4I', '4I', '10I'):
        values.append(r.read(fmt))
    return {'flag': flag, 'values': values}


def parse(data):
    r = Reader(data)
    version = int(r.header(b'LIQDPROJ', (1, 2, 5, 6)))
    out = {'version': version, 'duration_ms': r.read('I')}
    if not out['duration_ms']:
        raise ValueError('zero-duration projectile')
    for name, read in (('control_keys', None), ('resource_keys', _resource), ('motion_keys', _motion)):
        keys = []
        for _ in range(r.count()):
            time = r.read('f')
            if read:
                key = read(r)
            else:
                r.expect(1)
                kind, a, b = r.read('3i')
                key = {'type': kind, 'parameters': [a, b]}
                if kind == 0:
                    key.update(flag=r.read('B'), value=r.read('f'))
                elif kind == 3:
                    key.update(id=r.read('i'), value=r.read('f'))
                elif kind == 4:
                    key['value'] = r.read('f')
            keys.append({'time': time, **key})
        out[name] = keys
    out['behavior'] = _behavior(r, version)
    out['actions'] = [_action(r, version) for _ in range(r.count())]
    out['events'] = []
    for _ in range(r.count()):
        item = {'id': r.read('i')}
        if version != 1:
            item['name_bytes'] = r.take(r.count(1)).hex()
        kind = item['type'] = r.read('i')
        item['parameters'] = [r.read('I') for _ in range(1 if version == 1 else 2 if kind in (0, 1, 2) else 0)]
        out['events'].append(item)
    r.finish()
    return out
