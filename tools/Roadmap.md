# War of the Ring Reforged Roadmap

Updated Oct 19, 2026 · @Evgenii

Phases 1 and 2 implementation are complete. All 54 maps decode through EOF, including 752 decals. Helm's Deep and a decal-heavy Birth of the Uruk-Hai scene render and validate. An initial reference review has corrected prototype rendering bugs; controlled original-game fidelity validation remains open. The user has accepted Phase 3's movement and combat-spacing baseline. The command panel and explicit orders are implemented. The next milestone is the first data-driven combat slice in the Godot 4 C# sandbox (engine chosen Oct 19).

An [original-game reference library](../references/original-game/README.md) now
contains 74 saved images, the original manual and addendum, editor help and
campaign recording links, with provenance and checksums. Prerelease and
unversioned references are labeled; collecting them does not complete the
controlled original-game screenshot comparison or verify gameplay fidelity.

The [prototype reference review](../references/original-game/PROTOTYPE-REVIEW.md)
corrects missing runtime mipmaps, ignored lake vertex colours, spawned team
colours and selection disks. F1/F2/F3 views and capture metadata make further
comparisons repeatable. Lighting, weather, water animation, original UI and
animation-event/appearance adaptation remain open.

## Current checkpoint

The Godot sandbox launches from the repository root with `./prototype/run.ps1`.
Setup, controls and known limits are in [the prototype README](../prototype/README.md).
Latest checks: 120 Python tests, eight JavaScript tests and 217 synthetic C# checks
pass. Twenty additional cache checks cover runtime masks matching the initial
snapshot, all ten building centers being
blocked and all six friendly units traversing the four Helm's Deep surfaces
without overlap, plus opposing six-unit squads swapping between the clearing
and each walkway, plus safe startup for all 36 demo units. The cache contains
1,342 separate surface nodes and 48 terrain entrances, preserving elevation
and 350 mm body support along mesh edges. A 54-map audit
(`tools/verify_walker_masks.py`) matches the painted walker bits to the
original walker region graphs.
Eight-way weighted routes now use straight diagonal motion at the same speed as
cardinal moves, with blocked-corner checks and swept-body crossing reservations.
Wide-ground orders now use full-route any-angle smoothing, next-tick redirects,
rolling body checks, local steering and minimum-travel formation assignment.
The six-unit open-field fixture finishes in 211 ticks versus 274, with no waits.
[Movement notes](../prototype/MOVEMENT.md) document the benchmark and limits.
Single-file and mesh routes retain constrained coordination; deck links
remain eight-way. Rendering follows the recorded path between fixed ticks and turns
gradually. An 80 FPS motion smoke check verifies interpolation and turning with
the same 20 Hz simulation, input and combat checks.
Godot builds without warnings or errors; input and combat pass over 420 fixed
ticks. An elevated preview was inspected, and actual Godot right-click dispatch
selects a surface node and terrain beneath it in a separate headless check. The original four sandbox
GLBs validated without errors or warnings. Original-game fidelity remains unverified.

Combat now reserves individual reachable melee and waiting positions, maintains
allied spacing, holds settled fighters still and faces them toward their targets.
Moving/blocked targets trigger approach replanning, and surviving attackers can
continue onto nearby enemies. Ground approaches use the any-angle follower when
supported. Three six-unit squads per side populate the demo, with a unit-focused
default camera and 1/2/3 squad selection. A separate Godot combat smoke check
requires all three friendly squads to fight without overlaps.

The selected-unit command panel now exposes Move, Attack, Attack-move, Stop and
Hold Ground, plus Guard, Patrol and Force Attack. The visible bottom row is
Attack, Hold, Guard, Patrol. A single Attack/A action resolves to pursuit,
force attack or attack-move based on enemy, neutral/friendly or ground targets.
Move and Stop remain available without visible buttons; F/B shortcuts are removed.
Original console art, portraits and command/cursor assets are traced through
source UVA crops into the hashed local cache. The console scales proportionally
with height, supports a saved 75–150% size setting and fits 4K/ultrawide displays.
Editable groups 1–0 support Ctrl assignment, Shift additions, recall, lead-unit
centering and death pruning. Shift-click queues formation waypoints; queued
orders are recorded and replayed. Seventeen new actual Godot input checks cover
these additions, alongside 23 command checks. Explicit intent survives temporary
combat routes; Attack-move resumes after combat, force moves ignore retaliation,
and stationary orders never chase. Names, health, tooltips, active orders and
disabled commands are visible. Twenty-two actual Godot input checks cover all
three blue squads, cancellation, pause/resume and dead selections. The
[command notes](../prototype/COMMANDS.md) record behavior, original references,
intentional shortcut/layout differences and remaining limits. Stats and hit
timing still use prototype combat.

