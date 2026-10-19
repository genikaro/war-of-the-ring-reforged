# War of the Ring Reforged Roadmap

Updated Oct 19, 2026 · @Evgenii

Phases 1 and 2 implementation are complete. All 54 maps decode through EOF, including 752 decals. Helm's Deep and a decal-heavy Birth of the Uruk-Hai scene render and validate. Original-game screenshot comparison remains deferred by the project owner. Phase 3 now has a running Godot 4 C# sandbox (engine chosen Oct 19).

## Current checkpoint

The Godot sandbox launches from the repository root with `./prototype/run.ps1`.
Setup, controls and known limits are in [the prototype README](../prototype/README.md).
Latest checks: 109 Python tests, eight JavaScript tests and 68 synthetic C# checks
pass. Fourteen additional cache checks cover runtime masks matching the initial
snapshot, all ten building centers being
blocked and all six friendly units traversing the four Helm's Deep surfaces
without overlap. The cache contains
1,207 separate surface nodes and 39 terrain entrances, preserving elevation
and 350 mm body support along mesh edges.
Godot builds without warnings or errors; input and combat pass over 420 fixed
ticks. An elevated preview was inspected, and actual Godot right-click dispatch
selects a surface node and terrain beneath it in a separate headless check. The original four sandbox
GLBs validated without errors or warnings. Original-game fidelity remains unverified.

## Next milestone: reliable squad navigation

1. Verify the original walker masks and extend footprint updates to new/moving
   objects. Existing placed obstacles now have independent node/edge masks:
   removal/restoration runs on fixed ticks, preserves overlapping blocks,
   invalidates routes, and rejects restoration over occupied units or segments.
   Alt + right-click toggles an obstacle and its visual in the sandbox.
   Mesh-edge body clearance now checks continuous swept-square support with a
   350 mm radius, preserving slopes and rejecting gaps or another floor's support.
   Static oriented-collision footprints are integrated:
   108 Buildings/IsPathingObstacle placements block 1,741 ground cells. Original
   collision-query formulas remain unverified; this policy is conservative.
   Bridge/walkway geometry now uses a layered grid with continuous triangle
   coverage, height-matched terrain entrances and integer elevation. The stored
   MPL route codes are unused. Exact source-triangle picking selects visible
   navigation surfaces; Ctrl + right-click targets terrain beneath overpasses.
   Narrow routes can be omitted by the one-meter raster.
2. Complete coordinated yielding for opposing traffic and squad spacing.
   Basic congestion replanning is implemented: occupied waypoints wait, routes
   retry around occupied cells after ten blocked ticks, and cleared corridors
   resume. Opposing friendly move orders now use reachable side pockets and
   resume their original destinations. Six-unit gate and opposing-traffic tests
   pass. Formation slots now stay in the destination's connected region rather
   than snapping onto nearby disconnected patches. Region queries update after
   obstacle changes; unreachable replacement orders recenter moving units.
   Corridors without passing space and complex gridlocks remain limited.
3. Connect original animation events to equipment changes, footsteps,
   attack impacts and voices.
4. Package the importer with an in-game installation picker.

Navigation acceptance: send the six friendly units from the clearing through
Helm's Deep's gates, bridges and elevated walkways. The squad must reach
reachable destinations without crossing blocked geometry or overlapping;
unreachable orders must fail cleanly. Cover narrow passages, opposing groups
and repeatable command playback before moving on to the event/audio adapter.

Original-game reference captures can now begin the visual comparison deferred
by the owner until after Phase 2. Phase 4's original stats, combat, economy and
production follow the remaining Phase 3 runtime work.

## Decisions to make

The engine is chosen. Remaining choices can be made as their implementation approaches.

