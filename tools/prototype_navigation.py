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


def clip_polygon(polygon, value):
    """Clip a convex polygon to value(point) >= 0, including its boundary."""
    result = []
    for a, b in zip(polygon, polygon[1:]+polygon[:1]):
        u, v = value(a), value(b)
        if u >= 0:
            result.append(a)
        if (u < 0) != (v < 0):
            t = u/(u-v)
            result.append((a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t))
    return result


def polygon_area(polygon):
    # Local coordinates avoid cancellation on maps far from the origin.
    if not polygon:
        return 0.
    x, z = polygon[0]
    return abs(sum((a[0]-x)*(b[1]-z)-(b[0]-x)*(a[1]-z)
                   for a, b in zip(polygon, polygon[1:]+polygon[:1])))/2


def covers_body(a, b, triangles, radius=.35, tolerance=.15):
    """Prove a swept square body is supported by the height-matched mesh union.

    Graph edges are axis aligned (or vertical). Subtract each compatible
    triangle from the remaining rectangle, so overlapping triangles cannot
    hide holes. The square conservatively encloses the circular unit body.
    Height clipping rejects support from a different floor or a sharp hump.
    """
    if a[0] != b[0] and a[1] != b[1]:
        raise ValueError('Body clearance requires an axis-aligned graph edge')
    if a[:2] == b[:2] and abs(a[2]-b[2]) > tolerance:
        return False
    xmin, xmax = min(a[0], b[0])-radius, max(a[0], b[0])+radius
    zmin, zmax = min(a[1], b[1])-radius, max(a[1], b[1])+radius
    remaining = [[(xmin, zmin), (xmax, zmin), (xmax, zmax), (xmin, zmax)]]
    dx, dz = b[0]-a[0], b[1]-a[1]
    length_squared = dx*dx+dz*dz
    reference = next((t for t in triangles if (y := height_at(a[0], a[1], t)) is not None
                      and abs(y-a[2]) <= tolerance), None)
    if reference is None:
        return False
    def plane(p):
        return sum(w*v[2] for w, v in zip(barycentric(*p, reference), reference))
    # Preserve the source cross-slope while fitting the edge's two endpoint
    # heights. A stationary body follows the local tangent plane.
    correction = b[2]-plane(b[:2])-(a[2]-plane(a[:2]))
    def expected(p):
        t = ((p[0]-a[0])*dx+(p[1]-a[1])*dz)/length_squared if length_squared else 0
        return plane(p)+(a[2]-plane(a[:2]))+correction*t
    for triangle in triangles:
        if (max(p[0] for p in triangle) < xmin or min(p[0] for p in triangle) > xmax
                or max(p[1] for p in triangle) < zmin or min(p[1] for p in triangle) > zmax):
            continue
        if barycentric(a[0], a[1], triangle) is None:
            continue
        def difference(p):
            return sum(w*v[2] for w, v in zip(barycentric(*p, triangle), triangle))-expected(p)
        support = [tuple(p[:2]) for p in triangle]
        support = clip_polygon(support, lambda p: tolerance-difference(p))
        support = clip_polygon(support, lambda p: tolerance+difference(p))
        if polygon_area(support) < 1e-12:
            continue
        signed = sum(p[0]*q[1]-q[0]*p[1] for p, q in zip(support, support[1:]+support[:1]))
        if signed < 0:
            support.reverse()
        uncovered = []
        for polygon in remaining:
            inside = polygon
            for p, q in zip(support, support[1:]+support[:1]):
                def side(v):
                    return (q[0]-p[0])*(v[1]-p[1])-(q[1]-p[1])*(v[0]-p[0])
                outside = clip_polygon(inside, lambda v: -side(v))
                if polygon_area(outside) > 1e-10:
                    uncovered.append(outside)
                inside = clip_polygon(inside, side)
                if not inside:
                    break
        remaining = uncovered
        if not remaining:
            return True
    return False


def terrain_height(x, z, width, spacing, heights):
    ix, iz = int(x // spacing), int(z // spacing)
    dx, dz = x/spacing-ix, z/spacing-iz
    a, b = heights[iz*(width+1)+ix:iz*(width+1)+ix+2]
    c, d = heights[(iz+1)*(width+1)+ix:(iz+1)*(width+1)+ix+2]
    return a+(b-a)*dx+(c-a)*dz if dx+dz <= 1 else d+(c-d)*(1-dx)+(b-d)*(1-dz)


def build(width, height, spacing, allowed, heights, surfaces, footprints=()):
    """surfaces: name/triangles in world space. Build deterministic cell graph."""
    from prototype_obstacles import blocked, RADIUS
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
                if not covers_body((x, z, y/1000), (x, z, y/1000), surface['triangles'], RADIUS):
                    continue
                if blocked((x, z, y/1000), (x, z, y/1000), footprints, spacing):
                    continue
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
                if (covers_segment((x, z, y), other, triangles)
                        and covers_body((x, z, y), other, triangles, RADIUS)
                        and not blocked((x, z, y), other, footprints, spacing)):
                    node['links'].append(base+j); nodes[j]['links'].append(base+i)
    for node in nodes:
        node['links'].sort()
    return {'surface_nodes': nodes, 'surface_report': reports,
            'surface_policy': 'Separate mesh layers at grid centers; continuous triangle coverage and 350 mm swept-square body support within 150 mm of interpolated height, <=1 cell rise per edge, <=250 mm terrain joins. Narrower-than-grid routes may be omitted.',
            'surface_clearance_radius_mm': round(RADIUS*1000)}


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