## Accepted baseline: movement and combat spacing

**User acceptance (Oct 19):** movement, group pathfinding and placeholder combat
spacing are good enough until actual combat and the command interface are
implemented. Keep the six-squad demo and unit-focused default camera. Preserve
the 171 simulation checks and existing Godot checks as regression coverage.

This accepts the prototype milestone, not complete navigation or original-game
combat fidelity. Further movement/spacing polish is deferred. Revisit it only
for regressions or when verified combat ranges, unit sizes or new gameplay
actually require a change.

## Next milestone: unit commands and first data-driven combat

**Goal:** use the command panel to control a small battle between the existing
Aragorn, Elf Knight and Uruk-hai types, with distinct imported stats and attacks
whose damage lands at the appropriate point in the animation.

1. **Implemented: explicit orders and a functional command panel.**
   - Introduce recorded order kinds for Move, Attack, Attack-move, Stop and Hold
     Ground. Keep the existing right-click controls and route buttons, hotkeys
     and contextual clicks through the same fixed-tick command path.
   - Define order state separately from the current target and path. Stop clears
     the current order/pursuit; Hold Ground fights within reach without chasing;
     Attack-move resumes its destination after an engagement. Specify explicit
     movement versus automatic retaliation, target death and order replacement
     so combat cannot accidentally discard the player's intent.
   - Build the selected-unit command panel with names, health, tooltips, shortcut
     hints and visible active/available commands. Check the original manual,
     addendum, `Hotkeys`, `Controls` and source interface assets for layout,
     labels, bindings and behavior before calling them faithful. Guard, Patrol,
     basic queues, control groups and original console artwork are now implemented;
     hero abilities and full interface reconstruction remain subsequent work.
   - Verify cancellation during combat, Hold versus chasing, Attack-move
     resumption, mixed selections, dead units and identical replay state for
     equivalent button/hotkey/contextual orders.
2. **Replace the three demo types' placeholder stats with imported definitions.**
   - Resolve `Units` to `Weapons`, `DamageTypes`, `UnitToUnitModifiers` and model
     data. Export a minimal hashed runtime rules cache from the owner's existing
     data. Keep source IDs/provenance; quantize values at the adapter boundary
     so the simulation remains integer-only and independent of Godot.
   - Start with base health, armor, melee weapon, damage classification, range
     and the supported attacker limit. The source `Units` records already give
     base health of 485 for Aragorn, 350 for Elf Knight and 240 for Uruk-hai;
     all three currently use the prototype's 100 HP instead.
   - Recover or explicitly document each field's interpretation. The linked
     melee weapons store `CuttingDamagePerSecond`, not per-hit damage. Their
     `MaximumRange` values are 1.0 for Aragorn and 0.5 for the other two: establish
     world scale, body-radius and range-origin semantics before using these as
     center-to-center reach. Do not shrink the accepted spacing based on an
     unverified interpretation.
3. **Add the deterministic melee attack lifecycle and matching presentation.**
   - Model attack preparation, hit timing, recovery, cancellation, target death
     and retargeting. Consume the decoded `.lmd` melee-hit events and damage
     scales through a runtime adapter; convert clip times to fixed simulation
     ticks with a documented rounding rule.
   - The simulation owns hit resolution and damage. Animation/render callbacks
     observe combat state and cannot cause damage or alter simulation timing.
   - Apply the supported armor, damage-type and unit-to-unit rules, labeling
     unresolved formulas as approximations. Compare small controlled duels with
     the original game; use targeted executable analysis where tables and
     recordings cannot establish a formula.
   - Connect the required attack-ready/weapon visibility states. Add the small
     set of hit/attack sounds needed to read these fights; the complete event,
     effects, cloth and voice adapters remain subsequent work.
4. **Validate the complete slice in the current demo.**
   - Exercise all initial orders through the actual UI across the three blue
     squads. Retain safe movement, waiting attackers and deterministic command
     playback while health and damage differ by unit type.
   - Save repeatable duel scenarios, damage/hit-timing evidence and a Godot
     command/combat report. Keep original-reference comparisons distinct from
     implementation regressions and record any unverified formulas.