| Decision | Options | Recommendation | Needed before | Status |
| --- | --- | --- | --- | --- |
| Engine | Godot 4 (C#), Unity, a custom C++ engine, Recoil (Spring) RTS engine | Godot 4 with C#: open source, imports glTF directly, and the simulation can live in plain C# for determinism | Phase 3 | Godot 4 with C# chosen Oct 19 |
| How players get the assets | Convert the player's own install on first run, or ship converted files | First-run import from the player's install (the OpenMW model); shipping converted assets is not legal | Phase 3 | Open |
| First playable scope | Skirmish sandbox, or a campaign mission | Skirmish first: it needs no mission scripts or dialogue | Phase 3 | Open |
| Rule fidelity | Match the exe's formulas exactly (disassembly), or approximate from the tables | Tables first for a playable game, then disassemble combat and economy to match | Phase 4 | Open |
| Mission scripts | Host the original 32-bit DLLs behind the same interface, or rewrite each mission in the new engine's script language | Rewrite, using the DLL imports as the API spec; hosting ties the game to 32-bit Windows | Phase 5 | Open |
| Multiplayer model | Deterministic lockstep, client-server, or none at first | Keep the simulation deterministic from day one so lockstep stays possible; build networking last | Phase 3 | Open |

## Phase 1: finish the asset pipeline

**Gate:** every unit and building exports with correct team colour and the right gear in each clip, matched against in-game screenshots.

**Status (Oct 19): implementation complete.** The project owner will capture original-game reference screenshots after Phase 2; the visual comparison is deferred until then and is not marked verified. `converted/phase1-report.json` separates conversion results, source gaps and this deferred gate. Effects are data exports; cloth previews use the initial particle surface, without simulation. See `NOTES-presentation-assets.md` for limits and reproducible checks.

- [x] Apply team colour: additive and unlit, texture × light + mask × team colour (decoded from Team Color.lmt), using the `*_Mask.dds` (744 parts); exported as glTF emissive, the viewer has a team colour picker
- [x] Decode the `.lmd` animation state list, to get per-clip attachment visibility (sword drawn or sheathed) and building states (construction, healthy, damaged, ruins) — 5576 states / 5998 weighted choices in all 2108 models; resolver exposes named states, clips, mesh selectors and attachment timelines; see `NOTES-animation-states.md`
- [x] Replace the inferred default-gear rules with the decoded ones — GLB default scenes use the declared idle state; the viewer switches state/choice meshes and attachments, including timed visibility; named level/blood preview settings replace mask-frequency and filename guesses; Shelob now exports (84/85 units); see `NOTES-model-appearance.md`
- [x] Parse v1.0 skeletons (83 files, 15 units incl. Merry and Pippin) and the one v1.0 mesh — all 247 skeletons and 2395 meshes parse; Merry and Pippin export with 21 and 19 clips respectively, including static poses; see `NOTES-legacy-assets.md`
- [x] Export buildings: models with several state meshes and damaged texture swaps. Done: `lms2gltf.py buildings` exports 41/41; the choice's second key group turned out to be per-stage texture selection (construction, healthy, damaged, ruins; ruins drop team colour), applied in the GLB default scene and the viewer; see "Texture keys" in `NOTES-animation-states.md`
- [x] Decode the serialized effects/projectile/UV/cloth formats and export their data: 405 effects, 37 projectiles, 1027 UVAs, 2 texture sequences and 36 cloths parse to EOF; known resource references resolve, unnamed parameters are retained. Projectiles have 33 model previews plus 4 data-only definitions. Building GLBs include initial cloth surfaces, team masks and state visibility; 109 model cloth attachments, 114 collision volumes and 38 groups decode. All 142 unique unit/building/projectile GLBs pass Khronos validation. Particle rendering, cloth simulation and full unnamed-property semantics remain engine work; see `NOTES-presentation-assets.md`.
- [x] Find where clip events live (footsteps, hit frames, voice cues); candidates are the `SoundEvents` and `SpeechFXEvents` tables and the `.lmd` state list. Done: a per-model event table after the attachments, referenced by each choice's event indices; 1936 events in 122 models decode, with footstep joints, melee hit joints, SoundEvents, SpeechFX voice types, effects, projectiles and attachment show/hide named; see "Clip events" in `NOTES-animation-states.md`
- [x] Export the database tables to JSON (one file per table) as the data source for later phases — `lqr.py export --out-dir ../converted/database`; 154 tables, 24,468 records, schema and reference index included

## Phase 2: maps and terrain

**Gate:** one campaign map (Helm's Deep is a good first target) renders in the viewer with terrain, textures, every placed object and water.

**Status (Oct 19): complete.** Helm's Deep renders with all 767 model placements (93 models), 5,720 painted ground tiles and both lakes. Four navigation-only helpers are retained but hidden. Birth of the Uruk-Hai adds a second rendered scene with ten decals. Both GLBs pass Khronos validation with zero errors/warnings; 82 Python and eight JavaScript tests pass. `converted/phase2-report.json` audits maps through EOF, all decal projections, mesh adjacency/route checks and current screenshots. Two cyclic source bridge matrices are reported for rebuilding from geometry during runtime work; see `NOTES-world-scenes.md`.

- [x] Map the contents of `Levels.H2O` (54 files, 102 MB unpacked): all are `.wor` containers with embedded terrain and world records; `wor.py` inventories metadata, CRC-checked elevation grids, interned names and opaque ranges, with minimap/elevation previews; 54/54 maps, 64,639 names; texture layers and placement payloads remain the following tasks; see `NOTES-levels.md`
- [x] Read the shipped `RingsEditor.chm` for the level concepts and terminology — terrain palettes/vertex colour, separate walker/building passability, walkable meshes, painted lakes, placeables, markers, camera margins, lights and sound lines summarized in `NOTES-levels.md`
- [x] Finish terrain overlays/decals: all shipped secondary layers are empty; transitions are in primary textures. All 51 editor and 701 object decals resolve and project onto terrain. Rotated rectangles clip to source triangles and preserve height, tint and opacity; custom secondary painting and dynamic shader behavior remain explicit runtime limits.
- [x] Decode object placement and link each placed object to its model — 59,850 placements across 54 maps resolve, including saved ModelEvents variants; another 1,022 placed effects resolve as data.
- [x] Decode navigation geometry/indexing: ground-category grids, masks and region/portal graphs are retained; all 17 MPL dimensions match LMS triangles after excluding footer flag bit 1. Edge codes and destination-major indexing are proven; 583,802 pairs pass across 15 resources. Two cyclic source bridge tables are flagged, with adjacency retained for rebuilding. Ground graph query/clearance behavior belongs to Phase 3.
- [x] Export a map as one glTF scene and view it — `wor2gltf.py` exports Helm's Deep with shared mesh/image resources, separate placement nodes, painted lake surfaces and a detailed JSON companion; browser preview and zero-error/warning validation saved.

## Phase 3: engine prototype

**Gate:** a skirmish sandbox on one map where you select units, order them to move and attack, and they path, animate and fight with placeholder rules.

**Status (Oct 19): first Godot C# sandbox runs.** `prototype/run.ps1` builds and launches Helm's Deep with twelve animated units, selection/move/attack controls, camera controls and a simple opponent. A separate integer 20 Hz core passes 15 checks; actual Godot input dispatch, map/model loading and 420 simulation ticks pass the smoke test. This is an early playable milestone: complete navigation/avoidance, original animation-state event adaptation, audio and importer packaging remain open. See `prototype/README.md` (one folder above `tools/`).

- [x] Settle the engine: Godot 4 with C#; current local cache is generated from the owner's install, with no assets in source control. Production asset-delivery packaging remains open.
- [ ] Package the first-run importer: `prepare_prototype.py --install` now extracts/verifies source archives and writes a hashed runtime cache; in-game install selection and production packaging remain.
- [x] Load a map scene and units; RTS camera (pan, zoom, rotate), selection box, move and attack orders
- [ ] Complete animation state adaptation: initial idle/run/attack/death clips play; original attachment, texture and event timelines still need the Godot adapter.
- [ ] Complete navigation: layered A*, mesh entrances/elevation, swept-body mesh-edge clearance, removable/restorable placed-object footprints, deck/underpass picking, congestion replanning and cooperative yielding work; six-unit traversal of all four Helm's Deep walkable helpers passes. Original mask semantics, new/moving footprints and complex gridlocks remain.
- [x] Fixed-step integer simulation separate from rendering: 20 Hz core, tick commands, stable ordering and repeatability tests; lockstep networking remains later work.
- [ ] Audio: Ogg speech and music, WAV effects, triggered by clip events from Phase 1

## Phase 4: game rules

**Gate:** a skirmish against a scripted opponent that plays like the original: same costs, build times, damage outcomes and unit counters.

- [ ] Load unit, weapon, building, spell and technique stats from the JSON tables (`Units`, `Weapons`, `Buildings`, `Spells`, `Techniques`, `UnitToUnitModifiers`, `BattleGear`, `HeroExperiences`)
- [ ] Economy: resources, workers, building and training queues, places of power
- [ ] Combat: damage types, armour, unit-to-unit modifiers, projectiles, morale or knockback where the original has them
- [ ] Heroes, fate powers and spells (`SpellAndEventAndActions` chains events to actions)
- [ ] Open `Rings.exe` in Ghidra to recover the formulas the tables only feed: damage, hit chance, experience, upgrade effects
- [ ] Record reference fights in the original game (it still runs) and compare outcomes

## Phase 5: AI and campaign

**Gate:** the first campaign mission (Light Side tutorial 1) plays start to finish, with objectives, dialogue and cinematics.

- [ ] Skirmish AI from the `.rai` personalities and the AI tables (`AIWarParties`, `AIUnitDescriptors`, `AIBuildingClasses`)
- [ ] List every `BattleScriptInterface` function the 26 mission DLLs import, with its signature, as the scripting API spec
- [ ] Implement that API in the new engine: objectives, triggers, camera paths, cinematics, dialogue, war parties
- [ ] Rewrite the missions one by one, starting with the three Light Side tutorials (disassembly of each DLL as the reference)
- [ ] Dialogue and subtitles from `Dialogue.lqr` and the Ogg speech files; text from `Text.H2O` (this install is the Russian localization; an English install would add English text)
- [ ] Movies: play the two Bink files through FFmpeg, or convert them once at import

## Phase 6: multiplayer and polish

**Gate:** two players finish a skirmish over the internet without desyncs, and the game runs at native resolution on current hardware.

- [ ] Deterministic lockstep networking to replace the defunct GameSpy service
- [ ] Replays (the original `.wrr` format is a reference for what a replay stores)
- [ ] Interface: rebuild menus and the in-game console from `Screens.H2O`, `Fonts.H2O`, `Cursors.H2O` and the interface textures
- [ ] Settings, hotkeys (`Hotkeys.lqr`, `Controls.lqr`), widescreen and high-DPI, controller support if wanted
- [ ] Modding: load extra content from folders so the community can extend it

## Risks

The biggest risk is the game logic: assets are data and decode cleanly, but rules, AI and missions are compiled code.

| Risk | Fallback |
| --- | --- |
| Combat and economy formulas take too long to recover from `Rings.exe` | Ship rules approximated from the tables, tuned against recorded fights, and refine later |
| The `.lmd` state list stays undecoded | Keep the inferred default-gear rules plus a small manual override table |
| 26 compiled missions are too much to rewrite | Release skirmish first; port missions one at a time |
| Legal exposure from assets | Never distribute assets or converted files; only the importer that runs on the player's own copy |
| Only a Russian install is at hand | Text and speech from an English install, imported the same way |
| Floating-point differences break lockstep | Fixed-point or strictly controlled float maths in the simulation from Phase 3 |

## Resume kit

Everything lives in the project folder `war-of-the-ring-reforged`; the research report [Reverse Engineering War of the Ring Assets](https://claude.ai/code/artifact/95bab9cd-8ac8-4c5b-bdfb-289bf5b92397) explains each format and how it was found.

| Path | What it holds |
| --- | --- |
| `TLOTR - War of the Ring/` | The original install, read only |
| `extracted/` | Unpacked archives (regenerate with the first command below) |
| `converted/units/` | 84 unit records (69 unique GLBs) plus `index.json`; butterfly source gap recorded |
| `converted/presentation/` | 1507 presentation asset JSON files, typed references and inventory |
| `converted/buildings/`, `converted/projectiles/` | Building GLBs with initial cloth; projectile definitions/previews |
| `converted/phase1-report.json` | Reproducible conversion audit; visual comparison deferred until after Phase 2 |
| `converted/phase2-report.json` | Complete map/terrain/decal audit and two validated scene gates |
| `converted/prototype/` | Local hashed runtime cache, rendered preview and smoke/verification reports |
| `prototype/` | Godot C# project, launch script and integer simulation core; start with `prototype/README.md` |
| `tests/simulation/` | Independent C# navigation, movement, combat and repeatability checks |
| `tools/` | Asset import/export code; start with `tools/README.md`, then `tools/NOTES-database.md` |
| `.claude/launch.json` | The `viewer` server config (Python HTTP server on port 8765) |

Run from `tools/`:

```bash
python h2o.py extract "../TLOTR - War of the Ring/Archives" ../extracted --verify
python lms2gltf.py units --out-dir ../converted/units
python lms2gltf.py model UNIT_G_HERO_ARAGORN --out ../converted/units/MODEL_G_ARAGORN.glb
python resolve_model.py UNIT_E_URUK_HAI
```

The viewer is `http://localhost:8765/tools/viewer.html` once its server runs;
the current alternative is port 8767. To resume development, open a coding
session in this repository and start with the navigation milestone above.
