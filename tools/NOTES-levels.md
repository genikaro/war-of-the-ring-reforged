# Level containers and editor concepts

The inventory below describes the initial conservative reader. The new
`world_scene.py`/`wor2gltf.py` pipeline decodes the terrain/placement/pathing/lake
prefixes of all 54 maps and renders Helm's Deep; see `NOTES-world-scenes.md`.

`wor.py` inventories the shipped levels without changing the source install.
It reads world metadata, the minimap and the compressed terrain vertex grid,
then locates and validates the interned world-name table. Remaining byte ranges
are recorded as opaque. This is the foundation for terrain and placement
readers, not a complete map renderer.

## Archive inventory

`Levels.H2O` contains 54 `.wor` files, totaling 102,087,877 unpacked bytes:
32 multiplayer maps under `Multiplayer/`, 21 campaign maps and `MenuScene.wor`.
There are no separate heightmap, object-placement or waypoint files in this
archive: those resources belong to each world container.

All 54 have world version 125, metadata version 102 and terrain version 104.
The three terrain sets are Grasslands (24 maps), Mordor (17) and Mirkwood (13).
21 maps are rectangular. The terrain blobs contain 5,208,066 vertex records,
104,161,320 bytes before decompression. Every terrain CRC verifies. The name
tables contain 64,639 entries; **this is not a count of placed objects**.

Commands from the repository root:

```powershell
python -B tools/wor.py inventory --out-dir converted/levels --previews
python -B tools/wor.py inspect "extracted/Levels/Helms Deep.wor"
python -B tools/wor.py inventory "TLOTR - War of the Ring/Archives/Levels.H2O" --out-dir converted/levels-from-archive
```

Archive input verifies each H2O entry CRC as well as the inner terrain CRC.
Directory input reads the already extracted `.wor` files. Both produce the
same per-map JSON. Output includes:

- `index.json`: source paths, dimensions, terrain sets, name counts and links.
- `maps/<original relative name>.json`: metadata, source SHA-256, byte ranges,
  raw header fields, terrain statistics and the complete indexed name pool.
- With `--previews`, `minimaps/` and `heights/`: PNG previews using each map's
  relative path. Elevations are normalized independently to 8-bit grayscale;
  these images are inspection aids, not lossless heightmap exports. The Python
  `World.heights` array retains the decoded float values.

Bad maps are reported individually in the index and make the command fail.
Unsupported versions, invalid sizes, corrupt CRCs and ambiguous name tables
are rejected. Archive output paths are checked for traversal. Output inside
the original install or source level directory is refused.

## Recovered prefix and terrain blob

All fields are little endian. Strings use the same form as model/database
strings: u8 zero marker, u32 UTF-16 character count including NUL, then text.

```text
u32 world_version = 125, u32 metadata_version = 102
str name, str description
u32[2] unknown
u16 width, u16 height, u16 flags
u32 player_count_field, u8 preview_flag
u16[128*128] minimap pixels
u8 pre_terrain_records_present
if present: u32 count, byte[count*96] opaque records
u8 terrain_present
u32 terrain_version = 104
f32 cell_size, f32 width, f32 height, f32[4] raw_parameters
str terrain_set_name, u32 terrain_set_id, u32 packed_value
u32 compressed_size, u32 raw_size, u32 crc32
byte[compressed_size] terrain vertices (DCL, or stored when sizes match)
... terrain layers, coordinate sets and environment records ...
```

The vertex grid has `(width/cell_size + 1) * (height/cell_size + 1)` records,
20 bytes each. Each record starts with f32 elevation, followed by four raw
u32 words. The executable copies these words into vertex fields at offsets
`0x04`, `0x08`, `0x70` and `0x74`; their complete interpretation is pending.
They are not additional position floats: some words contain packed values
which would be NaNs if misread as floating point.

The inspector reads elevations in stored grid order. Terrain UVs, blend
weights, texture-layer selection, decals and coordinate orientation remain
future work. The 16-bit minimap preview is interpreted as RGB565; its colour
format and orientation still need comparison with the original game.

## World-name table and remaining records

The loader reads an interned string pool after terrain/environment:

```text
u32 blob_size
u32 name_count, u32 blob_size_again
char16[] NUL-terminated names
```

The repeated size covers the internal eight-byte header and the text, not the
outer size word. The inspector searches only after the verified vertex blob,
requires matching sizes, exact string count, complete UTF-16 terminators,
printable nonempty names and exactly one candidate. All shipped maps satisfy
these checks. This is a **structural locator**, not a sequential parse of
the intervening terrain records. Empty name pools and other world versions
are not currently supported.

