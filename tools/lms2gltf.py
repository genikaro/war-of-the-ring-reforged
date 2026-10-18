"""Convert War of the Ring models (.lms meshes, .dds textures, .lsk skeletons,
.lwt weights) into binary glTF (.glb).

Modes:
    model    resolve a unit or model by name through the database
             (resolve_model.py) and export everything it references:
             per-part textures, skeleton with all clips, skinning, and the
             attached models (weapons on joints, heads skinned to the parent
             skeleton, riders with their own skeleton).
    units    run "model" for every Units.lqr record, write one .glb per model
             plus index.json for the viewer.
    files    manual export of given meshes with one texture.

All state-controlled geometry is retained. The default glTF scene uses the
class's declared idle state, with level 1 gear and blood off. The viewer uses
extras.wotrAppearance to switch meshes and attachments, including timed keys.
--level and --blood set the preview configuration; --all-attachments exposes
all model attachments for debugging. Effects and cloth are not yet exported.

Team colour: parts with a *_Mask.dds use Materials/Team Color.lmt, two D3D9
fixed-function stages: stage 0 MODULATE(texture, lit diffuse), stage 1
MULTIPLYADD = current + mask * TFACTOR, alpha from stage 0. TFACTOR is the
player's TeamColors.lqr colour, and the diffuse texture is black under the
mask. That is glTF emissive exactly: emissiveTexture = mask, emissiveFactor =
team colour (TEAM_COLOR baked in; engines set it per player).

Usage:
    python lms2gltf.py model UNIT_G_HERO_ARAGORN --out ../converted/G_Aragorn.glb
    python lms2gltf.py units --out-dir ../converted/units
    python lms2gltf.py files --texture tex.dds --out m.glb [--skeleton s.lsk] mesh.lms[=weights.lwt] ...
"""
import argparse
import io
import json
import math
import os
import struct
import sys
import time

from PIL import Image

import lms
import lsk
import lwt
import model_appearance

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
# ponytail: TeamColors.lqr TEAM_COLOR_RED (record 0) copied, not read; per-player colour is the engine's job.
TEAM_COLOR = (185, 0, 0)


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def to_gltf_axes(v):
    # Game data is Z-up facing +Y; glTF is Y-up facing +Z. Proper rotation, keeps handedness.
    x, y, z = v
    return (-x, z, y)


def to_gltf_quat(q):
    # Game quaternions are w x y z; glTF wants x y z w in its own axes.
    w, x, y, z = q
    return to_gltf_axes((x, y, z)) + (w,)


def flatten(mesh):
    """Expand render vertices into flat position/normal/uv arrays."""
    pos = [to_gltf_axes(mesh.positions[i]) for i in mesh.pos_index]
    nrm = [to_gltf_axes(mesh.normals[i]) for i in mesh.nrm_index]
    uv = [mesh.uvs[i] for i in mesh.uv_index]
    tris = [list(t) for t in mesh.triangles]
    # Pick the winding that agrees with the stored normals.
    agree = 0
    for a, b, c in tris:
        pa, pb, pc = pos[a], pos[b], pos[c]
        e1 = [pb[k] - pa[k] for k in range(3)]
        e2 = [pc[k] - pa[k] for k in range(3)]
        fn = (e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0])
        vn = [nrm[a][k] + nrm[b][k] + nrm[c][k] for k in range(3)]
        agree += 1 if sum(fn[k] * vn[k] for k in range(3)) > 0 else -1
    if agree < 0:
        tris = [[a, c, b] for a, b, c in tris]
    # Stored normals have w = 1 and are unit length; renormalise after rounding.
    nrm = [tuple(c / (math.sqrt(sum(k * k for k in n)) or 1.0) for c in n) for n in nrm]
    return pos, nrm, uv, tris, agree < 0


def vertex_weights(mesh, weights):
    """Top four (joints, weights) per render vertex, normalised."""
    if len(weights) != len(mesh.positions):
        raise ValueError('%s: %d weight entries for %d positions' % (
            mesh.name, len(weights), len(mesh.positions)))
    joints, values = [], []
    for i in mesh.pos_index:
        inf = sorted(weights[i], key=lambda jw: -jw[1])[:4]
        total = sum(w for _, w in inf) or 1.0
        inf += [(0, 0.0)] * (4 - len(inf))
        joints.append(tuple(j for j, _ in inf))
        values.append(tuple(w / total for _, w in inf))
    return joints, values


