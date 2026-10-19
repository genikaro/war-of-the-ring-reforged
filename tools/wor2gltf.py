"""Export a decoded WOR map as a portable glTF scene with shared model meshes.

python tools/wor2gltf.py "extracted/Levels/Helms Deep.wor" --out-dir converted/maps/helms-deep
"""
import argparse
from collections import defaultdict
import copy
import json
import math
from pathlib import Path
import struct

import numpy as np

from lms2gltf import Exporter, convert_model, to_gltf_axes, to_gltf_quat, srgb_to_linear
from resolve_model import Resolver
import world_scene
import wor
import terrain_set
import mpl
import map_decals


def load_glb(path):
    data = Path(path).read_bytes()
    magic, ver, length = struct.unpack_from('<3I', data)
    if (magic, ver, length) != (0x46546c67, 2, len(data)):
        raise ValueError('invalid cached GLB')
    size, kind = struct.unpack_from('<2I', data, 12)
    if kind != 0x4e4f534a:
        raise ValueError('GLB JSON chunk missing')
    gltf = json.loads(data[20:20 + size])
    bsize, bkind = struct.unpack_from('<2I', data, 20 + size)
    if bkind != 0x004e4942:
        raise ValueError('GLB binary chunk missing')
    return gltf, data[28 + size:28 + size + bsize]


def import_model(ex, path):
    """Import geometry/images once. Node/skin hierarchies are cloned per placement."""
    src, binary = load_glb(path)
    while len(ex.bin) % 4:
        ex.bin.append(0)
    byte_offset = len(ex.bin)
    ex.bin.extend(binary)
    view_offset, accessor_offset = len(ex.views), len(ex.accessors)
    texture_offset, image_offset = len(ex.textures), len(ex.images)
    material_offset, mesh_offset = len(ex.materials), len(ex.meshes)
    for view in src['bufferViews']:
        view = copy.deepcopy(view); view['buffer'] = 0
        view['byteOffset'] = view.get('byteOffset', 0) + byte_offset
        ex.views.append(view)
    for acc in src['accessors']:
        acc = copy.deepcopy(acc); acc['bufferView'] += view_offset
        ex.accessors.append(acc)
    for img in src.get('images', []):
        img = copy.deepcopy(img); img['bufferView'] += view_offset
        ex.images.append(img)
    for tex in src.get('textures', []):
        tex = copy.deepcopy(tex); tex['source'] += image_offset; tex['sampler'] = 0
        ex.textures.append(tex)
    for mat in src.get('materials', []):
        mat = copy.deepcopy(mat)
        for container in [mat, mat.get('pbrMetallicRoughness', {})]:
            for key, value in container.items():
                if key.endswith('Texture'):
                    value['index'] += texture_offset
        ex.materials.append(mat)
    for mesh in src['meshes']:
        mesh = copy.deepcopy(mesh)
        for prim in mesh['primitives']:
            prim['attributes'] = {k: v + accessor_offset for k, v in prim['attributes'].items()}
            prim['indices'] += accessor_offset
            if 'material' in prim:
                prim['material'] += material_offset
        ex.meshes.append(mesh)
    return {'source': src, 'mesh_offset': mesh_offset, 'accessor_offset': accessor_offset}


def instance_model(ex, template, parent):
    src = template['source']
    node_offset, skin_offset = len(ex.nodes), len(ex.skins)
    for node in src['nodes']:
        node = copy.deepcopy(node)
        if 'mesh' in node:
            node['mesh'] += template['mesh_offset']
        if 'skin' in node:
            node['skin'] += skin_offset
        if 'children' in node:
            node['children'] = [n + node_offset for n in node['children']]
        ex.nodes.append(node)
    for skin in src.get('skins', []):
        skin = copy.deepcopy(skin)
        skin['joints'] = [n + node_offset for n in skin['joints']]
        if 'skeleton' in skin:
            skin['skeleton'] += node_offset
        skin['inverseBindMatrices'] += template['accessor_offset']
        ex.skins.append(skin)
    children = []
    for n in src['scenes'][src.get('scene', 0)]['nodes']:
        if 'skin' in src['nodes'][n]:
            # Skinning uses world-space joints under the placed model root.
            # glTF requires the skin mesh itself to remain a scene root.
            ex.scene.append(n + node_offset)
        else:
            children.append(n + node_offset)
    ex.nodes[parent]['children'] = children


