# WotR asset tools (proof of concept)

Python 3.13 + Pillow; map export also uses NumPy. Run from `tools/`.

## Current playable prototype

The Godot 4 C# sandbox loads Helm's Deep and 36 animated units in six squads. It
starts framing the units; keys 1/2/3 select the three blue squads. From the
repository root, run `./prototype/run.ps1`; see [setup and controls](README.md).
Phase 2 is complete and the movement/spacing prototype is accepted. The three
demo types now have [imported melee stats and timed attacks](COMBAT.md),
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
| `frontend_assets.py` | Build-147: menu art, the Immortal bitmap font (`LIQDFONT`), the Mordor sky panorama and the Barad-dûr menu scene (`MenuScene.wor`) into `converted/frontend` for the main menu, setup and loading screens ([MENUS.md](MENUS.md)). |
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
| `formats_fingerprint.py` | Build-192: every archive file's path, size, packing, CRC and SHA-256 and every table as `lqr.py export` writes it, for `tests/formats --python` to check the C# readers against (see below). |
| `models_fingerprint.py` | Build-195: every .lms (plain and with the strict footer), .lsk, .lwt and .lmd parse (build-200: and every .lfx and .lpo), hashed in a canonical form, for `tests/formats --python-models`; `--dump` prints one file's form. |
| `gltf_fingerprint.py` | Build-197 (`--pack` build-198: the pack's model files post-processed as the import writes them): every model the content pack exports converted by `lms2gltf.convert_model` from the given install, compared by JSON with accessor bytes and image pixels in place of offsets, for `tests/formats --python-gltf`; `--dump` prints one. |
| `resolve_fingerprint.py` | Build-196: every Models record resolved by `resolve_model.py` from the given install (its own extracted layout in a temporary folder), hashed in the canonical form, for `tests/formats --python-resolve`; `--dump` prints one. |
| `pack_fingerprint.py` | Build-205: the whole content pack built the Python way from the given install (archives extracted into a temporary folder), every file it writes recorded: a PNG by its decoded pixels, a GLB in `gltf_fingerprint`'s form, anything else by its bytes; and the audio and interface indexes whole. For `tests/formats --python-files`. |
| `frontend_fingerprint.py` | Build-212: the menu art exported by `frontend_assets.export` from the given install's extracted archives: the index without its hashes, the font JSON, every picture by its pixels and the menu scene in `gltf_fingerprint`'s form; for `tests/formats --python-frontend`. |
| `scene_fingerprint.py` | Build-210: every multiplayer map's scene exported by `wor2gltf.export` from the given install's extracted archives (a temporary folder), in `gltf_fingerprint`'s form with the extras' source path left out, each top-level key hashed, and `map_lighting.write_images`' pictures by their pixels; for `tests/formats --python-scenes`; `--dump` writes one map's scene whole, for `--compare-scene`. |
| `map_fingerprint.py` | Build-209: every multiplayer map's cache built by `prepare_prototype.prepare_skirmish` from a layout of the given install, without the scene, the clearance bodies from a given `content.json`: the manifest, `heights.bin` and `navigation.json` in pieces (each top-level value, obstacle layer and body profile hashed by value), for `tests/formats --python-maps`; `--dump` writes one map whole, for `--compare-map`. |
| `world_fingerprint.py` | Build-208: every level of Levels.H2O read by `world_scene.parse` and resolved against the install's database, the part the game reads hashed in the canonical form, for `tests/formats --python-worlds`; `--dump` prints one. |
| `combat_fingerprint.py` | Build-203: `combat/rules.json` and `combat/blood.json` as `combat_rules.export` and `blood_effects.export` build them from the given install (its own extracted layout in a temporary folder), for `tests/formats --python-combat` to check `PackCombat` against. Build-213: also the pack's `bodies` (`content_pack.faction_bodies` and the rules' unit bodies). |
| `rules_fingerprint.py` | Build-194: the content pack's seven database-only rule sections as the import builds them, for `tests/formats --python-rules` to check `PackRules` against. Build-199: also the sections that need models (the `models` list, `skirmish.economy`, `skirmish.construction` key by key; build-204 its defenses), from a layout of the install, for `PackModelRules` and `PackCombat.Defenses`. |
| `import_install.py` | Build-188, the launcher's import: check an install parses (`--check`), then extract it, build the content pack, menu art and every map (in parallel) and record it in `converted/install.json`. Progress lines start with `@reforged `. |
| `viewer.html` | three.js viewer with unit and clip pickers (`?clip=Name`, `?t=0.5` freezes a frame). |

