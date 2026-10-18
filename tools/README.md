# WotR asset tools (proof of concept)

Python 3.13 + Pillow. Run from this folder.

| Script | Purpose |
|---|---|
| `h2o.py` | List/extract Liquid Entertainment `.H2O` archives (v6). Format notes in the module docstring. |
| `blast.py` | PKWARE DCL "implode" decompressor used by H2O. |
| `lms.py` | `.lms` mesh parser (v1.0, v1.1 and v2.0). Format notes in the module docstring. |
| `lsk.py` | `.lsk` skeleton + animation clip parser (v1.0 and v2.0). Format and pose maths in the docstring; verification in `NOTES-legacy-assets.md`. |
| `lwt.py` | `.lwt` skin weight parser. |
| `lqr.py`, `lmd.py`, `resolve_model.py` | Database tables (including JSON export), `.lmd` models, and name to file resolution. See `NOTES-database.md`. |
| `lmd_states.py` | Decoded weighted animation choices, mesh selectors, attachment visibility and property tracks inside `.lmd` models. See `NOTES-animation-states.md`. |
| `lms2gltf.py` | Export to `.glb`: one unit by name, all units, or hand-picked files. |
| `viewer.html` | three.js viewer with unit and clip pickers (`?clip=Name`, `?t=0.5` freezes a frame). |

Extract the archives once:

```bash
python h2o.py extract "../TLOTR - War of the Ring/Archives" ../extracted --verify
```

Export one unit (or `MODEL_...` name) with its textures, skeleton, all clips and attachments:

```bash
python lms2gltf.py model UNIT_G_HERO_ARAGORN --out ../converted/units/MODEL_G_ARAGORN.glb
```

Export every unit plus `index.json` for the viewer:

```bash
python lms2gltf.py units --out-dir ../converted/units
```

All state-controlled meshes and model attachments are retained in the GLB. Its
default scene uses the class's declared idle state. Add `--level 2` or `--blood`
to set the preview configuration, or `--all-attachments` to expose every model
attachment for debugging. The old manual mode is `lms2gltf.py files` (see its `--help`).

Viewer: serve the repo root (`.claude/launch.json` config `viewer`) and open
`http://localhost:8765/tools/viewer.html` (config `viewer-alt` serves on 8767 when 8765 is taken).

## Animation states

Inspect state IDs, database names, primary skeleton clip names, mesh selectors,
attachment masks and timed property keys:

```bash
python resolve_model.py UNIT_G_HERO_ARAGORN --json --no-recurse
python resolve_model.py MODEL_E_BEAST_LAIR --json --no-recurse
python lmd.py "../extracted/Models/Units/Good/Aragorn/G_Aragorn.lmd" --json
```

The resolver's `animation_states` contains every weighted choice. A choice's
`mesh_selections` has one value per mesh slot: `-1` disables the mesh; other
values are retained selectors, whose detailed meaning is still being studied.
`models.indices` and `effects.indices` contain visible attachment indices,
each relative to its own attachment kind. `model_tracks` and `effect_tracks`
contain per-attachment timed keys, including `shown` values and raw flags.
`animation_state_table` and `default_animation_state` come from `ModelClasses`.

The exporter and viewer apply these masks and visibility keys. Aragorn's idle
state selects the sword in its scabbard, while attack-ready selects the drawn
sword. The Uruk-hai missile state toggles the arrow at 0, 0.285, 0.683 and 1.0
of the clip. Shelob's state enables her body and legs despite their default
hidden flags. Building states select construction, healthy, damaged and ruins
meshes; damaged texture swaps are still pending.

The viewer provides state, weighted-choice, gear-level and blood controls.
It starts at the class's declared default state and first positive-weight
choice. Query options: `?state=UNIT_ANIM_STATE_ATTACK_READY`, `?choice=0`,
`?clip=G_Aragorn_Idle_02`, `?t=0.5` (freeze at a normalized time), `?level=2`,
`?blood=1`. Weights are displayed for inspection; this viewer does not sample
choices randomly. Selecting raw skeleton clips uses the default appearance.

Visibility animation is stored in `extras.wotrAppearance` v1, with source slot
indices, mesh-node bindings and normalized STEP keys. Generic glTF viewers
show the default appearance; this project's viewer restores the retained
hidden mesh nodes and switches their visibility. Nested models use their own
default state; independent nested clip playback is pending. Effects, cloth,
unknown property parameters and gameplay flag operations are not rendered.
See `NOTES-model-appearance.md` for the metadata contract and preview policy.

## Database JSON

Export every database table, including its schema, for use by game-rule code:

```bash
python lqr.py export --out-dir ../converted/database
```