Names include units/buildings/scenery, lakes, creature spawn areas, start
points, camera paths, `DEFAULT_CAMERA_BOUNDARY` and script labels such as
`WP_`, `CWP_` and `CP_`. `name_hints` groups labels by those prefixes for
inspection; it does not establish typed objects, positions, path links or
model references. These must come from the world records after the name pool.

All bytes are accounted for by contiguous ranges in the JSON. Opaque ranges
are marked `decoded: false`; boundaries inside them remain undecoded.

## Helm's Deep example

The world name is `Helm's Deep`, description `The battle of the Hornburg.`.
It uses Grasslands, extent 352 by 260, cell size 1 and a 353 by 261 grid:
92,133 vertices. Elevations range from about -19.9918 to 60.

| Range | Offset | Bytes |
|---|---:|---:|
| Metadata | `0x0` | 117 |
| Minimap | `0x75` | 32,768 |
| Pre-terrain records / presence fields | `0x8075` | 14,118 |
| Terrain header | `0xb79b` | 67 |
| Terrain vertex blob, including 12-byte header | `0xb7de` | 823,613 |
| Remaining terrain/environment, opaque | `0xd491b` | 112,105 |
| Name table, including outer size | `0xeff04` | 33,234 |
| World records / trailing data, opaque | `0xf80d6` | 803,806 |

The terrain expands to 1,842,660 bytes with CRC `d08536f1`. There are 962
interned names, including 145 `WP_`/`CWP_` labels and six `CP_`/`Camera Path`
labels. These are named references, not yet exported waypoint coordinates.

## Executable evidence

Addresses apply to the shipped 32-bit `Rings.exe`, SHA-256
`7d9e30a76f08614de4c6c9cdbcd10769c73ecc87af04c4376c1709fa080b435a`.
The importer needs no disassembler or executable execution.

| Address | Role |
|---|---|
| `0x568523` | Latest world-body load order |
| `0x568769` | Metadata fields and fixed `0x8000`-byte minimap |
| `0x56b359` | Optional pre-terrain array, stride `0x60` |
| `0x5691d7` | Terrain presence and subsequent environment fields |
| `0x63ce09` | Terrain version dispatch |
| `0x63eeee` | Terrain v104 fields and compressed vertex grid |
| `0x63f0d9` | Five-word vertex copy |
| `0x5a0632` | Stored/DCL blob decode and CRC check |
| `0x56943f`, `0x5a44d9` | World name pool and string-table decoding |

## Editor guide: terminology that affects the next readers

The shipped `RingsEditor.chm` was extracted under `converted/editor-help/`
using an installed 7-Zip. The guide documents the editing concepts; it does
not document the binary format. Relevant chapters:

- `new.html`, `terrain.html`: map dimensions can be rectangular; a terrain
  set provides the texture palette and a default tile. Height sculpting and
  vertex-colour painting are distinct operations.
- `manipulation.html`, `placeable.html`: placeables include objects, units,
  buildings, decals and effects. Scenery supports random rotation and scale.
  Object placement alone does not paint movement obstacles.
- `passability.html`, `nono.html`: walker and building passability are separate
  layers. Bridges have walkable meshes, with their own entrance constraints;
  neither height alone nor visible scenery is sufficient to recover pathing.
- `lake.html`: lakes are painted regions with a water-plane height, a bottom,
  surface opacity, colour, reflection and ripple properties. A bounding rectangle
  alone will not reproduce the shoreline.
- `marker.html`: start points, creature spawn areas and water-ripple generators
  are separate marker types; spawn areas have a creature type, count and radius.
- `camera.html`: camera planes prevent the camera dipping into chasms. Camera
  view margins and impassable margins are different settings; painted passability
  cannot override the latter.
- `lighting.html`, `sound.html`: global time/lighting and placed dynamic lights
  coexist; ambient sounds may be points or connected lines with inner/outer
  attenuation ranges.

Phase 2 now extends this initial inventory through EOF: terrain painting,
placements, passability, water, camera/marker records and decals decode in
`world_scene.py`. See `NOTES-world-scenes.md` for scene exports and current
validation, and `prototype/README.md` above this folder for the Godot sandbox.

## Validation

55 Python tests and seven viewer-logic tests pass. Eleven new synthetic tests
cover metadata and Unicode names, complete range coverage, stored and compressed
vertices, bad versions/dimensions/CRCs/DCL streams, name-table ambiguity,
archive CRCs, path traversal, output safety, partial inventory failures and
preview pixels. All 54 real maps decode from directory and archive input.

```powershell
python -B -m unittest discover -q -s tools
node --test tools/test-model-appearance.mjs
```