def rotation(angles):
    """Source Euler rotations, XYZ order, degrees, mapped by the model export basis."""
    x, y, z = [math.radians(v) / 2 for v in angles]
    sx, sy, sz = math.sin(x), math.sin(y), math.sin(z)
    cx, cy, cz = math.cos(x), math.cos(y), math.cos(z)
    return list(to_gltf_quat((cx*cy*cz + sx*sy*sz,
                              sx*cy*cz - cx*sy*sz,
                              cx*sy*cz + sx*cy*sz,
                              cx*cy*sz - sx*sy*cz)))


def terrain_mesh(ex, scene, resolver):
    world = scene['world']; t = world.metadata['terrain']
    gx, gy = t['grid']; spacing = t['cell_size']
    vertices = list(struct.iter_unpack('<f4I', world.vertices))
    heights = np.asarray(world.heights).reshape(gy, gx)
    dy, dx = np.gradient(heights, spacing)
    normals = np.stack((dx, np.ones_like(dx), -dy), axis=-1)
    normals /= np.linalg.norm(normals, axis=-1, keepdims=True)
    groups = defaultdict(lambda: {'positions': [], 'normals': [], 'colors': [], 'uvs': [], 'indices': []})
    layers = scene['terrain']['layers']
    if not layers or not layers[0]['enabled']:
        raise ValueError('map has no enabled primary texture layer')
    if any(l['enabled'] and any(i != -1 for i in l['texture_ids']) for l in layers[1:]):
        raise ValueError('additional painted terrain layers need a compositing shader')
    layer = layers[0]; tx, ty = layer['tile_cells']; nx, ny = layer['grid']
    coords = scene['terrain']['coordinate_sets'][0]
    if coords['tile_cells'] != [tx, ty] or coords['channels'] != 2:
        raise ValueError('unsupported primary terrain UV layout')
    for y in range(ny):
        for x in range(nx):
            texture_id = layer['texture_ids'][y * nx + x]
            if texture_id < 0:
                raise ValueError('primary terrain tile has no texture')
            group = groups[texture_id]; base = len(group['positions'])
            width, height = min(tx, gx - 1 - x * tx), min(ty, gy - 1 - y * ty)
            for j in range(height + 1):
                for i in range(width + 1):
                    vx, vy = x * tx + i, y * ty + j
                    h, color, *_ = vertices[vy * gx + vx]
                    group['positions'].append(list(to_gltf_axes((vx * spacing, vy * spacing, h))))
                    group['normals'].append(normals[vy, vx].tolist())
                    group['colors'].append([srgb_to_linear(((color >> shift) & 255) / 255) for shift in (16, 8, 0)])
                    group['uvs'].append(coords['values'][j * (tx + 1) + i])
            for j in range(height):
                for i in range(width):
                    a = base + j * (width + 1) + i
                    b, c, d = a + 1, a + width + 1, a + width + 2
                    group['indices'].extend([a, b, c, b, d, c])
    primitives = []
    for rid, group in sorted(groups.items()):
        entry = resolver.texture(rid)
        if not entry or not entry['exists']:
            raise ValueError('missing terrain texture %d' % rid)
        mat = ex.material(str(wor.ROOT / entry['file']))
        # Primary transition tiles are opaque; alpha is not a cutout mask.
        ex.materials[mat].pop('alphaMode', None)
        attributes = {name: ex.accessor(group[key], fmt, kind, 5126, 34962, minmax=name == 'POSITION')
                      for name, key, fmt, kind in [('POSITION', 'positions', '3f', 'VEC3'),
                         ('NORMAL', 'normals', '3f', 'VEC3'), ('COLOR_0', 'colors', '3f', 'VEC3'),
                         ('TEXCOORD_0', 'uvs', '2f', 'VEC2')]}
        primitives.append({'attributes': attributes, 'indices': ex.accessor(group['indices'], 'I', 'SCALAR', 5125, 34963), 'material': mat})
    ex.meshes.append({'name': 'Terrain', 'primitives': primitives})
    ex.node({'name': 'Terrain', 'mesh': len(ex.meshes) - 1})
    return {'vertices': gx * gy, 'triangles': (gx - 1) * (gy - 1) * 2,
            'tiles': nx * ny, 'textures': len(groups)}