Use `--db "/path/to/game/Database"` for another install. The output directory
must be outside the source Database directory. This install exports 154 table
files (24,468 records), plus `index.json` mapping table IDs to JSON filenames.

Each table contains `format_version` (currently 1), `table_id`, `name` (the
internal name, e.g. `Unit`), `prefix`, `source`, `columns` and `records`.
The filename keeps the original table filename's stem, e.g. `Units.json`.
Records keep sparse IDs, symbolic names and editor folders, with game fields
nested under `values` so they cannot overwrite record metadata:

```json
{"id": 16, "name": "UNIT_G_HERO_ARAGORN", "folder": "Root\\\\Good Units\\\\Hero", "values": {"Model": 321, "MaxHealth": 485}}
```

That example shows only two of Aragorn's fields. All fields are exported.
Column schema includes types (`int`, `float`, `string`, `id`, `filename`),
defaults, descriptions, file extensions and editor groups. `kind` is 0 for
plain values, 1 for references, and 2 for enums. Reference columns include
`reference: {"table_id": 3, "table": "Models"}`; enum columns include a list
of `{value, label}` pairs. Values remain the original numbers, including `-1`
sentinels. Follow a reference through `index.json` and match the target record's
`id`, rather than using its array position. Without `Rings.lrp`, table IDs and
referenced table names are `null`; the column's reference table ID is retained.

Files are UTF-8, strict JSON, and deterministic for unchanged source data.
Run exporter regression tests without game assets or additional dependencies:

```bash
python -m unittest -v test_lqr_export
```

Run all parser and exporter regression tests (synthetic fixtures, no game assets):

```bash
python -m unittest discover -v
node --test test-model-appearance.mjs
```

## Status

- H2O: all 23 archives extract, every CRC verified. Stored entries are plain files
  (`.dds` textures, Ogg Vorbis speech/music, WAV effects, Bink `.bik` movies).
- LMS: all 2395 meshes parse, including the v1.0 Grasslands Tree 02.
- LSK: all 247 skeletons parse to the exact byte (164 v2.0, 83 v1.0).
  Merry and Pippin export with 21 and 19 clips, including static poses. Legacy child lists are checked
  against joint parents, and the resolver reads joint names through this parser.
- LWT: all 162 weight files parse. glTF keeps the top 4 influences per vertex.
- Database: all 154 tables export to JSON (24,468 records); all 2108 models resolve to existing files.
- LMD: all 2108 models decode sequentially, including 5576 animation states,
  5998 weighted choices, 411 joint sets and 4788 joint bounds.
- Units: 84 of 85 export with animation (69 unique models, no warnings). Shelob now
  includes her body and 27 leg/head parts; only `UNIT_WILD_BUTTERFLY` lacks a model record.
- Spot-checked in the viewer: Aragorn, Legolas, Gandalf, Uruk-hai, Elf Knight,
  Troll Bonecleaver, Rohan Rider, Witch-king, Frodo, Balrog.

## Conventions worth knowing

- Model scales in `Models.lqr` are absolute: attachments get the ratio to their parent.
- Rigid attachments are authored in their joint's bind orientation (the `.lsk`
  third array), so each joint gets a "frame" node with that rotation.
- A mesh is skinned only if its own flags say so; some heads list weights but are rigid.
- Default attachment visibility comes from the class's idle state. State masks and
  timed keys override the static `.lmd` "shown" flag; that flag is the fallback for
  models without choices. Gear level and blood use named `ModelFlags.lqr` tags.
- Static clips are retained with constant channels, and shorter bone tracks hold
  their last value to the declared duration, keeping visibility timelines aligned.
- Team colour (`Materials/Team Color.lmt`, D3D9 fixed-function stages) is additive and
  unlit: `texture * lighting + mask * teamColour`, and the diffuse is black under the
  mask. Exported as glTF emissive: `emissiveTexture` = `*_Mask.dds`, `emissiveFactor` =
  `TeamColors.lqr` red (48 of 69 unit models). Recolour by setting the material's
  emissive colour; the viewer has a picker (`?team=Blue`).
- `.lmt` materials (v2.0, 624 bytes; read by hand, no parser yet): `LIQDMATL`, f32 version,
  17 floats of `D3DMATERIAL9` at 0x0c, then 4 stage blocks of 0x7c bytes from 0x64:
  i32 editor texture id (-1 none), COLOROP, ALPHAOP, COLORARG1, COLORARG2, COLORARG0,
  ALPHAARG1, ALPHAARG2, ALPHAARG0 (D3D enum values), then address, filter and a 3x3
  texture transform. v1.0 (528 bytes) and v3.0 (2372 bytes) differ and are not decoded.

## Not yet handled

Building texture swaps, animation event payloads, effects (`.lfx`), cloth,
independent nested-model animation, full gameplay flag operations, terrain/levels.
