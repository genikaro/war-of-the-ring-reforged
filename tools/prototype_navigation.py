"""Conservative layered walker graph from placed navigation triangles.

Ground cells keep their original IDs. Surface cells append separate nodes,
usually at cell centers; terrain links require matching heights. Stored MPL
routing codes are unused.
Coordinates are source X/Y horizontal and Z up, in millimeters at runtime.
"""
import itertools
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

    Subtract each compatible triangle from the swept square's convex hull,
    so overlapping triangles cannot
    hide holes. The square conservatively encloses the circular unit body.
    Height clipping rejects support from a different floor or a sharp hump.
    """
    from prototype_obstacles import hull
    if a[:2] == b[:2] and abs(a[2]-b[2]) > tolerance:
        return False
    xmin, xmax = min(a[0], b[0])-radius, max(a[0], b[0])+radius
    zmin, zmax = min(a[1], b[1])-radius, max(a[1], b[1])+radius
    remaining = [hull([(p[0]+x*radius,p[1]+z*radius) for p in [a,b] for x in [-1,1] for z in [-1,1]])]
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


def node_point(node, width, spacing):
    """World position in meters; off-center nodes store integer millimeters."""
    cell = node['cell']
    return (node['x_mm']/1000 if 'x_mm' in node else (cell % width+.5)*spacing,
            node['z_mm']/1000 if 'z_mm' in node else (cell // width+.5)*spacing, node['height_mm']/1000)


# Searched nearest-first when a cell center cannot hold the body. The lattice
# stays inside the cell, so a node keeps its column for grid queries.
OFFSETS = sorted(((i/10, j/10) for i in range(-4, 5) for j in range(-4, 5) if i or j),
                 key=lambda o: (o[0]*o[0]+o[1]*o[1], o[1], o[0]))
NODE_SPACING = .7  # Two bodies fit at any pair of nodes on one floor.


def crowds(p, a, b):
    """A body standing at p would obstruct movement between a and b.

    Matches the simulation's reservation test: vertical ranges within 700 mm
    and a horizontal segment distance below 700 mm. Cell-center lattices
    never do this; off-center nodes must not either.
    """
    if p[2] <= min(a[2], b[2])-NODE_SPACING or p[2] >= max(a[2], b[2])+NODE_SPACING:
        return False
    dx, dz = b[0]-a[0], b[1]-a[1]
    length = dx*dx+dz*dz
    t = max(0, min(1, ((p[0]-a[0])*dx+(p[1]-a[1])*dz)/length)) if length else 0
    return math.hypot(p[0]-a[0]-t*dx, p[1]-a[1]-t*dz) < NODE_SPACING-1e-9


def build(width, height, spacing, allowed, heights, surfaces, footprints=()):
    """surfaces: name/triangles in world space. Build deterministic cell graph."""
    from prototype_obstacles import blocked, RADIUS
    nodes, triangles_by_node = [], []
    by_cell = defaultdict(list)
    reports = []
    def around(cell, reach):
        return [n for dz in range(-reach, reach+1) for dx in range(-reach, reach+1)
                if 0 <= cell % width+dx < width and 0 <= cell // width+dz < height
                for n in by_cell[cell+dz*width+dx]]
    def adjacent_cells(a, b):
        ca, cb = nodes[a]['cell'], nodes[b]['cell']
        return ca != cb and abs(ca % width-cb % width) <= 1 and abs(ca // width-cb // width) <= 1
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
        def add(cell, x, z, y, offset):
            by_cell[cell].append(len(nodes))
            nodes.append({'cell': cell, 'height_mm': y, 'links': [], 'surface': surface['name']})
            if offset:
                nodes[-1].update(x_mm=round(x*1000), z_mm=round(z*1000))
            triangles_by_node.append(surface['triangles'])
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
                add(cell, x, z, y, False)
        # Walkways narrower than 1.7 m can miss cell centers. Fill each
        # remaining floor with the nearest supported lattice point instead.
        def covered(x, z):
            return any(height_at(x, z, t) is not None for t in surface['triangles'])
        offsets = 0
        for cell, triangles in sorted(candidates.items()):
            cx, cz = (cell % width + .5)*spacing, (cell // width + .5)*spacing
            nearby = around(cell, 1)
            # Possible links of nodes up to two cells away can pass this cell.
            segments = [(node_point(nodes[a], width, spacing), node_point(nodes[b], width, spacing))
                        for a, b in itertools.combinations(around(cell, 2), 2) if adjacent_cells(a, b)]
            for dx, dz in OFFSETS:
                x, z = cx+dx*spacing, cz+dz*spacing
                for y in sorted({round(y*1000) for t in triangles if (y := height_at(x, z, t)) is not None}):
                    if any(abs(nodes[n]['height_mm']-y) < spacing*1000 for n in by_cell[cell]):
                        continue
                    if any(math.dist((x, z, y/1000), node_point(nodes[n], width, spacing)) < NODE_SPACING for n in nearby):
                        continue
                    if any(crowds((x, z, y/1000), a, b) for a, b in segments):
                        continue
                    if not all(covered(x+sx*RADIUS, z+sz*RADIUS) for sx in (-1, 1) for sz in (-1, 1)):
                        continue
                    if (covers_body((x, z, y/1000), (x, z, y/1000), surface['triangles'], RADIUS)
                            and not blocked((x, z, y/1000), (x, z, y/1000), footprints, spacing)):
                        add(cell, x, z, y, True); nearby.append(len(nodes)-1); offsets += 1
        reports.append({'name': surface['name'], 'triangles': len(surface['triangles']), 'nodes': len(nodes)-first,
                        'off_center_nodes': offsets})
    base = width*height
    def point(node):
        return node_point(nodes[node], width, spacing)
    for i, node in enumerate(nodes):
        cell = node['cell']; x, z, y = point(i)
        ground = terrain_height(x, z, width, spacing, heights)
        center = terrain_height((cell % width+.5)*spacing, (cell // width+.5)*spacing, width, spacing, heights)
        # Terrain links lead to the column's ground center.
        if allowed[cell] and abs(ground-y) <= .25 and abs(center-y) <= .25:
            node['links'].append(cell)
        adjacent = [cell+dz*width+dx for dz in (-1, 0, 1) for dx in (-1, 0, 1)
                    if 0 <= cell % width+dx < width and 0 <= cell // width+dz < height]
        for neighbor in adjacent:
            for j in by_cell[neighbor]:
                if j <= i:
                    continue
                other = point(j)
                if abs(y-other[2]) > spacing:
                    continue
                triangles = triangles_by_node[i]
                if triangles is not triangles_by_node[j]:
                    triangles = triangles + triangles_by_node[j]
                if any(crowds(point(k), (x, z, y), other) for k in around(cell, 2) if k != i and k != j):
                    continue
                if (covers_segment((x, z, y), other, triangles)
                        and covers_body((x, z, y), other, triangles, RADIUS)
                        and not blocked((x, z, y), other, footprints, spacing)):
                    node['links'].append(base+j); nodes[j]['links'].append(base+i)
    for node in nodes:
        node['links'].sort()
    return {'surface_nodes': nodes, 'surface_report': reports,
            'surface_policy': 'Eight-way separate mesh layers at grid centers, or the nearest supported 100 mm lattice point within 400 mm when a center cannot hold the body; off-center nodes stay 700 mm from nearby nodes and possible links, and no link passes within 700 mm of another node. Continuous triangle coverage and 350 mm swept-square body support within 150 mm of interpolated height, <=1 cell rise per edge, <=250 mm terrain joins at the node and its column center.',
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
