"""Terrain palette (.ter version 4): composition keys and transition folders."""
import wor
from world_scene import version


def parse(data):
    r = wor.Reader(data)
    ver = version(r, [4])
    rid, name = r.take('I'), r.string()
    palette = []
    for _ in range(r.take('I')):
        label, key = r.string(), r.take('B')
        composition, composition_id, color = r.string(), r.take('I'), r.take('I')
        palette.append({'name': label, 'key': key, 'character': chr(key),
                        'composition': composition, 'composition_id': composition_id, 'color': color})
    folders = [r.string() for _ in range(r.take('I'))]
    if r.pos != len(data):
        raise ValueError('trailing terrain palette bytes')
    return {'version': ver, 'id': rid, 'name': name, 'palette': palette, 'folders': folders}