class Exporter:
    """Accumulates glTF nodes, meshes, materials, skins and animations."""

    def __init__(self):
        self.bin = bytearray()
        self.views, self.accessors = [], []
        self.nodes, self.meshes, self.materials = [], [], []
        self.textures, self.images, self.skins, self.animations = [], [], [], []
        self.scene = []
        self._material_for = {}
        self._texture_for = {}
        self._frames = {}
        self.log = []
        self.appearance = None

    # -- buffers -------------------------------------------------------
    def _view(self, data, target=None):
        while len(self.bin) % 4:
            self.bin.append(0)
        view = {'buffer': 0, 'byteOffset': len(self.bin), 'byteLength': len(data)}
        if target:
            view['target'] = target
        self.bin += data
        self.views.append(view)
        return len(self.views) - 1

    def accessor(self, rows, fmt, gltf_type, component, target=None, minmax=False):
        data = b''.join(struct.pack('<' + fmt, *r) if isinstance(r, (tuple, list)) else struct.pack('<' + fmt, r)
                        for r in rows)
        acc = {'bufferView': self._view(data, target), 'componentType': component,
               'count': len(rows), 'type': gltf_type}
        if minmax:
            cols = [r if isinstance(r, (tuple, list)) else (r,) for r in rows]
            acc['min'] = [min(c[k] for c in cols) for k in range(len(cols[0]))]
            acc['max'] = [max(c[k] for c in cols) for k in range(len(cols[0]))]
        self.accessors.append(acc)
        return len(self.accessors) - 1

    # -- scene pieces --------------------------------------------------
    def node(self, node, parent=None):
        self.nodes.append(node)
        index = len(self.nodes) - 1
        if parent is None:
            self.scene.append(index)
        else:
            self.nodes[parent].setdefault('children', []).append(index)
        return index

    def texture(self, path, keep_alpha=True):
        """Embed an image once as PNG; return (texture index, has alpha)."""
        if path not in self._texture_for:
            img = Image.open(path)
            img.load()
            has_alpha = keep_alpha and img.mode in ('RGBA', 'LA') and img.getextrema()[-1][0] < 255
            png = io.BytesIO()
            img.convert('RGBA' if has_alpha else 'RGB').save(png, 'PNG')
            self.images.append({'bufferView': self._view(png.getvalue()), 'mimeType': 'image/png'})
            self.textures.append({'source': len(self.images) - 1, 'sampler': 0})
            self._texture_for[path] = (len(self.textures) - 1, has_alpha)
        return self._texture_for[path]

    def material(self, texture_path, mask_path=None):
        key = (texture_path, mask_path)
        if key in self._material_for:
            return self._material_for[key]
        if texture_path:
            index, has_alpha = self.texture(texture_path)
            mat = {'name': os.path.splitext(os.path.basename(texture_path))[0],
                   'pbrMetallicRoughness': {'baseColorTexture': {'index': index},
                                            'metallicFactor': 0.0, 'roughnessFactor': 0.9}}
            if has_alpha:
                mat.update(alphaMode='MASK', alphaCutoff=0.5, doubleSided=True)
            if mask_path:       # Team Color.lmt, see the module docstring; the mask's alpha is unused
                mat['name'] += ' team'
                mat['emissiveTexture'] = {'index': self.texture(mask_path, keep_alpha=False)[0]}
                mat['emissiveFactor'] = [srgb_to_linear(c / 255) for c in TEAM_COLOR]
        else:
            mat = {'name': 'untextured', 'pbrMetallicRoughness': {
                'baseColorFactor': [0.6, 0.6, 0.6, 1.0], 'metallicFactor': 0.0, 'roughnessFactor': 0.9}}
        self.materials.append(mat)
        self._material_for[key] = len(self.materials) - 1
        return self._material_for[key]

    def skeleton(self, skel, parent=None, clip_prefix=''):
        """Add joint nodes, a skin and one animation per clip; return (joint nodes, skin)."""
        base = len(self.nodes)
        for j in skel.joints:
            self.nodes.append({'name': j.name, 'translation': list(to_gltf_axes(j.offset))})
        for i, j in enumerate(skel.joints):
            if j.parent < 0:
                if parent is None:
                    self.scene.append(base + i)
                else:
                    self.nodes[parent].setdefault('children', []).append(base + i)
            else:
                self.nodes[base + j.parent].setdefault('children', []).append(base + i)
        joints = list(range(base, base + len(skel.joints)))
        # Bind frames are world aligned, so inverse bind matrices are pure translations.
        world = []
        for j in skel.joints:
            off = to_gltf_axes(j.offset)
            world.append(off if j.parent < 0 else tuple(world[j.parent][k] + off[k] for k in range(3)))
        ibm = [(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -x, -y, -z, 1) for x, y, z in world]
        self.skins.append({'joints': joints, 'inverseBindMatrices': self.accessor(ibm, '16f', 'MAT4', 5126)})
        for clip in skel.clips:
            samplers, channels = [], []
            for node, track in zip(joints, clip.tracks):
                if not track.times:
                    continue
                key_times = [t * clip.duration for t in track.times]
                hold_end = key_times[-1] < clip.duration
                if hold_end:
                    key_times.append(clip.duration)
                times = self.accessor(key_times, 'f', 'SCALAR', 5126, minmax=True)
                for path, keys, conv, fmt, kind in (
                        ('rotation', track.rotations, to_gltf_quat, '4f', 'VEC4'),
                        ('translation', track.positions, to_gltf_axes, '3f', 'VEC3')):
                    if keys:
                        values = [conv(k) for k in keys]
                        if hold_end:
                            values.append(values[-1])
                        out = self.accessor(values, fmt, kind, 5126)
                        samplers.append({'input': times, 'output': out, 'interpolation': 'LINEAR'})
                        channels.append({'sampler': len(samplers) - 1, 'target': {'node': node, 'path': path}})
            if not channels and joints:
                # Static idle clips still have a duration and can carry timed
                # appearance keys. glTF requires at least one animation channel.
                times = [0, clip.duration] if clip.duration > 0 else [0]
                value = list(to_gltf_axes(skel.joints[0].offset))
                samplers.append({'input': self.accessor(times, 'f', 'SCALAR', 5126, minmax=True),
                                 'output': self.accessor([value] * len(times), '3f', 'VEC3', 5126),
                                 'interpolation': 'STEP'})
                channels.append({'sampler': 0, 'target': {'node': joints[0], 'path': 'translation'}})
            if channels:
                self.animations.append({'name': clip_prefix + clip.name, 'samplers': samplers, 'channels': channels})
        return joints, len(self.skins) - 1

    def bone_frame(self, ctx, joint):
        """Node under a joint carrying its bind orientation. Rigid meshes attached
        to a joint are authored in that frame (a head runs along the bone's +X)."""
        skel, joints, _ = ctx
        key = (id(skel), joint)
        if key not in self._frames:
            q = skel.joints[joint].bind_orientation
            if abs(q[0]) > 0.99999:
                self._frames[key] = joints[joint]
            else:
                self._frames[key] = self.node({'name': skel.joints[joint].name + ' frame',
                                               'rotation': list(to_gltf_quat(q))}, joints[joint])
        return self._frames[key]

    def mesh(self, m, texture_path=None, weights_path=None, skin=None, njoints=0, parent=None, mask_path=None):
        """Add a parsed lms.Mesh. It is skinned only if it is flagged skinned and has weights."""
        pos, nrm, uv, tris, _ = flatten(m)
        attrs = {
            'POSITION': self.accessor(pos, '3f', 'VEC3', 5126, 34962, minmax=True),
            'NORMAL': self.accessor(nrm, '3f', 'VEC3', 5126, 34962),
            'TEXCOORD_0': self.accessor(uv, '2f', 'VEC2', 5126, 34962),
        }
        node = {'name': m.name, 'mesh': len(self.meshes)}
        if weights_path and skin is not None and m.skinned:
            weights, wj = lwt.parse(open(weights_path, 'rb').read())
            if wj != njoints:
                raise ValueError('%s: %d joints, skeleton has %d' % (weights_path, wj, njoints))
            joints, values = vertex_weights(m, weights)
            attrs['JOINTS_0'] = self.accessor(joints, '4H', 'VEC4', 5123, 34962)
            attrs['WEIGHTS_0'] = self.accessor(values, '4f', 'VEC4', 5126, 34962)
            node['skin'] = skin
            # Skinned mesh transforms are ignored by glTF; parents with a scale
            # get applied twice by some loaders, so keep the node at the root.
            parent = None
        self.meshes.append({'name': m.name, 'primitives': [{
            'attributes': attrs,
            'indices': self.accessor([i for t in tris for i in t], 'H', 'SCALAR', 5123, 34963),
            'material': self.material(texture_path, mask_path)}]})
        self.node(node, parent)
        return len(tris)

    def write(self, out_path):
        gltf = {
            'asset': {'version': '2.0', 'generator': 'wotr-reforged lms2gltf'},
            'scene': 0,
            'scenes': [{'nodes': self.scene}],
            'nodes': self.nodes,
            'meshes': self.meshes,
            'materials': self.materials,
            'accessors': self.accessors,
            'bufferViews': self.views,
            'buffers': [{'byteLength': len(self.bin)}],
        }
        if self.textures:
            gltf.update(textures=self.textures, images=self.images, samplers=[
                {'magFilter': 9729, 'minFilter': 9987, 'wrapS': 10497, 'wrapT': 10497}])
        if self.skins:
            gltf['skins'] = self.skins
        if self.animations:
            gltf['animations'] = self.animations
        if self.appearance is not None:
            gltf['extras'] = {'wotrAppearance': self.appearance}
        js = json.dumps(gltf, separators=(',', ':')).encode()
        js += b' ' * (-len(js) % 4)
        binary = bytes(self.bin) + b'\0' * (-len(self.bin) % 4)
        total = 12 + 8 + len(js) + 8 + len(binary)
        os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
        with open(out_path, 'wb') as f:
            f.write(struct.pack('<III', 0x46546C67, 2, total))
            f.write(struct.pack('<II', len(js), 0x4E4F534A) + js)
            f.write(struct.pack('<II', len(binary), 0x004E4942) + binary)
        return total


