# WotR asset tools (proof of concept)

Python 3.13 + Pillow; map export also uses NumPy. Run from this folder.

## Current playable prototype

The Godot 4 C# sandbox loads Helm's Deep and 36 animated units in six squads. It
starts framing the units; keys 1/2/3 select the three blue squads. From the
repository root, run `./prototype/run.ps1`; see [setup and controls](../prototype/README.md).
Phase 2 is complete and the movement/spacing prototype is accepted. The three
demo types now have [imported melee stats and timed attacks](../prototype/COMBAT.md),
equipment visibility and impact sounds; original formulas remain partly unverified.
The original console art, scalable HUD,
four bottom-row actions with contextual Attack/A, hidden Move/Stop controls,
queued waypoints and editable groups 1–0 are implemented;
[the roadmap](Roadmap.md) records the sequence and deferred engine work.
Initial reference review is complete;
controlled original-game fidelity validation remains open.

Original-game research references are in the
[reference library](../references/original-game/README.md): 26 visually retail-compatible images,
the manual and addendum, browsable editor help, campaign recording links and
per-file source/checksum records. All 48 prerelease images are excluded;
the [audit](../references/original-game/REFERENCE-AUDIT.md) records the review.
The retained web images and recording candidates are not controlled final-build
baselines. The [gallery](../references/original-game/gallery.html)
opens locally. Controlled, matching original-game captures are still needed
for fidelity validation.
The [prototype comparison](../references/original-game/PROTOTYPE-REVIEW.md)
documents the corrected rendering imports and remaining gaps.

Latest verification (Oct 19, 2026): 129 Python tests, eight JavaScript tests,
243 synthetic C# checks, 31 cache combat/navigation/demo checks, 40
actual command/group/HUD input checks, the
54-map walker mask audit and the Godot
input/combat (including all three blue squads fighting with separated bodies), interpolated movement/turning, deck/underpass picking and obstacle
remove/restore smoke tests pass. Generated
assets, previews, validation reports and portable tools stay in ignored
`converted/`; original and extracted assets are excluded from source control.

## Asset tools

| Script | Purpose |
|---|---|
| `combat_rules.py` | Export traced definitions, fixed-tick melee events and impact sounds for the three demo types. |
| `pcm_audio.py` | Decode selected mono Microsoft ADPCM impact WAVs to PCM without an external codec. |
| `h2o.py` | List/extract Liquid Entertainment `.H2O` archives (v6). Format notes in the module docstring. |
| `blast.py` | PKWARE DCL "implode" decompressor used by H2O. |
| `lms.py` | `.lms` mesh parser (v1.0, v1.1 and v2.0). Format notes in the module docstring. |
| `lsk.py` | `.lsk` skeleton + animation clip parser (v1.0 and v2.0). Format and pose maths in the docstring; verification in `NOTES-legacy-assets.md`. |
| `lwt.py` | `.lwt` skin weight parser. |
| `lqr.py`, `lmd.py`, `resolve_model.py` | Database tables (including JSON export), `.lmd` models, and name to file resolution. See `NOTES-database.md`. |
| `lmd_states.py` | Decoded weighted animation choices, mesh selectors, attachment visibility and property tracks inside `.lmd` models. See `NOTES-animation-states.md`. |
| `lfx.py`, `lpo.py`, `cth.py`, `texture_animation.py` | Effects, projectile timelines, cloth particle grids and UV/texture animations. See `NOTES-presentation-assets.md`. |
| `presentation_assets.py`, `verify_phase1.py` | Export presentation formats with typed dependencies; audit conversion coverage and the remaining visual gate. |
| `lms2gltf.py` | Export to `.glb`: one unit by name, all units, or hand-picked files. |
| `world_scene.py`, `terrain_set.py`, `mpl.py`, `map_decals.py` | Terrain, placements/decals, navigation geometry, lakes, palettes and the world tail. See `NOTES-world-scenes.md`. |
| `wor2gltf.py`, `verify_phase2.py` | Export Helm's Deep as one GLB; audit all 54 maps and the rendered scene. |
| `verify_walker_masks.py` | Audit every map's ground bitsets against its original walker region graph (`converted/walker-mask-report.json`). |
| `prepare_prototype.py` | Build a hashed local runtime cache for the Godot C# sandbox; optionally extract the owner's install. |
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

Buildings work the same way (`Buildings.lqr`); open the viewer with
`?index=../converted/buildings/index.json` to pick them:

