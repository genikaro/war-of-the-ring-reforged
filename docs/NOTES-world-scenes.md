# WOR map scenes: Phase 2

`world_scene.py` extends `wor.py` with sequential readers for terrain painting,
placed models/effects/decals, ground navigation, painted lakes and the world
tail. `wor2gltf.py` exports a portable GLB. All source files remain read only.

## Reproduce

From the repository root (Python needs Pillow and NumPy):

```powershell
python tools/wor2gltf.py "extracted/Levels/Helms Deep.wor" --out-dir converted/maps/helms-deep
python tools/wor2gltf.py extracted/Levels/DS06_Birth_of_the_UrukHai.wor --out-dir converted/maps/uruk-birth
node tools/validate-glb.mjs converted/research/node_modules/gltf-validator --index converted/maps/helms-deep/index.json --report converted/maps/helms-deep/gltf-validation.json
node tools/validate-glb.mjs converted/research/node_modules/gltf-validator --index converted/maps/uruk-birth/index.json --report converted/maps/uruk-birth/gltf-validation.json
python tools/verify_phase2.py
```

The separately installed Khronos validator is optional development tooling;
the importer needs neither it nor a disassembler. Serve the repository root
with `python -m http.server 8767 --bind 127.0.0.1` and open:

```text
http://127.0.0.1:8767/tools/viewer.html?index=../converted/maps/helms-deep/index.json&model=../converted/maps/helms-deep/scene.glb&yaw=15&pitch=60
```

The viewer supports `pitch`, orbit/pan/zoom and map-sized clipping distances.
Repeatable close-ups accept `focus=x,y,z` in glTF axes and positive `distance`.
Generated artifacts stay in ignored `converted/`: the combined `scene.glb`,
detailed `scene.json`, lossless `terrain-vertices.bin`, 93 inspection GLBs in
`models/`, `report.json`, validation results and browser previews. The full
inventory audit is `converted/phase2-report.json`.

## Confirmed scope and limits

All 54 shipped maps reach EOF with bounded,
contiguous reads. All 59,850 model placements resolve, including their saved
model-event variants. Another 1,022 placed effects resolve to Effects records.
All three terrain palettes and 17 mesh path lookup files parse to EOF.
The tail now decodes structurally, including spline keys, half-resolution grid,
camera/marker records, placed decals and fog payloads. Unnamed properties remain
numerical; their runtime behavior is not claimed to be implemented. Build-152 names the lighting, environment,
sky and grass fields (`map_lighting.py`; the tail's former `properties`, `regions` and `camera` are `time_of_day`,
`sun_keyframes` and `grass`) and the prototype uses them ([LIGHTING.md](LIGHTING.md)).

Helm's Deep exports 92,133 height samples, 5,720 primary tiles/105 textures,
767 placement nodes/93 models, two lakes and both pathing categories. Four
untextured `IsWalkable` helpers are disconnected from the visible scene;
their node data, geometry and four linked lookup matrices remain available.
This policy uses the database flag and absence of a declared model material,
not filename matching. Otherwise they display gray navigation surfaces.

The combined GLB passes Khronos validation with zero errors and warnings.
82 synthetic Python tests and eight JavaScript appearance tests pass, including
17 map/navigation/decal regressions. Both scenes were inspected in the browser. Models use
static poses; water uses the stored surface and opacity without animated ripple,
reflection or refraction shaders. The prototype's
initial reference review now
corrects runtime texture filtering and lake vertex-colour binding. Controlled
original-game fidelity comparison remains unverified.

Phase 2's scene gate is complete. All shipped secondary terrain layers are empty
(zero non-sentinel texture IDs across 54 maps); transitions are in the primary
tile images. Unseen custom maps with secondary painting fail export explicitly.
There are 51 editor decals and 701 persistent object decals. All resolve to
existing textures and produce non-empty clipped terrain surfaces (79,428
triangles total). Birth of the Uruk-Hai exercises four editor and six object
decals in its validated GLB. The proof files bind browser screenshots and
validation results to each exported GLB's SHA-256.