**Acceptance:** a player can select a squad, issue Move/Attack/Attack-move/Stop/
Hold Ground through the command panel or its shortcuts, and see those orders
behave consistently during a battle. The three existing types use traced source
definitions; damage is synchronized with simulation attack timing, and the same
order log replays identically. No navigation polish or full economy system is
required to accept this slice.

**Following milestones:** one ranged unit with projectile/hit resolution, then
hero commands/abilities and more unit counters; follow with workers, resources,
construction and training queues to form a complete skirmish loop. Full UI art,
first-run installer packaging, campaign, multiplayer and broad presentation polish
remain later work. Do not expand this first slice to all 85 unit records.

## Deferred navigation and engine work

1. Extend footprint updates to new/moving objects and settle the corner
   diagonal policy. The walker masks are verified: on all 54 maps the original
   walker region graph covers exactly the painted bits, and its links equal
   eight-way adjacency including corner-only diagonals. mask_b is the editor
   input before slope, water and outer-frame exclusions; mask_c forces cells
   passable. The prototype still requires both flank cells for a diagonal,
   which leaves 66 Helm's Deep cells (2,192 on 43 maps) outside the original's
   main area. Existing placed obstacles now have independent node/edge masks:
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
   navigation surfaces; Ctrl + Alt + right-click targets terrain beneath overpasses.
   Walkway cells whose center cannot hold the body take the nearest supported
   point within 400 mm (135 nodes; 129 more walkway nodes now reachable), kept
   700 mm from other nodes and links. Paths narrower than the body stay closed.
2. Extend the accepted coordination only when later gameplay exposes a need.
   Basic congestion replanning is implemented: occupied waypoints wait, routes
   retry around occupied cells after ten blocked ticks, and cleared corridors
   resume. Opposing friendly move orders now use reachable side pockets and
   resume their original destinations. Six-unit gate and opposing-traffic tests
   pass. Formation slots now stay in the destination's connected region rather
   than snapping onto nearby disconnected patches. Region queries update after
   obstacle changes; unreachable replacement orders recenter moving units.
   Opposing three-unit squads pass wide bays and single-entry three-cell pockets,
   including reversed orders, cancellation and recorded command playback.
   Movement endpoints stay reserved in transit. Refuges fill from the back,
   remain stable while approached, and can relocate after a route changes.
   Extra formation slots avoid nearby cut points so arrivals leave room for
   other traffic; idle yielders settle nearby if their old spot is claimed.
   Larger gridlocks resolve through chained refuges past standing units, head-on
   detection at junctions, refuge replacement and early release. Opposing
   six-unit squads clear a 12-cell single-file corridor, three squads share it,
   four squads cross a single-file junction, a squad clears idle units out of
   a corridor, 60 random order sequences for 6 to 10 units finish, and six-unit
   squads swap on all four Helm's Deep walkways. Corridors without passing space
   wait safely; dense orders that park units inside single-file passages others
   must cross can still wait. Continuous movement on mixed wide/narrow routes
   and elevated surfaces remains open; the larger dense stress suite needs
   revalidation after this movement pass.
3. Complete the remaining animation/presentation event adapter after the melee
   subset: all equipment/texture changes, footsteps, effects and voice events.
4. Package the importer with an in-game installation picker for distribution.

Navigation regression coverage: send the six friendly units from the clearing through
Helm's Deep's gates, bridges and elevated walkways. The squad must reach
reachable destinations without crossing blocked geometry or overlapping;
unreachable orders must fail cleanly. Existing narrow-passage, opposing-group
and repeatable-playback checks stay enabled during command/combat work.

Controlled original-game captures are still needed after the initial reference
review. Begin the command/combat slice now; deferred navigation refinements,
complete audio/appearance adaptation and importer packaging do not block it.

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
| Ground corner diagonals | Keep requiring both flank cells, or allow corner-only diagonals between painted cells like the original region links | Allow them across unpainted terrain but not across obstacle footprints, so reachability matches the original | Phase 3 | Open |

## Phase 1: finish the asset pipeline

**Gate:** every unit and building exports with correct team colour and the right gear in each clip, matched against in-game screenshots.