# -- resolver driven export ---------------------------------------------

def _file(entry):
    return os.path.join(ROOT, entry['file']) if entry and entry.get('exists') else None


def _scale(res):
    sx, sy, sz = res.get('scale') or (1.0, 1.0, 1.0)
    return (sx, sz, sy)


def _offset_node(name, offset, rel_scale):
    w, x, y, z, px, py, pz, s = offset
    node = {'name': name}
    if (w, x, y, z) != (1.0, 0.0, 0.0, 0.0):
        node['rotation'] = list(to_gltf_quat((w, x, y, z)))
    if (px, py, pz) != (0.0, 0.0, 0.0):
        node['translation'] = list(to_gltf_axes((px, py, pz)))
    scale = [s * k for k in rel_scale]
    if any(abs(k - 1.0) > 1e-6 for k in scale):
        node['scale'] = scale
    return node


def _add_model(ex, res, parent, ctx, owner=None, attachment=None, depth=0, on_joint=False):
    """Add one resolved model under node `parent`. ctx = (skeleton, joint nodes, skin) or None.
    Model scales in the database are absolute, so attachments get the ratio to their parent."""
    name = res.get('model') or os.path.basename(res.get('file', '?'))
    index = len(ex.appearance['models'])
    appearance = model_appearance.instance(res, owner, attachment, '' if depth == 0 else name + '/')
    ex.appearance['models'].append(appearance)
    skel_entry = res['skeletons'][0] if res.get('skeletons') else None
    if skel_entry and _file(skel_entry):
        try:
            with open(_file(skel_entry), 'rb') as f:
                skel = lsk.parse(f.read())
            joints, skin = ex.skeleton(skel, parent, '' if depth == 0 else name + '/')
            ctx = (skel, joints, skin)
        except ValueError as e:
            ex.log.append('%s: skeleton skipped (%s), static export' % (name, e))
            ctx = None
    # Texture keys (model_appearance.texture_keys) swap whole-model textures per
    # state. Embed every keyed slot and start from the default state's choice.
    slot_files = [_file(t) for t in res.get('textures', [])]
    keyed = {}
    for state in appearance['states']:
        for choice in state['choices']:
            for _, stages in choice['textureKeys']:
                for stage, tex in enumerate(stages):
                    if 0 <= tex < len(slot_files) and slot_files[tex]:
                        keyed.setdefault(tex, stage)
    if keyed:
        appearance['textures'] = [ex.texture(slot_files[t], keep_alpha=keyed[t] == 0)[0] if t in keyed else None
                                  for t in range(len(slot_files))]
    base, team_mask = model_appearance.texture_slots(model_appearance.default_choice(appearance))
    team = 'MATERIAL_TEAM_COLOR' in res.get('materials', [])
    for slot, part in enumerate(res['parts']):
        mesh_file = _file(part['mesh'])
        if not mesh_file:
            ex.log.append('%s: missing mesh %s' % (name, part['mesh'].get('file')))
            continue
        weights = _file(part.get('weights'))
        texture, mask = _file(part.get('texture')), _file(part.get('mask'))
        if base in keyed:
            texture = slot_files[base]
        if team and team_mask in keyed:
            mask = slot_files[team_mask]
        try:
            with open(mesh_file, 'rb') as f:
                m = lms.parse(f.read())
            if weights and ctx and m.skinned:
                ex.mesh(m, texture, weights, ctx[2], len(ctx[0].joints), mask_path=mask)
            else:
                anchor = parent
                if not on_joint and ctx and m.attach:     # rigid part that names its joint
                    anchor = ex.bone_frame(ctx, ctx[0].joint_index(m.attach))
                ex.mesh(m, texture, parent=anchor, mask_path=mask)
            # mesh() appends exactly one leaf node, even for root-level skins.
            appearance['parts'][slot] = len(ex.nodes) - 1
        except (ValueError, struct.error, StopIteration) as e:
            ex.log.append('%s: part %s skipped (%s)' % (name, os.path.basename(mesh_file), e))
    attachments = [a for a in res.get('attachments', []) if a['kind'] == 'model']
    for i, a in enumerate(attachments):
        if 'parts' not in a['model']:
            continue
        sub = a['model']
        on = a['joint'] >= 0 and ctx is not None and a['joint'] < len(ctx[1])
        anchor = ex.bone_frame(ctx, a['joint']) if on else parent
        rel = [c / p for c, p in zip(_scale(sub), _scale(res))]
        node = ex.node(_offset_node(sub.get('model') or 'attachment', a['offset'], rel), anchor)
        _add_model(ex, sub, node, ctx, index, i, depth + 1, on)