```bash
python lms2gltf.py buildings --out-dir ../converted/buildings
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
`events` lists the model's clip events (footsteps, melee hits, sounds, voice
cues, effects, attachment show/hide) with normalized times and resolved names;
each choice's `event_indices` selects the ones its clip fires.

The exporter and viewer apply these masks and visibility keys. Aragorn's idle
state selects the sword in its scabbard, while attack-ready selects the drawn
sword. The Uruk-hai missile state toggles the arrow at 0, 0.285, 0.683 and 1.0
of the clip. Shelob's state enables her body and legs despite their default
hidden flags. Building states select construction, healthy, damaged and ruins
meshes, and their texture keys swap the matching textures (ruins drop the team
colour mask); see "Texture keys" in `NOTES-animation-states.md`.

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
default state; independent nested clip playback is pending. Cloth renders its
initial particle surface with state visibility and separate diffuse/team-mask
UVs; its physical records remain in node extras. Effects retain references,
visibility and event metadata. Particle/cloth simulation, unknown property
parameters and gameplay flag operations are not rendered.
See `NOTES-model-appearance.md` for the metadata contract and preview policy.

## Presentation assets and Phase 1 audit

```bash
python presentation_assets.py export --out-dir ../converted/presentation
python lms2gltf.py projectiles --out-dir ../converted/projectiles
python verify_phase1.py
```

All 1507 files in Effects, Projectiles, UVAs, TextureAnimations and Cloths parse
to EOF and export to JSON with known database references. The presentation
exporter returns failure if references are missing: this install has two stale
UI texture references, reported explicitly in its index. The Phase 1 audit
separates source gaps from conversion errors. Projectiles have 33 model GLBs
and four data-only definitions; the viewer can use `?index=../converted/projectiles/index.json`.
GLBs preview models and initial cloth, while JSON retains the parameters for a
future simulator. See `NOTES-presentation-assets.md` for layouts and limitations.

`converted/phase1-report.json` records conversion coverage and the earlier
deferred screenshot gate. The initial prototype reference review is now in the
reference library; controlled fidelity validation remains open.
Phase 1 implementation is complete. Independent glTF validation and
its content-hash checks are documented in the notes.

## Level inventory

Inventory the extracted levels or read `Levels.H2O` directly, without changing
the game install:

```bash
python wor.py inventory --out-dir ../converted/levels --previews
python wor.py inspect "../extracted/Levels/Helms Deep.wor"
python wor.py inventory "../TLOTR - War of the Ring/Archives/Levels.H2O" --out-dir ../converted/levels-from-archive
```

All 54 `.wor` containers have metadata, a 128×128 minimap, a compressed terrain
grid and an interned name pool. The inspector verifies terrain sizes and CRCs
and writes `index.json` plus per-map JSON under `maps/`. `--previews` adds PNGs
under `minimaps/` and `heights/`; elevation previews are normalized grayscale,
while the Python API retains the float heights. Archive input also verifies
H2O entry CRCs. Invalid maps are reported individually and cause a failing exit.

The JSON includes byte ranges and the complete indexed name pool. Opaque
terrain/environment and world-record ranges are marked explicitly. Waypoint
prefixes are naming hints; positions, typed objects, terrain layers, water and
passability are still to be decoded. See `NOTES-levels.md` for evidence and the
editor guide's level terminology.

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
  5998 weighted choices, 411 joint sets and 4788 joint bounds, then cloth
  attachments, 5622 bounding boxes, 109 cloth attachments, 114 cloth collision
  volumes, 38 groups and 1938 clip events. Every model reaches its event table.
- Buildings: 41 of 41 export (40 unique models, 36 with texture keys). One
  warning: two Dark Foundry ore parts name an attach joint their skeleton lacks.
  Cloth exports initial particle surfaces with atlas UVs, team masks and state visibility.
- Presentation assets: 405 effects, 37 projectiles, 1027 UVAs, 2 texture sequences
  and 36 cloths export structured JSON. All 142 generated unit/building/projectile
  GLBs pass Khronos validation with zero errors and warnings.
- Levels: all 54 maps decode through EOF; 59,850 model
  placements, 1,022 effects and 752 decals resolve. Helm's Deep exports 105 ground textures,
  all 767 placement nodes and two lakes; four navigation helpers are retained
  but hidden. Birth of the Uruk-Hai adds a validated scene with ten decals.
  MPL edge/index semantics are verified; two cyclic source tables are flagged.
  See `NOTES-world-scenes.md` for commands and limits.
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

Particle/trail rendering, cloth wind/collision simulation, unnamed effect and
gameplay parameters, unnamed animation-event types, independent nested-model
animation, full gameplay flag operations, custom maps with secondary terrain painting,
ground graph query semantics and runtime marker/camera/sound behavior.
Decals render on terrain at initial size; dynamic resizing/fading and projection
onto water/objects remain runtime work. Two source bridge MPL matrices contain cycles.
Water previews preserve painted surfaces and opacity without animated shaders.
Phase 1's controlled original-game screenshot comparison remains unverified;
the prototype's initial review against the collected references is documented
in the reference library.
