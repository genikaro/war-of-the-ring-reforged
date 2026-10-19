"""Conservative layered walker graph from placed navigation triangles.

Ground cells keep their original IDs. Surface cells append separate nodes;
terrain links require matching heights. Stored MPL routing codes are unused.
Coordinates are source X/Y horizontal and Z up, in millimeters at runtime.
"""
import math
from collections import defaultdict


def transform(point, obj, model_scale=(1, 1, 1)):
    value = [p * s * m for p, s, m in zip(point, obj['scale'], model_scale)]
    # Match wor2gltf.rotation: Rz * Ry * Rx, then placement translation.
    for axis, degrees in enumerate(obj['angles_degrees']):
        a, b = ((1, 2), (2, 0), (0, 1))[axis]
        c, s = math.cos(math.radians(degrees)), math.sin(math.radians(degrees))
        value[a], value[b] = c * value[a] - s * value[b], s * value[a] + c * value[b]
    return [v + p for v, p in zip(value, obj['position'])]


def barycentric(x, z, triangle):
    a, b, c = triangle
    denominator = (b[1]-c[1])*(a[0]-c[0]) + (c[0]-b[0])*(a[1]-c[1])
    if abs(denominator) < 1e-9:
        return None
    u = ((b[1]-c[1])*(x-c[0]) + (c[0]-b[0])*(z-c[1])) / denominator
    v = ((c[1]-a[1])*(x-c[0]) + (a[0]-c[0])*(z-c[1])) / denominator
    return u, v, 1-u-v


def height_at(x, z, triangle):
    weights = barycentric(x, z, triangle)
    if weights is None or min(weights) < -1e-7:
        return None
    return sum(w * v[2] for w, v in zip(weights, triangle))


def covers_segment(a, b, triangles, tolerance=0.15):
    """Prove continuous coverage, including height, using triangle intervals."""
    intervals = []
    for triangle in triangles:
        first, last = barycentric(a[0], a[1], triangle), barycentric(b[0], b[1], triangle)
        if first is None:
            continue
        lo, hi = 0., 1.
        for u, v in zip(first, last):
            delta = v-u
            if abs(delta) < 1e-12:
                if u < -1e-7:
                    hi = -1
            elif delta > 0:
                lo = max(lo, (-1e-7-u)/delta)
            else:
                hi = min(hi, (-1e-7-u)/delta)
        if lo > hi:
            continue
        for t in (lo, hi):
            weights = [u + (v-u)*t for u, v in zip(first, last)]
            surface = sum(w*p[2] for w, p in zip(weights, triangle))
            if abs(surface - (a[2]+(b[2]-a[2])*t)) > tolerance:
                break
        else:
            intervals.append((lo, hi))
    reached = 0.
    for lo, hi in sorted(intervals):
        if lo > reached + 1e-6:
            return False
        reached = max(reached, hi)
    return reached >= 1-1e-6


def terrain_height(x, z, width, spacing, heights):
    ix, iz = int(x // spacing), int(z // spacing)
    dx, dz = x/spacing-ix, z/spacing-iz
    a, b = heights[iz*(width+1)+ix:iz*(width+1)+ix+2]
    c, d = heights[(iz+1)*(width+1)+ix:(iz+1)*(width+1)+ix+2]
    return a+(b-a)*dx+(c-a)*dz if dx+dz <= 1 else d+(c-d)*(1-dx)+(b-d)*(1-dz)


def build(width, height, spacing, allowed, heights, surfaces):
    """surfaces: name/triangles in world space. Build deterministic cell graph."""
    nodes, triangles_by_node = [], []
    by_cell = defaultdict(list)
    reports = []
    for surface in surfaces:
        candidates = defaultdict(list)
        for triangle in surface['triangles']:
            xmin = max(0, math.floor(min(v[0] for v in triangle)/spacing))
            xmax = min(width-1, math.floor(max(v[0] for v in triangle)/spacing))
            zmin = max(0, math.floor(min(v[1] for v in triangle)/spacing))
            zmax = min(height-1, math.floor(max(v[1] for v in triangle)/spacing))
            for z in range(zmin, zmax+1):
                for x in range(xmin, xmax+1):
                    candidates[z*width+x].append(triangle)
        first = len(nodes)
        for cell, triangles in sorted(candidates.items()):
            x, z = (cell % width + .5)*spacing, (cell // width + .5)*spacing
            samples = sorted({round(y*1000) for t in triangles if (y := height_at(x, z, t)) is not None})
            for y in samples:
                # Retain genuinely stacked surfaces; merge rounding at shared edges.
                if any(abs(nodes[n]['height_mm']-y) <= 2 for n in by_cell[cell]):
                    continue
                node = len(nodes)
                by_cell[cell].append(node)
                nodes.append({'cell': cell, 'height_mm': y, 'links': [], 'surface': surface['name']})
                triangles_by_node.append(surface['triangles'])
        reports.append({'name': surface['name'], 'triangles': len(surface['triangles']), 'nodes': len(nodes)-first})
    base = width*height
    def point(node):
        cell = nodes[node]['cell']
        return ((cell % width+.5)*spacing, (cell // width+.5)*spacing, nodes[node]['height_mm']/1000)
    for i, node in enumerate(nodes):
        cell = node['cell']; x, z, y = point(i)
        ground = terrain_height(x, z, width, spacing, heights)
        if allowed[cell] and abs(ground-y) <= .25:
            node['links'].append(cell)
        adjacent = [cell] + ([cell-1] if cell % width else []) + ([cell-width] if cell >= width else [])
        for neighbor in adjacent:
            for j in by_cell[neighbor]:
                if j >= i:
                    continue
                other = point(j)
                if abs(y-other[2]) > spacing:
                    continue
                triangles = triangles_by_node[i]
                if triangles is not triangles_by_node[j]:
                    triangles = triangles + triangles_by_node[j]
                if covers_segment((x, z, y), other, triangles):
                    node['links'].append(base+j); nodes[j]['links'].append(base+i)
    for node in nodes:
        node['links'].sort()
    return {'surface_nodes': nodes, 'surface_report': reports,
            'surface_policy': 'Separate mesh layers at grid centers; continuous triangle coverage, <=1 cell rise per edge, <=250 mm terrain joins. Narrower-than-grid routes may be omitted.'}


def placed_surfaces(scene, resolver):
    import mpl
    surfaces = []
    for obj in scene['placements']['objects']:
        if not obj['is_walkable'] or not obj['mesh_path_lookup']:
            continue
        entry = obj['mesh_path_lookup']
        path = resolver.index.find('MeshPathLookups', entry['file'])
        if not path:
            raise ValueError('Missing walkable mesh lookup: '+entry['name'])
        resource = mpl.load_resource(path, resolver, strict=False)
        mesh = resource['navigation']
        scale = resolver.resolve(obj['model'])['scale']
        positions = [transform(p, obj, scale) for p in mesh['positions']]
        surfaces.append({'name': obj['name'], 'triangles': [[positions[i] for i in t] for t in mesh['triangles']]})
    return surfaces
