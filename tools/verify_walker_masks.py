"""Audit ground pathing bitsets against each map's original region graph.

python tools/verify_walker_masks.py
The walker graph's regions cover exactly the first ("painted") bitset, so it
is the runtime passability. The other two bitsets behave as editor inputs:
painted cells lie in mask_b or mask_c, unforced cells respect the category's
slope and water limits, and remaining mask_b exclusions sit in an outer frame.
"""
import argparse
from collections import Counter
import json
import math
from pathlib import Path

import prototype_navigation
import world_scene
import wor


def bits(bitset, count):
    if not bitset:
        return [False] * count
    return [bool((bitset['words'][i // 32] >> (i % 32)) & 1) for i in range(count)]


def lake_depths(scene, width, height, spacing):
    """Deepest visible lake water above each terrain cell center."""
    heights, depths = scene['world'].heights, {}
    for lake in scene['water']['lakes']:
        surface = lake['surface']
        if not surface:
            continue
        gx, gy = surface['grid']; step = surface['cell_size']; ox, oz, level, _ = surface['offset']
        for z in range(max(0, int(oz // spacing)), min(height - 1, int((oz + (gy - 1) * step) // spacing)) + 1):
            for x in range(max(0, int(ox // spacing)), min(width - 1, int((ox + (gx - 1) * step) // spacing)) + 1):
                px, pz = (x + .5) * spacing, (z + .5) * spacing
                u, v = math.floor((px - ox) / step), math.floor((pz - oz) / step)
                # Visibility is stored on the lake cell's top-left vertex.
                if not (0 <= u < gx - 1 and 0 <= v < gy - 1) or surface['vertices'][v * gx + u][3] & 1:
                    continue
                depth = level + surface['vertices'][v * gx + u][0] - prototype_navigation.terrain_height(px, pz, width, spacing, heights)
                depths[z * width + x] = max(depths.get(z * width + x, depth), depth)
    return depths


def components(passable, width, height, corners):
    """Eight-way labels; without corners a diagonal needs both flank cells."""
    labels = [-1] * len(passable)
    for start, value in enumerate(passable):
        if not value or labels[start] >= 0:
            continue
        labels[start] = start; stack = [start]
        while stack:
            cell = stack.pop(); x, z = cell % width, cell // width
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    nx, nz = x + dx, z + dz
                    if not (dx or dz) or not (0 <= nx < width and 0 <= nz < height):
                        continue
                    other = nz * width + nx
                    if not passable[other] or labels[other] >= 0:
                        continue
                    if dx and dz and not corners and not (passable[z * width + nx] and passable[nz * width + x]):
                        continue
                    labels[other] = start; stack.append(other)
    return labels


def audit_category(scene, category):
    width, height = category['grid']; count = width * height
    spacing = scene['world'].metadata['terrain']['cell_size']; heights = scene['world'].heights
    painted = bits(category['painted'], count)
    inputs, forced = bits(category.get('mask_b'), count), bits(category.get('mask_c'), count)
    depths = lake_depths(scene, width, height, spacing)
    limit = math.tan(math.radians(category['max_slope'])) * spacing

    def baked(cell):
        x, z = cell % width, cell // width
        a, b = heights[z * (width + 1) + x], heights[z * (width + 1) + x + 1]
        c, d = heights[(z + 1) * (width + 1) + x], heights[(z + 1) * (width + 1) + x + 1]
        # Steepest of the cell's two source terrain triangles.
        return (max(math.hypot(b - a, c - a), math.hypot(c - d, b - d)) <= limit + 1e-6
                and depths.get(cell, -1) <= category['max_water_depth'] + 1e-6)

    frame = [i for i, value in enumerate(painted) if value]
    xs, zs = [i % width for i in frame], [i // width for i in frame]
    unexplained = [i for i in range(count) if inputs[i] and not forced[i] and not painted[i] and baked(i)]
    result = {'name': category['name'], 'grid': [width, height], 'max_slope_degrees': category['max_slope'],
              'max_water_depth': category['max_water_depth'], 'painted': len(frame),
              'mask_b': sum(inputs), 'mask_c': sum(forced),
              'painted_outside_mask_b_or_c': sum(p and not (b or f) for p, b, f in zip(painted, inputs, forced)),
              'mask_c_unpainted': sum(f and not p for p, f in zip(painted, forced)),
              'unforced_painted_over_slope_or_water': sum(p and not f and not baked(i) for i, (p, f) in enumerate(zip(painted, forced))),
              'mask_b_baked_but_unpainted': len(unexplained),
              'mask_b_baked_but_unpainted_inside_painted_bounds': sum(
                  min(xs) <= i % width <= max(xs) and min(zs) <= i // width <= max(zs) for i in unexplained) if frame else 0}
    graph = category.get('graph')
    if not graph:
        return result
    regions = world_scene.region_cells(graph)
    cells = Counter(r for r in regions if r >= 0)
    bounds = {}
    for i, r in enumerate(regions):
        if r >= 0:
            x, z = i % width, i // width
            box = bounds.setdefault(r, [x, x, z, z])
            box[:] = [min(box[0], x), max(box[1], x), min(box[2], z), max(box[3], z)]
    orthogonal, diagonal = set(), set()
    for i, r in enumerate(regions):
        if r < 0:
            continue
        x, z = i % width, i // width
        for dx, dz in [(1, 0), (0, 1), (1, 1), (-1, 1)]:
            if 0 <= x + dx < width and z + dz < height and regions[(z + dz) * width + x + dx] not in (-1, r):
                (diagonal if dx and dz else orthogonal).add(tuple(sorted((r, regions[(z + dz) * width + x + dx]))))
    links = {tuple(sorted((i, link['header'][0]))) for i, region in enumerate(graph['regions']) for link in region['links']}
    with_corners = components(painted, width, height, True)
    without_corners = components(painted, width, height, False)
    main = Counter(v for v in with_corners if v >= 0).most_common(1)[0][0] if frame else -1
    kept = Counter(without_corners[i] for i in range(count) if with_corners[i] == main).most_common(1)[0][0] if frame else -1
    result.update(regions=len(graph['regions']), region_cells=sum(cells.values()),
                  region_mismatches=sum((r >= 0) != p for r, p in zip(regions, painted)),
                  region_records_match_cells=all(region['raw_words'][1] == cells[i] and region['raw_words'][2:6] == bounds.get(i)
                                                 for i, region in enumerate(graph['regions'])),
                  links=len(links), links_match_eight_way_adjacency=links == orthogonal | diagonal,
                  corner_only_links=len(diagonal - orthogonal),
                  components_with_corner_diagonals=len({v for v in with_corners if v >= 0}),
                  components_without_corner_diagonals=len({v for v in without_corners if v >= 0}),
                  main_cells_cut_without_corner_diagonals=sum(with_corners[i] == main and without_corners[i] != kept for i in range(count)))
    return result


def audit(output):
    output = Path(output).resolve()
    if output.is_relative_to(wor.ROOT / 'TLOTR - War of the Ring') or output.is_relative_to(wor.ROOT / 'extracted'):
        raise ValueError('audit output must be outside source folders')
    maps = []
    for path in sorted((wor.ROOT / 'extracted/Levels').rglob('*.wor')):
        scene = world_scene.parse(path.read_bytes())
        maps.append({'source': path.relative_to(wor.ROOT).as_posix(),
                     'categories': [audit_category(scene, c) for c in scene['pathing']['categories']]})
    walkers = [c for m in maps for c in m['categories'] if c['name'] == 'PATHING_CATEGORY_WALKER']
    every = [c for m in maps for c in m['categories']]
    result = {'format_version': 1, 'maps': maps, 'map_count': len(maps),
              'walker_regions_match_painted': len(walkers) == len(maps) and all(
                  c.get('region_mismatches') == 0 and c['region_records_match_cells'] for c in walkers),
              'walker_links_are_eight_way': all(c.get('links_match_eight_way_adjacency') for c in walkers),
              'painted_within_editor_inputs': all(c['painted_outside_mask_b_or_c'] == 0 for c in every),
              'maps_with_exact_slope_water_bake': sum(c['unforced_painted_over_slope_or_water'] == 0 for c in walkers),
              'interpretation': 'Painted bits are the baked passability covered by the walker region graph. mask_b is the editor input before slope, water and outer-frame exclusions; mask_c forces cells passable. The frame margin source is not decoded.'}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=wor.ROOT / 'converted/walker-mask-report.json')
    report = audit(parser.parse_args().out)
    print('%d maps; walker regions match painted: %s; eight-way links: %s; exact slope/water bake on %d maps'
          % (report['map_count'], report['walker_regions_match_painted'], report['walker_links_are_eight_way'],
             report['maps_with_exact_slope_water_bake']))
    raise SystemExit(0 if report['walker_regions_match_painted'] and report['painted_within_editor_inputs'] else 1)