## Terrain records

After the CRC-checked v104 vertex blob: palette-mode byte; layer count and
parameter; texture layers; coordinate sets; composition grid; tile-color grid;
four parameter words; material bindings/materials; texture lookup; fixed world
environment payload. The parser must reach the independently validated name
pool exactly. All shipped maps use direct texture IDs and layer v102.

A direct layer contains version, two tile-cell dimensions, a flag and enabled
byte, then an i32 texture-ID grid (`-1` absent). Coordinate v100/101 contains
tile dimensions, channel count, usage and `(dx+1)*(dy+1)*channels` floats;
v101 may include two extra per-vertex arrays. Composition v100/101 has its
own byte-grid dimensions; color-grid v100 contains u32 values. Material
bindings select coordinate sets; `LIQDMATL` versions 1/2/3 have fixed bodies
of 0x204/0x264/0x938 bytes, retained as raw words. A world-wrapper texture
lookup and 94-byte environment follow. Unnamed words stay integers.

Primary tile images already contain blended transitions (`dddd`, `ssss`,
mixed variants). The exporter uses their stored UV grid (normally 5x5 for
4x4 cells) and the first packed ARGB vertex word. Specular and the other two
vertex words are retained raw. Height-derived normals and a fixed triangle
diagonal are preview choices.

`terrain_set.py` reads v4 ID/name, counted palette entries of label, ASCII key,
composition name/ID/color, then counted texture-folder names. Composition-grid
bytes are palette keys (`D`, `G`, `S`, `T`), not database IDs. The audit validates
composition names against `TerrainCompositions`.

Texture-grid allocation truncates a product with a stored float32 reciprocal,
rounded toward zero, then adds a cell only for an integer division remainder.
For 456/24 this stores 18, not 19: the reciprocal is slightly below 1/24.
This quirk affects Lothlorien and Cirith Udun lake layers; mathematical `ceil`
misaligns their following sections. A synthetic regression preserves it.

## Placements and navigation

After the name pool: optional 80-byte player array, then model placements.
The fixed 95-byte record stores four u32 values (name index, class, database
ID, saved model variant), position/scale as two float4s, three Euler angles,
seven property words, two bytes, object ID, one byte, then counted u32 pairs.
Properties include owner, health and pair count; unnamed values remain raw.

Class 0/1/2 selects Units/Buildings/Objects. Units/buildings reference `Model`;
Objects reference `ModelEvents`, where the saved one-based variant selects
`AssociatedModelResourceN`. Choosing the first weighted resource would replace
saved scenery variants. The following array holds placed effects: opaque word,
interned-name index, position/scale, property words and flag; effect references
are at in-memory offsets 0x3c/0x40. Helm's Deep has no such entries. A separate
line/point array follows.

Scene axes match model export: `(x,y,z) -> (-x,z,y)`. Instances share meshes
and images, with separate joint hierarchies/skins. Skin mesh nodes remain scene
roots; their joints inherit placement transforms. Map snapshots omit animation
channels; standalone model GLBs keep them.

Ground categories store enabled/ID/name, maximum water depth and slope, optional
grid bitset, two more bitsets and optional region/portal graph. Bitsets have
cached population, bit count and padded u32 words; population and dimensions
are checked. Region links, u16 indices, portal lists, bit matrices and lookup
tables decode structurally. Unnamed masks/properties are not a pathfinding API.

Each `.mpl` v100 category has a square-matrix dimension, allocated word count,
logical byte count and packed two-bit codes. All 17 files have two categories.
`code(source,destination) = codes[destination*N + source]`. Codes 0,1,2 select
the source triangle edges (v0,v1), (v1,v2), (v2,v0); 3 means unreachable. Before
assigning compact IDs, the game excludes LMS triangles whose footer flags have
bit 1 set. Retained triangles stay in file order; render vertices map through
`pos_index` before finding shared edges. All 17 dimensions match exactly.

