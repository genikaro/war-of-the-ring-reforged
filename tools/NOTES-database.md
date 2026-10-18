# Database (.lqr) and model (.lmd) notes

Byte layouts live in the module docstrings of `lqr.py` and `lmd.py`; this file
records how they were verified and what is still open.

## Tools

| Script | Purpose |
|---|---|
| `lqr.py` | Reads any `Database/*.lqr` table (records, symbolic names, column schema, references, editor folders) and `Rings.lrp` (table id to name); exports all tables and schema to JSON. |
| `lmd.py` | Parses `.lmd` models: skeleton ids, mesh / material / texture slots with weights and state masks, attached models and effects with joint index and offset. |
| `resolve_model.py` | Model name, unit name, id or `.lmd` path to the files under `extracted/` (`.lsk`, `.lms`, `.lwt`, `.dds`, `.lfx`), recursive over attachments, joint names from the `.lsk`. `--json`, `--validate`. |

```bash
python lqr.py schema Units
```

```bash
python lqr.py show Units UNIT_G_HERO_ARAGORN
```

```bash
python lqr.py export --out-dir ../converted/database
```

The JSON format and reference lookup are documented in `README.md`, under
"Database JSON". Raw IDs and values are preserved; enum labels and reference
targets live in the column schema rather than replacing record values.

```bash
python resolve_model.py UNIT_G_HERO_ARAGORN
```

```bash
python resolve_model.py MODEL_E_URUK_HAI --json
```

```bash
python resolve_model.py --validate --list-missing
```

Python use:

```python
import lqr, lmd, resolve_model
db = lqr.Database()                       # Database dir next to tools/
db['Meshes'].get('MESH_M_SW_G_ARAGORN_BODY')['Filename']
model = lmd.load('../extracted/Models/Units/Good/Aragorn/G_Aragorn.lmd')
res = resolve_model.Resolver().resolve('MODEL_G_ARAGORN')   # plain dict, as --json
```

## How a unit resolves

`Units.lqr` column `Model` (reference to Models) -> `Models.lqr` Filename ->
`.lmd`. The `.lmd` holds record ids: skeleton (Skeletons), per mesh slot the
mesh (Meshes) and weights (Weights), materials, texture slots (Textures), and
attachments (Models / Effects) with a skeleton joint index. Heads, weapons,
quivers and blood are separate models attached to the body model; a head
attaches with joint -1 and its own weights, i.e. it is skinned to the parent
skeleton. Example: `MODEL_G_ARAGORN` = body mesh 0x574 + weights 0x83 +
texture 0x970 + mask 0x971 + skeleton 0xb0; head model 0x13d (mesh 0x575,
weights 0x84); sword 0x13f on joint 28 `J_Attached_Sword`; scabbard on joint 3.

Texture paths: `Textures.lqr` stores the source `.tga`; the extracted file is
`.dds` unless the record's Conversion is None (44 files stay `.tga`).

## Validation (all files in this install)

- `.lqr`: all 154 tables parse to the exact byte (record block ends at the
  name block offset, editor block ends at the string table offset);
  `lqr.py list` and `schema` run on every table.
- JSON export: 154 tables, 24,468 records and 1,466 columns. All exported record
  IDs, names, folders and values, plus column types, defaults, references and
  enum labels, round-trip against the parsed source. Repeat exports are byte
  identical. Synthetic regression tests cover sparse IDs, metadata collisions,
  UTF-8, absent project metadata, invalid input and interrupted file replacement.
- `.lmd`: all 2108 models parse; header counts match the slot lists in every
  file; all 288 models that declare attachments decode them (1879
  attachments: 1876 by id, every id exists in Models or Effects, 3 by path).
- `resolve_model.py --validate`: **2108 of 2108 models resolve fully**, every
  referenced file exists (model, all skeleton slots, meshes, weights, all
  texture slots, attached models recursively, effect `.lfx` files). 2 models
  reference an attachment by a stale authoring path
  (`...\Osgiliath Pieces\Minas_Ithil_Building_Wall_03.lmd`) and resolve by file
  name. 2668 mesh parts, 2641 with a texture (the rest are walkable / decal
  helper meshes without material), 744 with a team colour mask.
- Units: 84 of 85 units resolve fully through their Model column (the 85th
  has no model).
- Cross check against the geometry headers: every weights file has exactly
  the position count of the mesh it is paired with (206 checks, 0
  mismatches) and the joint count of the skeleton of its model or, for
  joint-less attachments, of the parent model (152 checks, 0 mismatches).

## Open questions

### .lqr

- Name block starts with 9 flag bytes; the first is 1 exactly for the 23
  tables with a filename column, the others are not understood.
- Column descriptor: two i32 fields not understood (one often looks like a
  maximum: 10, 50, 100, 255, 360, 5000; the other is -1 or a small number for
  id columns).
- Per record editor entry: a u32 (0..6) and a u8 flag before the folder path.
- Column type 0 vs 3: both are 4 byte ints; 3 is used for references, enums
  and bools, 0 for plain numbers. No other difference seen.
- `Rings.lrp`: two id lists (37, 79, 97 and 100) and the strings Demo / Demo /
  _Demo before the table list, probably build configurations (tables have
  `Version2_Demo` columns).

### .lmd

- Not decoded: the joint set / per joint bounds block after the skeleton ids
  (skipped by searching for the first slot ref), and the animation state list
  (header count [0]) between the texture slots and the attachments. The
  animation states probably carry per state mesh visibility and texture
  channel binding, which buildings need (construction / healthy / damaged /
  ruins meshes and damaged texture swaps). Until then, `resolve_model.py`
  gives every mesh the default diffuse texture (first diffuse slot on the
  lowest channel) and lists the rest as alternates.
- Texture channel byte: units use 0 = diffuse, 1 = team colour mask, but
  buildings number channels 0..6 (healthy, damaged, mask, black, ruins...).
  Mask detection therefore uses "material is MATERIAL_TEAM_COLOR and the
  file name contains mask".
- State masks: bit 24 (0x1000000) is the default for simple models. Unit
  upgrade variants use other bits, e.g. Gondorian swordsman body L1 mesh
  0x40010, L3 mesh 0xf0, textures L1 0x30, L2 0xd0, L4 0x40010 (bit 4 shared
  by all). Which game state each bit is (upgrade level, idle variation) is
  not known.
- Extra skeleton slots: 46 models have 2 or 3 skeleton ids, e.g. Balrog
  [E_BALROG, 0, WORLD_OBJECT_MINI_VOLCANO_00], Gondorian swordsman
  [G_GONDORIAN_BLUNT, 0, G_ELVEN_KNIGHT], Giant Spider [E_GIANT_SPIDER,
  E_BLACK_RIDER]. In the 3 slot cases the middle id is always 0. Likely
  animation sources; the first slot is the model's own skeleton.
- Ref object: `u32 1, u32 n, u32[n]` before the id; n is 1 (value 0 or -1)
  except one ref with n = 0. Meaning unknown.
- Attachments: 21 bytes between the joint index and the offset are not
  understood; version 0x69 and older have no offset (identity assumed).
  Offset order is assumed quaternion w x y z, position, scale (identity is
  1,0,0,0, 0,0,0, 1).
- Header fields [5] and [8..14] (beyond the counts listed in `lmd.py`) are not
  identified.
- The `.lms` header flag is not a strict "has weights" marker: 21 parts
  disagree (trees and moss flagged skinned without weights, Uruk-hai head and
  ambient spider the other way round).
- `.lsk` 1.0 joint names are read heuristically by `resolve_model.py`
  (exact for 2.0).
