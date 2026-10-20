"""Body-specific clearance over the existing terrain / walkable-mesh graph."""
import base64
import hashlib
import json
import math
from pathlib import Path
import struct

from prototype_navigation import covers_body, node_point, terrain_height
from prototype_obstacles import runtime_layers, intersects

ROOT = Path(__file__).resolve().parent.parent


def build(nav, heights, definitions):
    width, height = nav['width'], nav['height']; base = width*height
    spacing = nav['cell_size_mm']/1000
    allowed = base64.b64decode(nav['painted_allowed_base64'])
    nodes = nav['surface_nodes']
    surfaces = {s['name']: s['triangles'] for s in nav['picking_surfaces']}
    footprints = [b for o in nav['runtime_obstacles'] for b in o['footprints']]
    profiles = []
    def point(n):
        if n >= base:
            return node_point(nodes[n-base], width, spacing)
        x, z = (n % width+.5)*spacing, (n//width+.5)*spacing
        return x,z,terrain_height(x,z,width,spacing,heights)
    def ground_patch(x,z,radius):
        result = []
        for row in range(max(0,math.floor((z-radius)/spacing)), min(height-1,math.floor((z+radius)/spacing))+1):
            for col in range(max(0,math.floor((x-radius)/spacing)), min(width-1,math.floor((x+radius)/spacing))+1):
                if not allowed[row*width+col]: continue
                def vertex(cx,cz): return (cx*spacing,cz*spacing,heights[cz*(width+1)+cx])
                a,b,c,d = vertex(col,row),vertex(col+1,row),vertex(col,row+1),vertex(col+1,row+1)
                result.extend([[a,b,c],[b,d,c]])
        return result
    edges = sorted({tuple(sorted((base+i,link))) for i,n in enumerate(nodes) for link in n['links']})
    for radius_mm, height_mm in sorted({(u['BodyRadiusMm'],u['BodyHeightMm']) for u in definitions}):
        radius, body_height = radius_mm/1000, height_mm/1000
        blocked = set()
        for i,node in enumerate(nodes):
            p = point(base+i)
            if not covers_body(p,p,surfaces[node['surface']],radius): blocked.add(base+i)
        # Walkable decks are not static obstacle footprints. Their undersides
        # still limit headroom for bodies on the terrain / other mesh floors.
        for surface_name, triangles in surfaces.items():
            for triangle in triangles:
                floor = min(v[2] for v in triangle)
                polygon = [(v[0],v[1]) for v in triangle]
                xmin = max(0,math.floor((min(v[0] for v in triangle)-radius)/spacing))
                xmax = min(width-1,math.floor((max(v[0] for v in triangle)+radius)/spacing))
                zmin = max(0,math.floor((min(v[1] for v in triangle)-radius)/spacing))
                zmax = min(height-1,math.floor((max(v[1] for v in triangle)+radius)/spacing))
                for row in range(zmin,zmax+1):
                    for col in range(xmin,xmax+1):
                        n = row*width+col
                        if not allowed[n] or n in blocked: continue
                        x,z,y = point(n)
                        if y+.3 < floor < y+body_height and intersects(polygon,x-radius,x+radius,z-radius,z+radius): blocked.add(n)
                for i,node in enumerate(nodes):
                    if node['surface'] == surface_name or base+i in blocked: continue
                    x,z,y = point(base+i)
                    if y+.3 < floor < y+body_height and intersects(polygon,x-radius,x+radius,z-radius,z+radius): blocked.add(base+i)
        blocked_edges = []
        for a,b in edges:
            if a in blocked or b in blocked: continue
            triangles = []
            for name in {nodes[n-base]['surface'] for n in (a,b) if n >= base}:
                triangles += surfaces[name]
            if a < base or b < base:
                for n in (a,b):
                    x,z,_ = point(n); triangles += ground_patch(x,z,radius+spacing)
            if not covers_body(point(a),point(b),triangles,radius): blocked_edges.append([a,b])
        profiles.append({'radius_mm':radius_mm,'height_mm':height_mm,'nodes':sorted(blocked),'edges':blocked_edges,
            'obstacles':runtime_layers(width,height,spacing,allowed,heights,nodes,footprints,radius,body_height)})
    return profiles


def refresh(output, definitions=None):
    output = Path(output).resolve()
    if any(output.is_relative_to(ROOT/p) for p in ['extracted','TLOTR - War of the Ring']):
        raise ValueError('clearance cache must be outside original and extracted data')
    path = output/'navigation.json'; nav = json.loads(path.read_text())
    if definitions is None: definitions = json.loads((output/'combat/rules.json').read_text())['Units']
    raw = (output/'heights.bin').read_bytes()
    heights = [h/1000 for h in struct.unpack('<%di' % (len(raw)//4),raw)]
    nav['body_profiles'] = build(nav,heights,definitions)
    nav['body_profile_policy'] = 'Scaled main-collision cylinders; conservative swept-square terrain/mesh support, body-specific object masks. Same base graph; unsupported larger bodies cannot use the 350 mm mesh clearance.'
    path.write_text(json.dumps(nav,indent=2)+'\n')
    manifest_path = output/'manifest.json'
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text()); manifest['files']['navigation.json'] = hashlib.sha256(path.read_bytes()).hexdigest()
        manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    return nav['body_profiles']