## C# readers (build-192)

`prototype/Formats` is the start of reading the install in place, as OpenMW and Daggerfall Unity do, instead of
importing it first. These are ports of the Python readers and give the same results, without Godot, so the game and
the checks share them:

| C# | Python | Reads |
|---|---|---|
| `H2OArchive`, `GameArchives` | `h2o.py` | One archive or every archive of an install. A file is read on request, CRC-checked, by its archive path or by its `extracted/` path (`Models/Units/...`), in any case and with either separator. Reads are positional, so threads can share an archive. |
| `Blast`, `Crc32` | `blast.py`, `zlib.crc32` | DCL-imploded data and zlib's CRC-32. |
| `LqrTable`, `LqrDatabase` | `lqr.py` | Database tables with their schema, the Rings.lrp table ids and reference/enum resolution. Tables load when first asked for. |
| `Ltx` | `game_text.py` `parse_ltx` | Build-193: LIQDTEXT text tables. `H2OArchive.ReadPrefix` decompresses only the start of a file, for headers. |
| `LmsMesh`, `LskSkeleton`, `LwtWeights`, `LmdModel` | `lms.py`, `lsk.py`, `lwt.py`, `lmd.py`, `lmd_states.py` | Build-195: meshes (footer on request), skeletons with clips, skin weights, and models with their states, tracks, attachments, boxes, cloth volumes and clip events. Field names follow the Python's in the checks' canonical form. |
| `PresentationAssets.Lfx`, `.Lpo` | `lfx.py`, `lpo.py` | Build-200: effects (tracks, transforms, atlas descriptors, emitters) and projectiles (keys, behaviour, actions, events, launch sounds), as dictionaries with the Python's keys and value types. |
| `World` | `wor.py`, `world_scene.py` `parse`, `resolve_objects`, `region_cells` | Build-208: a level: header, terrain vertices and heights, name table, terrain layers and environment, placed objects and effects with what they resolve to, pathing bitsets and region graphs, lakes, the tail's lighting, grass, markers and decals. |
| `Install/PackCombat.Bodies`, `CacheManifest.InstallPack` | `content_pack.build`'s `bodies` and the rest of `content.json` | Build-213: the pack's manifest from the install, so the game reads no `converted/content`. |
| `Install/PackFrontEnd` | `frontend_assets.export`: `export_art`, `parse_font`, `export_font`, `export_menu_scene`, `sky_panorama` | Build-212: the menus' art, font, Barad-dûr scene and sky, served through `InstallFiles.UseFrontEnd` in place of `converted/frontend`. |
| `Install/SandboxMap`, `Install/PyRandom` | `sandbox_map.flatten`, `random.Random(str)` and `choice` | Build-211: the Sandbox field cut from Bilbo's Bluffs, its grass tiles scattered by CPython's Mersenne Twister. |
| `Install/MapScene`, `Install/MapPictures` | `wor2gltf.export` with `map_decals`, `world_scene.resolve_decals`, `terrain_set.parse`'s checks; `map_lighting.write_images` with Pillow's Lanczos resize | Build-210: a map's scene as glTF (terrain, water, decals, placed models) and its cloud map and skybox faces, built while a match loads. |
| `Install/PackMap`, `Install/MapNavigation`, `Install/MapGeometry` | `prepare_prototype.prepare_skirmish` (no scene), `build_navigation`, `skirmish_map`, `skirmish_construction.construction_data`'s sites, `prototype_navigation`, `prototype_obstacles`, `prototype_clearance`, `verify_walker_masks.audit_category`, `map_lighting.lighting` | Build-209: a skirmish map's manifest, heights and navigation with every body's clearance, built at load time; `PyMath.Dist` and `FloorDiv` are `math.dist` and float `//`. |
| `Install/ModelResolver`, `PresentationAssets`, `Install/PyMath` | `resolve_model.py`, `cth.py`, `texture_animation.parse_uva` | Build-196: a model record to its files, states, attachments, events and cloth, as nested dictionaries with the Python's keys and `extracted/` file names; cloth grids and texture animations; Python's `round(x, n)`. |
| `Install/ModelGltf`, `Images` | `lms2gltf.convert_model`, `model_appearance.py`, Pillow's DDS and TGA decoding | Build-197: a resolved model as glTF (JSON as dictionaries, the binary buffer, each image's pixels; `Glb()` writes the file), and DXT1/DXT5/TGA decoding with PNG encoding. |
| `Install/PackModels` | `content_pack.build`'s model exports, `prototype_assets.adapt` and `restore_building` | Build-198: the pack's unit, building, place-of-power and rally banner files built from the install as the import writes them; the game loads them from memory. Build-207: also the projectile, effect and dropped-object models the combat rules name. |
| `Install/OriginalText` | `game_text.py` `build` | Build-193: the original text as the game reads it, built from the install at startup ([LOCALIZATION.md](LOCALIZATION.md)). |
| `Install/PackModelRules`, `PyDecimal` | `prepare_prototype.export_units`' models list, `skirmish_economy.economy_data`, `skirmish_construction.construction_data` and `placement_data`, `skirmish_map.building_footprint`, Python's `decimal` | Build-199: the rule sections that need the models: clip names and in-place clips from a walk of the resolved model's skeletons (no glTF is built), worker clip lengths in ticks, building footprints from the oriented-collision boxes. `PyDecimal` is `Decimal(str(x))` with the default context's 28 digits. |
| `Install/PackImages` | `prototype_interface.export`, `healing_visual.export_sprite_effect`'s and `blood_effects`' pictures, `content_pack.export_corruption_tiles` | Build-206: the pack's pictures (crops of atlases decoded as Pillow does) and the interface index, made on request; the game reads them through `InstallFiles`. |
| `Install/PackAudio`, `MsAdpcm` | `game_audio.export`, `pcm_audio.decode_ms_adpcm` | Build-205: the audio index and its files (Ogg copied, PCM WAV copied, Microsoft ADPCM decoded to the byte), and the combat rules' sounds, built on request. |
| `Install/PackCombat` | `combat_rules.export` with `hero_abilities`, `hero_skills`, `dwarf_abilities`, `ranger_abilities`, `elven_abilities`, `nature_abilities`, `evil_abilities`, `evil_upgrades`, `hero_experience`, `healing_visual`, `global_powers`; `blood_effects.export` | Build-203: the combat rules and blood table built from the install, in the background from startup. The files they name (sounds, sprite frames, effect and projectile models) are not written. `PyMath.Hypot` and `PyMath.Sum` are CPython's `math.hypot` and float `sum`. |
| `Install/PackRules`, `Install/SkirmishCatalog` | `content_pack.faction_data`, `prepare_prototype.team_colors`, `skirmish_production`, `skirmish_research`, `places_of_power.export`, `skirmish_ai.export`, the `skirmish_map` lists | Build-194: the content pack's database-only rule sections, built from the install at startup in place of the pack's. Python's rounding is kept (`round` halves to even, `quantize` halves away from zero on the shortest decimal form, `//` floors). While the import still builds the pack, a change to these Python modules or lists needs the same change here; `--python-rules` fails until it is made. |

