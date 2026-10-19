"""Prototype static footprints from root oriented-collision boxes.

The source layer is named by BBoxes.lqr. The vectors are interpreted here as
half-extents, center, quaternion (wxyz); this is a conservative runtime policy,
not a claim to reproduce the original executable's collision queries.
"""
import math

from prototype_navigation import transform, terrain_height

RADIUS = .35
BODY_HEIGHT = 1.8


def rotate(point, quaternion):
    w, x, y, z = quaternion
    length = math.sqrt(w*w+x*x+y*y+z*z)
    if not math.isfinite(length) or abs(length-1) > .01:
        raise ValueError('Obstacle box has non-unit quaternion')
    w, x, y, z = (v/length for v in quaternion)
    px, py, pz = point
    tx, ty, tz = 2*(y*pz-z*py), 2*(z*px-x*pz), 2*(x*py-y*px)
    return [px+w*tx+y*tz-z*ty, py+w*ty+z*tx-x*tz, pz+w*tz+x*ty-y*tx]


def hull(points):
    points = sorted(set(tuple(p[:2]) for p in points))
    def cross(a, b, c):
        return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    def chain(sequence):
        result = []
        for p in sequence:
            while len(result) > 1 and cross(result[-2], result[-1], p) <= 0:
                result.pop()
            result.append(p)
        return result[:-1]
    return chain(points)+chain(reversed(points))


def footprint(box, obj, model_scale=(1, 1, 1)):
    values = box['vectors']
    if box['joint'] != -1 or box['flag'] != 0:
        raise ValueError('Animated/flagged obstacle boxes need an explicit adapter')
    if len(values) != 12 or not all(math.isfinite(v) for v in values) or min(values[:3]) <= 0:
        raise ValueError('Invalid obstacle box dimensions')
    if values[3] != 1 or values[7] != 1:
        raise ValueError('Unknown obstacle box vector format')
    corners = []
    for x in [-values[0], values[0]]:
        for z in [-values[1], values[1]]:
            for y in [-values[2], values[2]]:
                p = rotate([x, z, y], values[8:])
                corners.append(transform([v+c for v, c in zip(p, values[4:7])], obj, model_scale))
    polygon = hull(corners)
    if len(polygon) < 3:
        raise ValueError('Obstacle has degenerate horizontal footprint')
    return {'object_id': obj['object_id'], 'name': obj['name'], 'polygon': polygon,
            'y_min': min(p[2] for p in corners), 'y_max': max(p[2] for p in corners)}


def placed_footprints(scene, resolver):
    import lmd
    import wor
    result = []
    layer = resolver.db['BBoxes'].by_name['BBOX_LAYER_ORIENTED_COLLISION'].id
    models = {}
    for obj in scene['placements']['objects']:
        record = resolver.db[obj['database_table']].get(obj['reference'])
        if obj['is_walkable'] or (obj['database_table'] != 'Buildings' and not record.get('IsPathingObstacle')):
            continue
        if obj['model'] not in models:
            resolved = resolver.resolve(obj['model'])
            model = lmd.parse((wor.ROOT / resolved['file']).read_bytes())
            models[obj['model']] = (resolved['scale'], [b for b in model.bounding_boxes if b['layer'] == layer])
        scale, boxes = models[obj['model']]
        if not boxes:
            raise ValueError('Pathing obstacle lacks an oriented collision box: '+obj['name'])
        result.extend(footprint(box, obj, scale) for box in boxes)
    return result


def intersects(polygon, xmin, xmax, zmin, zmax):
    """Convex polygon vs axis-aligned rectangle, including boundary contact."""
    cx, cz = (xmin+xmax)/2, (zmin+zmax)/2
    ex, ez = (xmax-xmin)/2, (zmax-zmin)/2
    axes = [(1, 0), (0, 1)]
    axes.extend((-(b[1]-a[1]), b[0]-a[0]) for a, b in zip(polygon, polygon[1:]+polygon[:1]))
    for x, z in axes:
        projected = [x*p[0]+z*p[1] for p in polygon]
        center, radius = cx*x+cz*z, ex*abs(x)+ez*abs(z)
        if max(projected) < center-radius or min(projected) > center+radius:
            return False
    return True


def blocked(a, b, footprints, spacing):
    """Conservative swept-cell/body envelope, preserving separate floors."""
    padding = spacing/2+RADIUS
    for item in footprints:
        if max(a[2], b[2])+BODY_HEIGHT <= item['y_min'] or min(a[2], b[2]) >= item['y_max']:
            continue
        if intersects(item['polygon'], min(a[0], b[0])-padding, max(a[0], b[0])+padding,
                      min(a[1], b[1])-padding, max(a[1], b[1])+padding):
            return True
    return False


def ground_mask(width, height, spacing, allowed, heights, footprints):
    result = bytearray(allowed)
    report = []
    padding = spacing/2+RADIUS
    for item in footprints:
        polygon = item['polygon']
        xmin = max(0, math.floor((min(p[0] for p in polygon)-padding)/spacing))
        xmax = min(width-1, math.floor((max(p[0] for p in polygon)+padding)/spacing))
        zmin = max(0, math.floor((min(p[1] for p in polygon)-padding)/spacing))
        zmax = min(height-1, math.floor((max(p[1] for p in polygon)+padding)/spacing))
        changed = 0
        for z in range(zmin, zmax+1):
            for x in range(xmin, xmax+1):
                cell = z*width+x
                if not result[cell]:
                    continue
                px, pz = (x+.5)*spacing, (z+.5)*spacing
                point = [px, pz, terrain_height(px, pz, width, spacing, heights)]
                if blocked(point, point, [item], spacing):
                    result[cell] = 0; changed += 1
        report.append({'object_id': item['object_id'], 'name': item['name'], 'ground_cells_blocked': changed})
    return bytes(result), {'placements': len(footprints), 'ground_cells_blocked': sum(allowed)-sum(result),
                          'objects': report, 'radius_m': RADIUS, 'body_height_m': BODY_HEIGHT,
                          'policy': 'Root oriented-collision boxes for Buildings and IsPathingObstacle objects; half-extents/center/wxyz pose, conservative projected swept-cell clearance. Static snapshot; original runtime collision formulas unverified.'}
