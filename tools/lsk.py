"""Parser for Liquid Entertainment .lsk skeletons with animation clips (LIQD0LSK 2.0).

Layout (little endian). Strings are u8 0, u32 char count incl. NUL, UTF-16LE.
    char[8]  "LIQD0LSK"
    f32      version (2.0)
    u32      joint_count (J), u32 clip_count (C)
    f32[J][4] bind offsets from the parent joint, world aligned (w = 1)
    f32[J][4] bind positions in model space (w = 1)
    f32[J][4] bind orientations, quaternion w x y z (authoring tool frames;
             not needed for skinning, all poses below are world aligned)
    J joints: str name, u32 flags, i32 parent, u32 child_count, u32 child_list_offset
    C clips:  str name, f32 duration (seconds), then per joint:
             u32 key_count (K), u8 has_position, u8 has_rotation,
             f32[K] key times normalised to 0..1,
             f32[K][4] positions (w = 1) if has_position, replacing the bind offset,
             f32[K][4] rotations, quaternion w x y z, if has_rotation.

Joint frames are world aligned in the bind pose. Rotations are local deltas
from it: world_rot(j) = world_rot(parent) * rot(j), and joint j sits at
world_pos(parent) + world_rot(parent) applied to its offset (bind offset or
position key). Roots have no parent, so their offset is a model-space position.
Joints without keys keep the bind offset and inherit the parent's motion.
Version 1.0 files (static buildings and objects, a few units) are not handled.
"""
from dataclasses import dataclass, field

from lms import Reader


@dataclass
class Joint:
    name: str
    parent: int
    offset: tuple
    bind_position: tuple
    bind_orientation: tuple
    flags: int


@dataclass
class Track:
    times: tuple
    positions: list = field(default_factory=list)
    rotations: list = field(default_factory=list)


@dataclass
class Clip:
    name: str
    duration: float
    tracks: list


@dataclass
class Skeleton:
    joints: list
    clips: list

    def joint_index(self, name):
        return next(i for i, j in enumerate(self.joints) if j.name == name)


def _string(r):
    r.take('B')
    return r.string()


def parse(data):
    if data[:8] != b'LIQD0LSK':
        raise ValueError('not an LSK file')
    r = Reader(data)
    r.p = 8
    version, njoints, nclips = r.take('fII')
    if version != 2.0:
        raise ValueError('unsupported LSK version %s' % version)
    offsets = [r.take('4f')[:3] for _ in range(njoints)]
    positions = [r.take('4f')[:3] for _ in range(njoints)]
    orientations = [r.take('4f') for _ in range(njoints)]
    joints = []
    for i in range(njoints):
        name = _string(r)
        flags, parent, _children, _child_offset = r.take('IiII')
        if parent >= i:
            raise ValueError('joint %s: parent %d not before child' % (name, parent))
        joints.append(Joint(name, parent, offsets[i], positions[i], orientations[i], flags))
    clips = []
    for _ in range(nclips):
        name = _string(r)
        duration = r.take('f')[0]
        tracks = []
        for _j in range(njoints):
            keys, has_pos, has_rot = r.take('IBB')
            track = Track(r.take('%df' % keys))
            if has_pos:
                track.positions = [r.take('4f')[:3] for _ in range(keys)]
            if has_rot:
                track.rotations = [r.take('4f') for _ in range(keys)]
            tracks.append(track)
        clips.append(Clip(name, duration, tracks))
    if r.p != len(data):
        raise ValueError('%d trailing bytes' % (len(data) - r.p))
    return Skeleton(joints, clips)