```powershell
dotnet run --project tests/formats -c Release
python tools/formats_fingerprint.py "Legacy/Game EN" artifacts/fingerprint-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python artifacts/fingerprint-en.json
```

```powershell
python tools/game_text.py --install "Legacy/Game EN" --out artifacts/text-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-text artifacts/text-en.json
python tools/rules_fingerprint.py "Legacy/Game EN" artifacts/rules-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-rules artifacts/rules-en.json
python tools/models_fingerprint.py "Legacy/Game EN" artifacts/models-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-models artifacts/models-en.json
python tools/resolve_fingerprint.py "Legacy/Game EN" artifacts/resolve-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-resolve artifacts/resolve-en.json
python tools/gltf_fingerprint.py "Legacy/Game EN" artifacts/gltf-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-gltf artifacts/gltf-en.json
python tools/gltf_fingerprint.py "Legacy/Game EN" artifacts/pack-en.json --pack
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-pack artifacts/pack-en.json
python tools/combat_fingerprint.py "Legacy/Game EN" artifacts/combat-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-combat artifacts/combat-en.json
python tools/pack_fingerprint.py "Legacy/Game EN" artifacts/files-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-files artifacts/files-en.json
python tools/world_fingerprint.py "Legacy/Game EN" artifacts/worlds-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-worlds artifacts/worlds-en.json
python tools/map_fingerprint.py "Legacy/Game EN" artifacts/maps-en.json --bodies converted/content/good/content.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-maps artifacts/maps-en.json
python tools/scene_fingerprint.py "Legacy/Game EN" artifacts/scenes-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-scenes artifacts/scenes-en.json
python tools/frontend_fingerprint.py "Legacy/Game EN" artifacts/frontend-en.json
dotnet run --project tests/formats -c Release -- --install "Legacy/Game EN" --python-frontend artifacts/frontend-en.json
```