Fifteen tables pass every route check: 583,802 ordered pairs across 30 category
matrices, including 189,236 unreachable pairs. The shipped
`MS_Human_Bridge_Walkable_Mesh.mpl` and older
`M_S_Osgiliath_Bridge_Walkable_Mesh.mpl` contain routing cycles against their
matching geometry. `mpl.load_resource()` fails by default; inventory/export
can preserve them with `strict=False`, `stored_routes_valid=false` and the
error. Runtime must rebuild routes from their exported adjacency instead of
following those matrices. No source file is repaired or replaced.

`lms.parse(..., read_footer=True)` decodes full mesh groups and triangle flags
and checks EOF. All navigation meshes pass. Geometry-only conversion remains
available: nine Obelisk effect meshes' animated group UV tails are not yet
supported by the strict footer reader, unrelated to the map navigation data.

## Prototype navigation snapshot (Oct 19)

`prepare_prototype.py` preserves the painted walker mask and derives a separate
runtime allowed mask. On Helm's Deep the raw `(painted, mask_b, mask_c)` counts
are `(1,1,0): 38,841`, `(0,1,0): 6,849`, `(0,0,0): 45,830`.

The walker graph's row tables are u16 `(region, run length - 1)` pairs, with
`0xffff` for no region and the table word equal to the row width
(`world_scene.region_cells`). Each region record stores its cell count and
bounding box (`raw_words[1:6]`); all 54 maps match the decoded cells.
`verify_walker_masks.py` then shows on every map:

- The walker regions cover exactly the painted bits, so painted is the
  original runtime passability. mask_b-only cells belong to no region.
- Region links equal eight-way cell adjacency, including diagonals whose two
  flank cells are unpainted (126 corner-only links on Helm's Deep, 12,697 in all).
- Painted cells always lie in mask_b or mask_c, for both categories. Unforced
  painted cells pass the category's slope limit (steeper of the cell's two
  terrain triangles) and lake depth on 46 maps; the other 8 differ by at most
  15 cells. mask_c cells act as forced passable: all walker mask_c cells are
  painted except 106 on LS06. mask_b cells passing both limits but left unpainted lie outside the
  painted bounding box, apart from at most 23 per map: an outer frame whose
  margin source is not decoded. Helm's Deep reproduces exactly: painted =
  mask_b, slope <= 30 degrees, depth <= 1 m, inside that frame.

The prototype keeps painted bits as allowed cells and fails cache generation if
they differ from the region graph. Its ground diagonals still require both
flank cells; without corner-only diagonals 66 painted cells on Helm's Deep
(2,192 over 43 maps) fall out of the original's main connected area. Whether
units in the original cut those corners is not verified.

All ten building centers have painted bits set. The static obstacle adapter
therefore subtracts independent footprints for Buildings and Objects whose
database `IsPathingObstacle` is set. All 108 such placements have one root box
in `BBOX_LAYER_ORIENTED_COLLISION` (BBoxes.lqr), flag zero, positive dimensions,
two homogeneous vector tags of 1 and a unit final quaternion within 0.01.
The prototype interprets their three vec4s as half-extents, center and wxyz
quaternion. This interpretation matches the source poses and render scale,
but the executable's collision-query formulas have not been verified. Parser
records intentionally keep their raw vector representation.

`prototype_obstacles.py` transforms all eight corners and uses a projected
convex polygon plus vertical interval. A conservative cell envelope includes
a 350 mm unit radius and 1.8 m body. SAT overlap blocks 1,741 previously allowed
ground cells. Height tests also apply to mesh nodes and edges, keeping separate
floors. Unsupported box flags, joint attachment or absent collision layers
fail generation rather than silently dropping an obstacle. Moving/created/
destroyed obstacles require later runtime updates; projected tilted boxes may
block more space than necessary. Unit clearance from mesh boundaries remains
separate work.

`prototype_navigation.py` rebuilds layered links from transformed, filtered
navigation triangles instead of following MPL codes. Helm's Deep produces
1,342 surface nodes with 48 height-matched terrain links. Segment/triangle
interval unions reject gaps; source mesh/placement scales and rotations match
the GLB. All six friendly units traverse all four helpers in the cache-backed
C# check, preserving integer heights and non-overlap.

