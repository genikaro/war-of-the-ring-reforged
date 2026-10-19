"""Clip static decal rectangles against the source terrain triangles.

The snapshot uses saved transforms/opacity and the definition's initial size.
Timed resizing/fading and projection onto moving objects belong to runtime.
"""
import math
import struct

from lms2gltf import to_gltf_axes, srgb_to_linear
import wor


def clip(vertices, axis, bound, keep_greater):
    result = []
    if not vertices:
        return result
    previous = vertices[-1]
    for current in vertices:
        a, b = previous[axis] - bound, current[axis] - bound
        inside_a = a >= 0 if keep_greater else a <= 0
        inside_b = b >= 0 if keep_greater else b <= 0
        if inside_a != inside_b:
            t = a / (a - b)
            result.append([p + (c - p) * t for p, c in zip(previous, current)])
        if inside_b:
            result.append(current)
        previous = current
    return result


def project(world, decal):
    width, height = decal['width'], decal['height']
    if width <= 0 or height <= 0:
        raise ValueError('static decal has non-positive dimensions')
    gx, gy = world.metadata['terrain']['grid']
    spacing = world.metadata['terrain']['cell_size']
    cx, cy = decal['position'][:2]
    angle = math.radians(decal['angles_degrees'][2])
    c, s = math.cos(angle), math.sin(angle)
    ex, ey = (abs(c) * width + abs(s) * height) / 2, (abs(s) * width + abs(c) * height) / 2
    xmin, xmax = max(0, math.floor((cx - ex) / spacing)), min(gx - 2, math.floor((cx + ex) / spacing))
    ymin, ymax = max(0, math.floor((cy - ey) / spacing)), min(gy - 2, math.floor((cy + ey) / spacing))
    color = decal['color_argb']
    tint = [srgb_to_linear(((color >> bit) & 255) / 255) for bit in (16, 8, 0)]
    alpha = decal['alpha'] * decal['definition_alpha'] * ((color >> 24) & 255) / 255
    positions, normals, uvs, colors, indices = [], [], [], [], []

    def vertex(x, y):
        idx = y * gx + x
        diffuse = struct.unpack_from('<I', world.vertices, idx * 20 + 4)[0]
        rgb = [srgb_to_linear(((diffuse >> bit) & 255) / 255) for bit in (16, 8, 0)] if decal['blends_with_terrain'] else [1, 1, 1]
        dx, dy = x * spacing - cx, y * spacing - cy
        return [x * spacing, y * spacing, world.heights[idx],
                (c * dx + s * dy) / width + 0.5,
                (-s * dx + c * dy) / height + 0.5,
                *[v * t for v, t in zip(rgb, tint)], max(0, min(1, alpha))]

    for y in range(ymin, ymax + 1):
        for x in range(xmin, xmax + 1):
            a, b, c0, d = vertex(x, y), vertex(x + 1, y), vertex(x, y + 1), vertex(x + 1, y + 1)
            for triangle in ([a, b, c0], [b, d, c0]):
                polygon = triangle
                for axis, bound, greater in [(3, 0, True), (3, 1, False), (4, 0, True), (4, 1, False)]:
                    polygon = clip(polygon, axis, bound, greater)
                if len(polygon) < 3:
                    continue
                # Source is Z-up; the glTF basis reflects X and swaps Y/Z.
                p, q, r = triangle
                u, v = [q[i] - p[i] for i in range(3)], [r[i] - p[i] for i in range(3)]
                normal = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
                length = math.sqrt(sum(n*n for n in normal))
                normal = to_gltf_axes([n / length for n in normal])
                base = len(positions)
                for p in polygon:
                    positions.append(to_gltf_axes((p[0], p[1], p[2] + 0.015)))
                    normals.append(normal); uvs.append(p[3:5]); colors.append(p[5:9])
                for i in range(1, len(polygon) - 1):
                    # Clipping through a source vertex can duplicate a point.
                    p, q, r = polygon[0], polygon[i], polygon[i + 1]
                    if abs((q[0]-p[0])*(r[1]-p[1]) - (q[1]-p[1])*(r[0]-p[0])) > 1e-10:
                        indices.extend([base, base + i, base + i + 1])
    return {'positions': positions, 'normals': normals, 'uvs': uvs, 'colors': colors, 'indices': indices}


def export(ex, scene, resolver):
    report, materials = [], {}
    for decal in scene['decals']:
        surface = project(scene['world'], decal)
        if not surface['indices']:
            report.append({'name': decal['name'], 'source': decal['source'], 'triangles': 0, 'outside_terrain': True})
            continue
        rid = decal['texture_id']
        if rid not in materials:
            entry = resolver.texture(rid)
            if not entry or not entry['exists']:
                raise ValueError('missing decal texture %d' % rid)
            texture, _ = ex.texture(str(wor.ROOT / entry['file']))
            materials[rid] = len(ex.materials)
            ex.materials.append({'name': decal['name'], 'alphaMode': 'BLEND',
                                  'pbrMetallicRoughness': {'baseColorTexture': {'index': texture},
                                                          'metallicFactor': 0, 'roughnessFactor': 1}})
        attributes = {name: ex.accessor(surface[key], fmt, kind, 5126, 34962, minmax=name == 'POSITION')
                      for name, key, fmt, kind in [('POSITION', 'positions', '3f', 'VEC3'),
                          ('NORMAL', 'normals', '3f', 'VEC3'), ('TEXCOORD_0', 'uvs', '2f', 'VEC2'),
                          ('COLOR_0', 'colors', '4f', 'VEC4')]}
        ex.meshes.append({'name': decal['name'], 'primitives': [{'attributes': attributes,
                          'indices': ex.accessor(surface['indices'], 'I', 'SCALAR', 5125, 34963),
                          'material': materials[rid]}]})
        ex.node({'name': decal['name'], 'mesh': len(ex.meshes) - 1,
                 'extras': {'wotrDecalId': decal['decal_id'], 'wotrDecalSource': decal['source'],
                            'wotrParentObjectId': decal['object_id']}})
        report.append({'name': decal['name'], 'source': decal['source'], 'triangles': len(surface['indices']) // 3})
    return report
