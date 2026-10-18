"""Sequential reader for the animation-state block embedded in .lmd models.

Recovered from the files and Rings.exe readers at 0x5ed556 and 0x5e749b.
The caller supplies lmd._Reader; this module has no dependency on game files.
Unknown property flags and event payloads are retained without assigning them
gameplay meanings. See NOTES-animation-states.md for the byte layout.
"""
from dataclasses import dataclass, field
import math


@dataclass
class Visibility:
    size: int
    indices: list


@dataclass
class AnimationChoice:
    version: int
    weight: int
    clip: int
    mesh_selections: list
    event_indices: list
    secondary_event_indices: list
    models: Visibility
    effects: Visibility
    cloths: Visibility
    auxiliary: Visibility
    mesh_tracks: list = field(default_factory=list)
    auxiliary_tracks: list = field(default_factory=list)
    model_tracks: list = field(default_factory=list)
    effect_tracks: list = field(default_factory=list)
    cloth_tracks: list = field(default_factory=list)
    event_keys: list = field(default_factory=list)
    secondary_keys: list = field(default_factory=list)


@dataclass
class AnimationState:
    id: int
    total_weight: int
    flags: int
    choices: list


def _visibility(r):
    population, size = r.unpack('II')
    words = r.ints((size + 31) // 32)
    if sum(word.bit_count() for word in words) != population:
        raise ValueError('visibility bit count does not match its population')
    if size % 32 and words and words[-1] >> (size % 32):
        raise ValueError('visibility bits outside the declared size')
    indices = [i for i in range(size) if words[i // 32] >> (i % 32) & 1]
    return Visibility(size, indices)


def _boolean(r, allow_zero=False):
    version = r.unpack('I')
    if version not in ((0, 1) if allow_zero else (1,)):
        raise ValueError('unsupported property boolean version %d' % version)
    return bool(r.unpack('B'))


def _parameters(r, effect=False):
    r.expect(1, 'property parameters')
    return {'flags': list(r.unpack('5B' if effect else '11B')),
            'values': r.signed(2 if effect else 5), 'value': r.unpack('f')}


def _property_key(r, kind):
    r.expect(1, 'property key')
    flags = list(r.unpack('3B' if kind in ('model', 'effect') else '2B'))
    shown = _boolean(r, allow_zero=True)
    secondary = _boolean(r) if kind in ('model', 'effect') else None
    return {'flags': flags, 'shown': shown, 'secondary_flag': secondary,
            'parameters': _parameters(r, effect=kind == 'effect')}


def _time(r):
    value = r.unpack('f')
    if not math.isfinite(value):
        raise ValueError('non-finite state key time')
    return value


def _tracks(r, count, kind):
    if not r.unpack('B'):
        return []
    result = []
    for _ in range(count):
        keys = []
        for _ in range(r.count()):
            time = _time(r)
            if kind == 'auxiliary':
                r.expect(1, 'auxiliary property key')
                value = {'flag': r.unpack('B'), 'value': _boolean(r, allow_zero=True)}
            else:
                value = _property_key(r, kind)
            keys.append({'time': time, **value})
        result.append(keys)
    return result


def _choice(r):
    version, weight, meshes, events, secondary, clip = r.unpack('5Ii')
    if version not in (1, 2):
        raise ValueError('unsupported animation choice version %d' % version)
    selections = r.signed(meshes)
    event_indices, secondary_indices = r.ints(events), r.ints(secondary)
    models, effects, cloths = [_visibility(r) for _ in range(3)]
    auxiliary = _visibility(r) if version == 2 else Visibility(0, [])
    choice = AnimationChoice(version, weight, clip, selections, event_indices,
                             secondary_indices, models, effects, cloths, auxiliary)
    if version == 1:
        return choice
    choice.mesh_tracks = _tracks(r, meshes, 'mesh')
    choice.auxiliary_tracks = _tracks(r, auxiliary.size, 'auxiliary')
    choice.model_tracks = _tracks(r, models.size, 'model')
    choice.effect_tracks = _tracks(r, effects.size, 'effect')
    choice.cloth_tracks = _tracks(r, cloths.size, 'mesh')
    for _ in range(r.count()):
        time = _time(r)
        r.expect(1, 'event key')
        version = r.unpack('I')
        if version not in (0, 1):
            raise ValueError('unsupported event value version %d' % version)
        value, flag = r.unpack('iB')
        choice.event_keys.append({'time': time, 'version': version, 'value': value, 'flag': flag})
    for _ in range(r.count()):
        time = _time(r)
        r.expect(1, 'secondary key')
        r.expect(1, 'secondary key values')
        count = r.count()
        flags = [r.unpack('B') for _ in range(count)]
        values = r.signed(count)
        types = [r.unpack('B') for _ in range(count)]
        choice.secondary_keys.append({'time': time, 'flags': flags, 'values': values, 'types': types})
    return choice


def read_states(r, count):
    """Read exactly count states and their dense state-ID-to-record-index map."""
    states = []
    for _ in range(count):
        r.expect(0x64, 'animation state')
        state_id, total_weight, choices, flags = r.unpack('4I')
        if choices > (len(r.data) - r.pos) // 24:
            raise ValueError('animation choice count exceeds remaining data')
        states.append(AnimationState(state_id, total_weight, flags,
                                     [_choice(r) for _ in range(choices)]))
    lookup = r.signed(r.count()) if count else []
    if len({state.id for state in states}) != len(states):
        raise ValueError('duplicate animation state id')
    for index, state in enumerate(states):
        if state.id >= len(lookup) or lookup[state.id] != index:
            raise ValueError('animation state lookup mismatch for id %d' % state.id)
    if any(index < -1 or index >= count for index in lookup):
        raise ValueError('animation state lookup index out of range')
    return states, lookup
