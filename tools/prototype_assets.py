"""Godot cache adaptations; preserve Phase 1's original model exports."""
from bisect import bisect_right
import json
from pathlib import Path
import struct


def in_place(gltf, binary):
    """Subtract J_Position's horizontal displacement from each skeleton root.

    J_Position and the pelvis are independent root joints in the original
    skeletons. Adjusting only the marker would leave the visible body moving.
    Keep vertical bobbing and all child-joint motion. Source keys are linear.
    """
    binary = bytearray(binary)
    parents = {child: i for i, node in enumerate(gltf['nodes']) for child in node.get('children', [])}
    changed = []

    def accessor(index, width):
        acc = gltf['accessors'][index]; view = gltf['bufferViews'][acc['bufferView']]
        if acc['componentType'] != 5126 or 'sparse' in acc or view.get('byteStride', width * 4) != width * 4:
            raise ValueError('unsupported prototype animation accessor')
        offset = view.get('byteOffset', 0) + acc.get('byteOffset', 0)
        values = [list(struct.unpack_from('<%df' % width, binary, offset + i * width * 4)) for i in range(acc['count'])]
        return acc, offset, values

    for animation in gltf.get('animations', []):
        if not any(term in animation['name'].lower() for term in ['_run', '_walk']):
            continue
        channels = {c['target']['node']: c for c in animation['channels'] if c['target']['path'] == 'translation'}
        for skin in gltf.get('skins', []):
            joints = set(skin['joints'])
            markers = [j for j in skin['joints'] if gltf['nodes'][j].get('name', '').replace('_', '').replace(' ', '').lower() == 'jposition']
            if not markers or markers[0] not in channels:
                continue
            marker = markers[0]
            sampler = animation['samplers'][channels[marker]['sampler']]
            if sampler.get('interpolation', 'LINEAR') != 'LINEAR':
                raise ValueError('prototype root motion requires linear source keys')
            _, _, times = accessor(sampler['input'], 1)
            _, _, keys = accessor(sampler['output'], 3)
            times = [t[0] for t in times]
            first = keys[0][:]

            def displacement(time):
                index = min(max(0, bisect_right(times, time) - 1), len(times) - 1)
                nxt = min(index + 1, len(times) - 1)
                amount = 0 if index == nxt else max(0, min(1, (time - times[index]) / (times[nxt] - times[index])))
                return [keys[index][a] + (keys[nxt][a] - keys[index][a]) * amount - first[a] for a in [0, 2]]

            for root in [j for j in joints if parents.get(j) not in joints and j in channels]:
                target = animation['samplers'][channels[root]['sampler']]
                _, _, root_times = accessor(target['input'], 1)
                acc, offset, values = accessor(target['output'], 3)
                for i, (time, value) in enumerate(zip(root_times, values)):
                    dx, dz = displacement(time[0])
                    value[0] -= dx; value[2] -= dz
                    struct.pack_into('<3f', binary, offset + i * 12, *value)
                if 'min' in acc:
                    acc['min'] = [min(v[a] for v in values) for a in range(3)]
                    acc['max'] = [max(v[a] for v in values) for a in range(3)]
            changed.append(animation['name'])
    return bytes(binary), sorted(set(changed))


def adapt(path):
    from wor2gltf import load_glb
    path = Path(path)
    gltf, binary = load_glb(path)
    binary, changed = in_place(gltf, binary)
    gltf.setdefault('extras', {})['wotrPrototype'] = {'in_place_locomotion': changed}
    js = json.dumps(gltf, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
    js += b' ' * (-len(js) % 4)
    binary += bytes(-len(binary) % 4)
    result = struct.pack('<3I', 0x46546c67, 2, 28 + len(js) + len(binary))
    result += struct.pack('<2I', len(js), 0x4e4f534a) + js
    result += struct.pack('<2I', len(binary), 0x004e4942) + binary
    path.write_bytes(result)
    return changed