A cell center supports the 700 mm body only on walkways at least 1.7 m wide.
Where it cannot, the builder takes the nearest supported point of a 100 mm
lattice within 400 mm of the center (135 off-center nodes on Helm's Deep; 129
more walkway nodes are reachable from the main ground area). Off-center nodes
stay 700 mm from nearby nodes and from possible links between them, and links
passing within 700 mm of a third node are dropped, so a unit parked on any node
leaves every other link usable. Bodies narrower than 700 mm stay impassable;
this is a prototype policy rather than original path fidelity.

## Decals and world tail

After lakes: optional 48-byte camera-waypoint records; placed lights with two
vector splines and a quaternion spline; half-resolution u32 grid; world bounds
and regions; camera paths; camera properties; marker array; decals; fixed-size
fog images and final settings. All section boundaries are checked to EOF.
Spline keys are retained as words until interpolation is implemented.

Each 63-byte decal stores object ID, Decals database ID, position/scale float4s,
three Euler degree angles, ARGB tint, opacity, priority and two flags. The
second word is a database reference, not an interned name. Persistent object
decals come from each placed record's `Decal` column. Definition dimensions are
scaled by the saved transform; source angles rotate the projection rectangle.
`map_decals.py` clips each intersecting source triangle to UV [0,1]^2, interpolates
height/diffuse color and applies saved tint/opacity. A 0.015-unit lift prevents
z-fighting. The portable material uses alpha blending. Initial size is used;
random variation, timed resizing/fades, original cloud/specular shader blending,
water/object projection and animated effects remain runtime work.

## Lakes and executable evidence

Lake wrappers store ID/name/plane height, properties and optional v101 surface.
The surface contains cell size/extents, cell/vertex counts, origin, CRC-checked
five-word vertices, texture/UV layers, filters, flags, parameters and material.
Vertices contain elevation, ARGB diffuse/specular, cell flags and another word.
The draw loop tests bit 0 on each cell's top-left vertex to exclude both
triangles. Testing neighboring vertices creates an incorrect border. Unsupported
ripple-generator arrays fail explicitly; original shader parameters remain data.

Addresses apply to the read-only `Rings.exe` hash documented in `NOTES-levels.md`:

| Address | Evidence |
|---|---|
| `0x63eeee` | Terrain v104 section order |
| `0x642c26`, `0x642558` | Direct layer v102 and texture-grid dimensions |
| `0x63884a`, `0x6389d5` | Terrain coordinate grids |
| `0x638198`, `0x64142e` | Composition/color grids |
| `0x5a9864` | LIQDMATL fixed body versions |
| `0x5691d7`, `0x5693e7` | World terrain wrapper, lookup/environment |
| `0x5694c2`, `0x56a1ad` | Player data/model placements |
| `0x56ac1a`, `0x56b270` | Placed effects and line/point arrays |
| `0x56b92d`, `0x42abab` | Ground pathing categories/region graph |
| `0x4164c5`, `0x5a084e` | Grid/population-checked bitsets |
| `0x56ca33`, `0x63fc4e` | Lake wrapper/v101 surface |
| `0x645936`, `0x646361` | Lake texture/UV grids |
| `0x63c587` | Per-cell water exclusion flag |
| `0x41fa3d`, `0x41e11d` | Filtered triangle IDs and edge adjacency |
| `0x41f507`, `0x41f52b`, `0x41eb33` | Destination-major code, unreachable test and edge following |
| `0x5c769e`, `0x5ebb52`, `0x5c7718` | LMS groups and optional triangle flags |
| `0x56ce49` through `0x56df83` | Sequential world tail |
| `0x56d412`, `0x565c2d` | Time-of-day block and Sun/Moon keyframes (tail `time_of_day`, `sun_keyframes`), defaults |
| `0x53c80a`, `0x53cef7` | Sun position from time of day; light colour x intensity (`map_lighting.py`, build-152) |
| `0x56a77b` | Placed decal records |