The first runs 50 checks on synthetic archives, tables, text, rule rounding, model files, model resolution, DDS and PNG. `--install` reads and CRC-checks every archive file and
parses every table, with timings (`--sequential` for one thread); `--python` then requires every file's bytes and
record and every table's columns, records and values to equal the Python readers'. On the English install: 23
archives open in about 25 ms, 678 MB of files read in 1.0 s on one thread, the 154 tables parse in under 0.1 s, and
all 13,666 files and 264,243 values match (13,654 files on the Russian install). `--python-text` builds the original
text from the install and requires it to equal `game_text.py --out`'s, key for key: it does on both installs, built in
about 90 ms. `--python-rules` does the same for the rule sections against `rules_fingerprint.py`'s (an integer
there must be an integer here too): all 21 equal on both installs, the seven database-only ones built in about 50 ms
and the fourteen that need models (builds 199, 204 and 207) in about 0.3 s. `--python-models` parses every
model, effect and projectile file and requires each reading to hash as `models_fingerprint.py`'s does: all 7,749
readings of the 5,354 files match on both installs (about 35 s in Python on all cores, 0.1 s in C# on one thread). `--python-resolve` resolves
every Models record and requires it to hash as `resolve_fingerprint.py`'s does: all 2,108 match on both installs.
`--python-gltf` builds the 89 exported models as glTF and requires each to compare as `gltf_fingerprint.py`'s does: all
match on both installs. `--python-pack` does the same for the 89 content pack files with their post-processing, as the
game builds them (about 1 s on all cores). `--python-combat` builds the combat rules and blood table and requires
them to equal `combat_fingerprint.py`'s, section by section: they do on both installs, in about 0.75 s.
`--python-files` builds the pack's files the game builds from the install and requires each to equal what
`pack_fingerprint.py` recorded: the audio index and all 2,514 audio files do on both installs, and (build-206) the
interface index and all 1,265 pictures, and (build-207) all 110 model files. `--python-worlds` (build-208) reads every
level and requires it to hash as `world_fingerprint.py`'s does: all 54 do on both installs. `--python-maps` (build-209)
builds every multiplayer map's manifest, heights and navigation and requires them to equal `map_fingerprint.py`'s: all
32 do on both installs (about 12 minutes in Python on all cores, 17 s in C#). `--time-maps <content.json> [stem]` times
the builds; `--compare-map <stem> <dump> <content.json>` lists every difference against one `--dump`. `--python-scenes`
(build-210) builds every map's scene and lighting pictures and requires them to equal `scene_fingerprint.py`'s: all 32
do on both installs (11 s in C#). Build-211: both records hold the Sandbox too (33 maps), and both checks require it.
`--python-frontend` (build-212) builds the menu art and requires it to equal `frontend_fingerprint.py`'s: it does on
both installs.

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
