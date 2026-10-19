"""Mesh path lookup category matrices, version 100.

Destination-major matrices select a source triangle edge (0..2); code 3
means unreachable. Triangle flag bit 1 excludes triangles before indexing.
"""
from pathlib import Path

import lms
import wor
from world_scene import version


def parse(data):
    r = wor.Reader(data)
    categories = []
    for _ in range(r.take('I')):
        version(r, [100])
        category, size, allocated_words, logical_bytes = r.take('4I')
        if logical_bytes != size * size // 4 + 1:
            raise ValueError('mesh path lookup byte size does not match the square matrix')
        if allocated_words != (logical_bytes + 3) // 4:
            raise ValueError('mesh path lookup word size mismatch')
        packed = r.read(allocated_words * 4)
        codes = [(packed[i // 4] >> ((i % 4) * 2)) & 3 for i in range(size * size)]
        categories.append({'category': category, 'matrix_size': size,
                           'allocated_words': allocated_words, 'logical_bytes': logical_bytes,
                           'packed_hex': packed.hex(), 'codes': codes})
    if r.pos != len(data):
        raise ValueError('trailing mesh path lookup bytes')
    return {'version': 1, 'categories': categories}


def code(category, source, destination):
    size = category['matrix_size']
    if not 0 <= source < size or not 0 <= destination < size:
        raise ValueError('mesh path triangle index out of range')
    return category['codes'][destination * size + source]


def graph(mesh):
    if len(mesh.triangle_flags) != len(mesh.triangles):
        raise ValueError('navigation requires decoded LMS triangle flags')
    source = [i for i, flags in enumerate(mesh.triangle_flags) if not flags & 2]
    triangles = [[mesh.pos_index[v] for v in mesh.triangles[i]] for i in source]
    neighbors = [[None] * 3 for _ in triangles]
    edges = {}
    for tid, triangle in enumerate(triangles):
        for eid in range(3):
            edge = tuple(sorted((triangle[eid], triangle[(eid + 1) % 3])))
            edges.setdefault(edge, []).append((tid, eid))
    for members in edges.values():
        if len(members) > 2:
            raise ValueError('non-manifold navigation edge')
        if len(members) == 2:
            (a, ea), (b, eb) = members
            neighbors[a][ea], neighbors[b][eb] = b, a
    return {'source_triangles': source, 'positions': mesh.positions,
            'triangles': triangles, 'neighbors': neighbors,
            'excluded_triangles': len(mesh.triangles) - len(source)}


def route(category, navigation, source, destination):
    """Return compact triangle IDs, or None for a stored unreachable pair."""
    step = code(category, source, destination)
    if source != destination and step == 3:
        return None
    path, visited = [source], {source}
    while source != destination:
        step = code(category, source, destination)
        if step == 3:
            raise ValueError('mesh path becomes unreachable mid-route')
        source = navigation['neighbors'][source][step]
        if source is None or source in visited:
            raise ValueError('mesh path selects a boundary edge or cycle')
        visited.add(source); path.append(source)
    return path


def verify(lookup, navigation):
    reports = []
    size = len(navigation['triangles'])
    for category in lookup['categories']:
        if category['matrix_size'] != size:
            raise ValueError('mesh path matrix does not match filtered triangle count')
        reached = unreachable = max_hops = 0
        for destination in range(size):
            # Memoize the functional graph for each destination: O(N^2).
            hops = {destination: 0}
            for source in range(size):
                if source != destination and code(category, source, destination) == 3:
                    hops[source] = None
            for source in range(size):
                current, pending, visited = source, [], set()
                while current not in hops:
                    if current in visited:
                        raise ValueError('mesh path matrix contains a routing cycle')
                    visited.add(current); pending.append(current)
                    current = navigation['neighbors'][current][code(category, current, destination)]
                    if current is None:
                        raise ValueError('mesh path matrix selects a boundary edge')
                distance = hops[current]
                if distance is None and pending:
                    raise ValueError('mesh path matrix routes into an unreachable triangle')
                for triangle in reversed(pending):
                    distance += 1; hops[triangle] = distance
                if hops[source] is None:
                    unreachable += 1
                else:
                    reached += 1; max_hops = max(max_hops, hops[source])
        reports.append({'category': category['category'], 'reachable_pairs': reached,
                        'unreachable_pairs': unreachable, 'max_hops': max_hops})
    return reports


def load_resource(path, resolver, *, strict=True):
    path = Path(path)
    mesh_path = resolver.index.find_basename('Meshes', path.stem + '.lms')
    if not mesh_path:
        raise ValueError('mesh path lookup has no unique matching LMS: ' + path.name)
    lookup = parse(path.read_bytes())
    navigation = graph(lms.parse(Path(mesh_path).read_bytes(), read_footer=True))
    try:
        verification = verify(lookup, navigation)
        lookup['stored_routes_valid'] = True
    except ValueError as error:
        if strict:
            raise
        lookup['stored_routes_valid'] = False
        lookup['stored_route_error'] = str(error)
        verification = None
    lookup.update(navigation=navigation, mesh_source=resolver.index.show(mesh_path),
                  indexing='destination * size + source',
                  edge_vertices=[[0, 1], [1, 2], [2, 0]], unreachable_code=3,
                  verification=verification)
    return lookup