def convert_model(resolver, key, out_path, all_attachments=False, level=1, blood=False):
    if level not in range(1, 5):
        raise ValueError('gear level must be between 1 and 4')
    res = resolver.resolve(key)
    ex = Exporter()
    ex.appearance = {'version': 1, 'flags': model_appearance.flag_policy(resolver.db['ModelFlags']),
                     'config': {'level': level, 'blood': blood, 'allAttachments': all_attachments},
                     'models': []}
    root = ex.node({'name': res.get('model') or key, 'scale': list(_scale(res))})
    _add_model(ex, res, root, None)
    if not ex.meshes:
        raise ValueError('no mesh could be exported: ' + '; '.join(ex.log))
    model_appearance.default_scene(ex, ex.appearance)
    size = ex.write(out_path)
    return {'model': res.get('model'), 'bytes': size, 'meshes': len(ex.meshes),
            'joints': sum(len(s['joints']) for s in ex.skins),
            'clips': [a['name'] for a in ex.animations], 'warnings': ex.log}


def convert_all_units(out_dir, all_attachments=False, level=1, blood=False, table='Units'):
    """Every record of Units.lqr (or Buildings.lqr): one .glb per model plus index.json."""
    import resolve_model
    resolver = resolve_model.Resolver()
    units = sorted(resolver.db[table], key=lambda r: r.name)
    done, index = {}, []
    start = time.time()
    for unit in units:
        entry = {'unit': unit.name}
        try:
            rec = resolver.models.get(unit['Model'])
            if rec is None:
                raise ValueError('no model record')
            entry['model'] = rec.name
            if rec.name not in done:
                path = os.path.join(out_dir, rec.name + '.glb')
                try:
                    done[rec.name] = convert_model(resolver, rec.name, path, all_attachments, level, blood)
                    done[rec.name]['file'] = os.path.basename(path)
                except Exception as e:            # keep going, report per unit
                    done[rec.name] = {'error': '%s: %s' % (type(e).__name__, e)}
            entry.update(done[rec.name])
        except Exception as e:
            entry['error'] = '%s: %s' % (type(e).__name__, e)
        index.append(entry)
        status = 'ok   %4d KB %3d clips %s' % (entry['bytes'] // 1024, len(entry['clips']), entry['model']) \
            if 'file' in entry else 'FAIL ' + entry['error'][:90]
        print('%-34s %s' % (unit.name, status), flush=True)
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, 'index.json'), 'w') as f:
        json.dump(index, f, indent=1)
    ok = sum(1 for e in index if 'file' in e)
    warned = sum(1 for e in index if e.get('warnings'))
    print('\n%d of %d %s exported (%d unique models, %d with warnings) in %.0fs' % (
        ok, len(index), table.lower(), len(done), warned, time.time() - start))


