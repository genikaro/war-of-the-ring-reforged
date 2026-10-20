"""Godot cache adaptations; preserve Phase 1's original model exports."""
from bisect import bisect_right
import json
from pathlib import Path
import struct


def in_place(gltf, binary, center_pivot=False):
    """Subtract J_Position's horizontal displacement from each skeleton root.

    J_Position and the pelvis are independent root joints in the original
    skeletons. Adjusting only the marker would leave the visible body moving.
    Keep vertical bobbing and all child-joint motion. Source keys are linear.
    center_pivot also subtracts absolute marker offsets in every clip and the
    bind pose, so changing between running, ready and attacks cannot shift the
    center of rotation. Apply this to freshly converted assets, not twice.
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
        if not center_pivot and not any(term in animation['name'].lower() for term in ['_run', '_walk']):
            continue
        channels = {c['target']['node']: c for c in animation['channels'] if c['target']['path'] == 'translation'}
        for skin in gltf.get('skins', []):
            joints = set(skin['joints'])
            markers = [j for j in skin['joints'] if gltf['nodes'][j].get('name', '').replace('_', '').replace(' ', '').lower() == 'jposition']
            if not markers or (not center_pivot and markers[0] not in channels):
                continue
            marker = markers[0]
            sampler = animation['samplers'][channels[marker]['sampler']] if marker in channels else None
            if sampler and sampler.get('interpolation', 'LINEAR') != 'LINEAR':
                raise ValueError('prototype root motion requires linear source keys')
            if sampler:
                _, _, times = accessor(sampler['input'], 1)
                _, _, keys = accessor(sampler['output'], 3)
                times = [t[0] for t in times]
            else:
                times, keys = [0], [gltf['nodes'][marker].get('translation', [0,0,0])[:]]
            first = [0,0,0] if center_pivot else keys[0][:]

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
    if center_pivot:
        for skin in gltf.get('skins', []):
            joints = set(skin['joints'])
            marker = next((j for j in joints if gltf['nodes'][j].get('name','').replace('_','').replace(' ','').lower() == 'jposition'), None)
            if marker is None: continue
            pivot = gltf['nodes'][marker].get('translation',[0,0,0])[:]
            for root in [j for j in joints if parents.get(j) not in joints]:
                translation = gltf['nodes'][root].setdefault('translation',[0,0,0])
                for axis in [0,2]: translation[axis] -= pivot[axis]
    return bytes(binary), sorted(set(changed))


def adapt(path, center_pivot=False):
    from wor2gltf import load_glb
    path = Path(path)
    gltf, binary = load_glb(path)
    binary, changed = in_place(gltf, binary, center_pivot)
    locomotion = [name for name in changed if any(t in name.lower() for t in ['_run','_walk'])]
    gltf.setdefault('extras', {})['wotrPrototype'] = {'in_place_locomotion': locomotion, 'centered_animation_pivots': changed if center_pivot else []}
    restore_meshes(gltf)
    write_glb(path,gltf,binary)
    return locomotion


def restore_meshes(gltf):
    """Godot must import hidden equipment before runtime can switch its state."""
    appearance = gltf.get('extras',{}).get('wotrAppearance',{})
    for hidden in appearance.get('hiddenNodes',[]):
        if hidden['parent'] is None:
            children = gltf['scenes'][gltf.get('scene',0)].setdefault('nodes',[])
        else:
            children = gltf['nodes'][hidden['parent']].setdefault('children',[])
        if hidden['node'] not in children:
            children.append(hidden['node'])
    for index,node in enumerate(gltf['nodes']):
        if 'mesh' in node:
            node['name'] = 'WotrMesh_' + str(index)


def restore_geometry(path):
    from wor2gltf import load_glb
    path = Path(path); gltf,binary = load_glb(path)
    restore_meshes(gltf); write_glb(path,gltf,binary)


def write_glb(path,gltf,binary):
    js = json.dumps(gltf, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
    js += b' ' * (-len(js) % 4)
    binary += bytes(-len(binary) % 4)
    result = struct.pack('<3I', 0x46546c67, 2, 28 + len(js) + len(binary))
    result += struct.pack('<2I', len(js), 0x4e4f534a) + js
    result += struct.pack('<2I', len(binary), 0x004e4942) + binary
    path.write_bytes(result)