**Status (Oct 19): implementation complete.** The initial prototype review against collected references is documented in the reference library. Controlled, matching original-game captures are still needed; the fidelity gate is not marked verified. `converted/phase1-report.json` retains the earlier conversion audit and its deferred visual gate. Effects are data exports; cloth previews use the initial particle surface, without simulation. See `NOTES-presentation-assets.md` for limits and reproducible checks.

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

**Status (Oct 19): playable baseline accepted; console and basic orders implemented.** `prototype/run.ps1` launches 36 animated units in six squads with original console art, a scalable four-button bottom row (Attack, Hold, Guard, Patrol), target-dependent Attack/A, hidden Move/Stop controls, editable groups and queued waypoints. The integer 20 Hz core passes 217 synthetic checks plus twenty map/demo checks; 40 command/group input checks and existing combat/motion/picking checks cover actual Godot dispatch. The user accepts current movement and spacing. Advanced navigation, complete animation-state/event adaptation, audio and importer packaging remain deferred; proceed to the Phase 4 combat-data and hit-timing slice. See `prototype/README.md`.

- [x] Settle the engine: Godot 4 with C#; current local cache is generated from the owner's install, with no assets in source control. Production asset-delivery packaging remains open.
- [ ] Package the first-run importer: `prepare_prototype.py --install` now extracts/verifies source archives and writes a hashed runtime cache; in-game install selection and production packaging remain.
- [x] Load a map scene and units; RTS camera (pan, zoom, rotate), selection box, move and attack orders
- [ ] Complete animation state adaptation: initial idle/run/attack/death clips play; original attachment, texture and event timelines still need the Godot adapter.
- [x] Establish prototype navigation and combat spacing (accepted): eight-way layered A*, any-angle wide-ground movement, immediate redirects, formation assignment, mesh entrances/elevation, swept-body clearance, off-center walkway nodes, removable/restorable footprints, deck/underpass picking, congestion replanning, chained yielding and individual combat/waiting positions work. Six-unit traversal and six-versus-six walkway swaps pass. Advanced cases remain in the deferred navigation section; full original-game pathing fidelity is not claimed.
- [x] Implement the selected-unit command panel and explicit Move/Attack/Attack-move/Stop/Hold Ground states, with shared button/hotkey/contextual dispatch, independent intent, cancellation, health, tooltips and replay checks. The combat-data adapter remains Phase 4 work.
- [x] Fixed-step integer simulation separate from rendering: 20 Hz core, tick commands, stable ordering and repeatability tests; lockstep networking remains later work.
- [ ] Audio: Ogg speech and music, WAV effects, triggered by clip events from Phase 1

## Phase 4: game rules

**Gate:** a skirmish against a scripted opponent that plays like the original: same costs, build times, damage outcomes and unit counters.

**Next slice:** complete the combat-data and attack-timing parts of the milestone
above for the three existing demo types. Establish supported data semantics before
expanding to ranged combat, powers, the economy and production.

- [ ] Load unit, weapon, building, spell and technique stats from the JSON tables (`Units`, `Weapons`, `Buildings`, `Spells`, `Techniques`, `UnitToUnitModifiers`, `BattleGear`, `HeroExperiences`)
- [ ] Economy: resources, workers, building and training queues, places of power
- [ ] Combat: attack preparation/hit/recovery and interruption, damage types, armour, unit-to-unit modifiers, projectiles, morale or knockback where the original has them
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
- [ ] Interface: complete menus and original in-game console art from `Screens.H2O`, `Fonts.H2O`, `Cursors.H2O` and interface textures; functional unit commands and their panel move earlier into the next command/combat milestone
- [ ] Settings, hotkeys (`Hotkeys.lqr`, `Controls.lqr`), widescreen and high-DPI, controller support if wanted
- [ ] Modding: load extra content from folders so the community can extend it

## Risks

The biggest risk is the game logic: assets are data and decode cleanly, but rules, AI and missions are compiled code.

| Risk | Fallback |
| --- | --- |
| Combat and economy formulas take too long to recover from `Rings.exe` | Ship rules approximated from the tables, tuned against recorded fights, and refine later |
| Weapon damage-per-second, range and animation-event fields are misinterpreted | Trace table descriptions and executable behavior, compare controlled original-game fights, and label approximations; keep the integer rules adapter separate from rendering |
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
| `converted/walker-mask-report.json` | 54-map audit of ground bitsets against the original walker region graphs |
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