def convert_files(mesh_specs, texture_path, out_path, skeleton_path=None):
    ex = Exporter()
    ctx = None
    if skeleton_path:
        skel = lsk.parse(open(skeleton_path, 'rb').read())
        joints, skin = ex.skeleton(skel)
        ctx = (skel, joints, skin)
    for spec in mesh_specs:
        path, _, weights_path = spec.partition('=')
        m = lms.parse(open(path, 'rb').read())
        if weights_path and ctx and m.skinned:
            ex.mesh(m, texture_path, weights_path, ctx[2], len(ctx[0].joints))
        else:
            parent = ex.bone_frame(ctx, ctx[0].joint_index(m.attach)) if ctx and m.attach else None
            ex.mesh(m, texture_path, parent=parent)
    print('wrote %s: %d bytes, %d animations' % (out_path, ex.write(out_path), len(ex.animations)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest='mode', required=True)
    p = sub.add_parser('model')
    p.add_argument('name', help='UNIT_..., MODEL_..., Models id or .lmd path')
    p.add_argument('--out', required=True)
    p.add_argument('--all-attachments', action='store_true')
    p.add_argument('--level', type=int, choices=range(1, 5), default=1, help='preview gear level (default: 1)')
    p.add_argument('--blood', action='store_true', help='include state-enabled blood overlays')
    for mode in ('units', 'buildings'):
        p = sub.add_parser(mode)
        p.add_argument('--out-dir', required=True)
        p.add_argument('--all-attachments', action='store_true')
        p.add_argument('--level', type=int, choices=range(1, 5), default=1, help='preview gear level (default: 1)')
        p.add_argument('--blood', action='store_true', help='include state-enabled blood overlays')
    p = sub.add_parser('files')
    p.add_argument('meshes', nargs='+', help='mesh.lms or mesh.lms=weights.lwt')
    p.add_argument('--texture', required=True)
    p.add_argument('--skeleton')
    p.add_argument('--out', required=True)
    a = ap.parse_args()
    if a.mode == 'model':
        import resolve_model
        info = convert_model(resolve_model.Resolver(), a.name, a.out, a.all_attachments, a.level, a.blood)
        print('wrote %s: %d bytes, %d meshes, %d joints, %d clips' % (
            a.out, info['bytes'], info['meshes'], info['joints'], len(info['clips'])))
        for w in info['warnings']:
            print('warning:', w)
    elif a.mode in ('units', 'buildings'):
        convert_all_units(a.out_dir, a.all_attachments, a.level, a.blood, a.mode.capitalize())
    else:
        convert_files(a.meshes, a.texture, a.out, a.skeleton)
    return 0


if __name__ == '__main__':
    sys.exit(main())