def water_meshes(ex, scene):
    report = []
    for lake in scene['water']['lakes']:
        surf = lake['surface']
        if not surf:
            continue
        gx, gy = surf['grid']; spacing = surf['cell_size']; ox, oy, oz, _ = surf['offset']
        vertices = surf['vertices']
        indices = []
        # Lake cell visibility is stored on its top-left vertex (0x63c587).
        # Bit 0 excludes both triangles; bit 1 marks a boundary cell.
        for y in range(gy - 1):
            for x in range(gx - 1):
                a = y * gx + x; b, c, d = a + 1, a + gx, a + gx + 1
                if not vertices[a][3] & 1:
                    indices.extend([a, b, c, b, d, c])
        if not indices:
            raise ValueError('lake has no visible surface triangles')
        positions, colors = [], []
        for i, (h, color, _, flags, _) in enumerate(vertices):
            positions.append(list(to_gltf_axes((ox + (i % gx) * spacing, oy + (i // gx) * spacing, oz + h))))
            colors.append([srgb_to_linear(((color >> s) & 255) / 255) for s in (16, 8, 0)] + [((color >> 24) & 255) / 255])
        attributes = {'POSITION': ex.accessor(positions, '3f', 'VEC3', 5126, 34962, minmax=True),
                      'NORMAL': ex.accessor([[0, 1, 0]] * len(vertices), '3f', 'VEC3', 5126, 34962),
                      'COLOR_0': ex.accessor(colors, '4f', 'VEC4', 5126, 34962)}
        ex.materials.append({'name': lake['name'], 'pbrMetallicRoughness': {'metallicFactor': 0, 'roughnessFactor': 0.35},
                             'alphaMode': 'BLEND', 'doubleSided': True})
        ex.meshes.append({'name': lake['name'], 'primitives': [{'attributes': attributes,
                          'indices': ex.accessor(indices, 'I', 'SCALAR', 5125, 34963), 'material': len(ex.materials) - 1}]})
        ex.node({'name': lake['name'], 'mesh': len(ex.meshes) - 1, 'extras': {'wotrLakeId': lake['id']}})
        report.append({'name': lake['name'], 'vertices': len(vertices), 'triangles': len(indices) // 3})
    return report


def export(source, output, resolver=None):
    source, output = Path(source).resolve(), Path(output).resolve()
    if output.is_relative_to(wor.ROOT / 'TLOTR - War of the Ring') or output.is_relative_to(source.parent):
        raise ValueError('output must be outside the original install and source level folder')
    scene = world_scene.parse(source.read_bytes()); resolver = resolver or Resolver()
    counts = world_scene.resolve_objects(scene, resolver.db)
    world_scene.resolve_decals(scene, resolver.db)
    if None in counts:
        raise ValueError('unresolved placed model references')
    palette_record = resolver.db['TerrainSets'].get(scene['world'].metadata['terrain']['terrain_set_id'])
    palette_path = resolver.index.find('TerrainSets', palette_record['Filename']) if palette_record else None
    if not palette_path:
        raise ValueError('terrain palette resource is missing')
    scene['terrain_palette'] = terrain_set.parse(Path(palette_path).read_bytes())
    lookups = {}
    for obj in scene['placements']['objects']:
        entry = obj['mesh_path_lookup']
        if entry and entry['name'] not in lookups:
            path = resolver.index.find('MeshPathLookups', entry['file'])
            if not path:
                raise ValueError('mesh path lookup is missing: ' + entry['name'])
            lookups[entry['name']] = mpl.load_resource(path, resolver, strict=False)
    scene['mesh_path_lookups'] = lookups
    output.mkdir(parents=True, exist_ok=True)
    ex = Exporter()
    terrain_report = terrain_mesh(ex, scene, resolver)
    water_report = water_meshes(ex, scene)
    decal_report = map_decals.export(ex, scene, resolver)
    models, warnings, hidden_navigation = {}, [], []
    cache = output / 'models'; cache.mkdir(exist_ok=True)
    # Rebuild cache each export: it must match the current decoder and source files.
    for index, name in enumerate(sorted(counts)):
        target = cache / (name + '.glb')
        result = convert_model(resolver, name, str(target))
        models[name] = import_model(ex, target)
        warnings.extend(result['warnings'])
        print('model %d/%d: %s (%d placements)' % (index + 1, len(counts), name, counts[name]), flush=True)
    for obj in scene['placements']['objects']:
        sx, sy, sz, _ = obj['scale']
        node = ex.node({'name': obj['name'], 'translation': list(to_gltf_axes(obj['position'][:3])),
                        'scale': [sx, sz, sy], 'rotation': rotation(obj['angles_degrees']),
                        'extras': {'wotrObjectId': obj['object_id'], 'wotrOwner': obj['owner'],
                                   'wotrDatabaseName': obj['database_name'], 'wotrModel': obj['model']}})
        instance_model(ex, models[obj['model']], node)
        # A navigation helper has IsWalkable and no declared render material;
        # the Phase 1 converter supplies its own gray placeholder for inspection.
        if obj['is_walkable'] and not resolver.resolve(obj['model']).get('materials'):
            hidden_navigation.append(obj['name'])
            ex.nodes[node]['extras']['wotrNavigationSurface'] = True
            ex.nodes[node]['extras']['wotrNavigationRoots'] = ex.nodes[node].pop('children', [])
    summary = {'format_version': 1, 'source': str(source), 'source_sha256': scene['world'].metadata['sha256'],
               'name': scene['world'].metadata['name'], 'terrain': terrain_report,
               'placed_objects': len(scene['placements']['objects']), 'unique_models': len(models),
               'unresolved_objects': 0, 'other_placeables': len(scene['placements']['other_placeables']),
               'hidden_navigation_helpers': hidden_navigation,
               'water': water_report, 'pathing_categories': [c['name'] for c in scene['pathing']['categories']],
               'decals': decal_report,
               'mesh_path_lookups': len(lookups),
               'warnings': warnings, 'limitations': ['Static model poses; no map simulation.',
                    'Water uses the stored plane, painted surface and vertex opacity; animated ripple/reflection shaders are not reproduced.',
                    'Decals use saved transforms, opacity and initial size projected onto terrain; timed fades/resizing and projection onto objects need runtime.',
                    'World camera/light/marker payloads decode structurally; unnamed fields remain numerical.']}
    ex.extras = {'wotrWorld': summary}
    summary['glb_bytes'] = ex.write(str(output / 'scene.glb'))
    world = scene.pop('world')
    (output / 'terrain-vertices.bin').write_bytes(world.vertices)
    scene['metadata'] = world.metadata
    for filename, content in [('scene.json', scene), ('report.json', summary),
                              ('index.json', [{'unit': summary['name'], 'file': 'scene.glb', 'clips': []}])]:
        (output / filename).write_text(json.dumps(content, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    return summary


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--out-dir', required=True, type=Path)
    args = parser.parse_args()
    report = export(args.source, args.out_dir)
    print(json.dumps(report, indent=2))
