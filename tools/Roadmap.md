# War of the Ring Reforged Roadmap

Oct 18, 2026 · @Evgenii

The unit asset pipeline exports 84 of 85 units as animated glTF, including decoded gear visibility; next come the remaining asset gaps and the maps, then a playable skirmish sandbox in a new engine once the engine is chosen.

## Roadmap

&#91;embedded content: roadmap · 6 phases, 1 gate\]

Phases 1 and 2 need only the Python tools and can start now; Phase 3 starts once the engine is chosen, and Phases 5 and 6 can overlap once the rules work.

## Decisions to make

Only the engine choice blocks progress, and only from Phase 3 on; Phases 1 and 2 need no decision.

| Decision | Options | Recommendation | Needed before | Status |
| --- | --- | --- | --- | --- |
| Engine | Godot 4 (C#), Unity, a custom C++ engine, Recoil (Spring) RTS engine | Godot 4 with C#: open source, imports glTF directly, and the simulation can live in plain C# for determinism | Phase 3 | Open |
| How players get the assets | Convert the player's own install on first run, or ship converted files | First-run import from the player's install (the OpenMW model); shipping converted assets is not legal | Phase 3 | Open |
| First playable scope | Skirmish sandbox, or a campaign mission | Skirmish first: it needs no mission scripts or dialogue | Phase 3 | Open |
| Rule fidelity | Match the exe's formulas exactly (disassembly), or approximate from the tables | Tables first for a playable game, then disassemble combat and economy to match | Phase 4 | Open |
| Mission scripts | Host the original 32-bit DLLs behind the same interface, or rewrite each mission in the new engine's script language | Rewrite, using the DLL imports as the API spec; hosting ties the game to 32-bit Windows | Phase 5 | Open |
| Multiplayer model | Deterministic lockstep, client-server, or none at first | Keep the simulation deterministic from day one so lockstep stays possible; build networking last | Phase 3 | Open |

## Phase 1: finish the asset pipeline

**Gate:** every unit and building exports with correct team colour and the right gear in each clip, matched against in-game screenshots.

- [x] Apply team colour: additive and unlit, texture × light + mask × team colour (decoded from Team Color.lmt), using the `*_Mask.dds` (744 parts); exported as glTF emissive, the viewer has a team colour picker
- [x] Decode the `.lmd` animation state list, to get per-clip attachment visibility (sword drawn or sheathed) and building states (construction, healthy, damaged, ruins) — 5576 states / 5998 weighted choices in all 2108 models; resolver exposes named states, clips, mesh selectors and attachment timelines; see `NOTES-animation-states.md`
- [x] Replace the inferred default-gear rules with the decoded ones — GLB default scenes use the declared idle state; the viewer switches state/choice meshes and attachments, including timed visibility; named level/blood preview settings replace mask-frequency and filename guesses; Shelob now exports (84/85 units); see `NOTES-model-appearance.md`
- [x] Parse v1.0 skeletons (83 files, 15 units incl. Merry and Pippin) and the one v1.0 mesh — all 247 skeletons and 2395 meshes parse; Merry and Pippin export with 21 and 19 clips respectively, including static poses; see `NOTES-legacy-assets.md`
- [x] Export buildings: models with several state meshes and damaged texture swaps. Done: `lms2gltf.py buildings` exports 41/41; the choice's second key group turned out to be per-stage texture selection (construction, healthy, damaged, ruins; ruins drop team colour), applied in the GLB default scene and the viewer; see "Texture keys" in `NOTES-animation-states.md`
- [ ] Decode effects (`Effects.H2O`, 405 `.lfx`), projectiles, UV animations (`UVAs.H2O`, `TextureAnimations.H2O`) and cloth (`Cloths.H2O`)
- [x] Find where clip events live (footsteps, hit frames, voice cues); candidates are the `SoundEvents` and `SpeechFXEvents` tables and the `.lmd` state list. Done: a per-model event table after the attachments, referenced by each choice's event indices; 1936 events in 122 models decode, with footstep joints, melee hit joints, SoundEvents, SpeechFX voice types, effects, projectiles and attachment show/hide named; see "Clip events" in `NOTES-animation-states.md`
- [x] Export the database tables to JSON (one file per table) as the data source for later phases — `lqr.py export --out-dir ../converted/database`; 154 tables, 24,468 records, schema and reference index included

## Phase 2: maps and terrain

**Gate:** one campaign map (Helm's Deep is a good first target) renders in the viewer with terrain, textures, every placed object and water.

- [x] Map the contents of `Levels.H2O` (54 files, 102 MB unpacked): all are `.wor` containers with embedded terrain and world records; `wor.py` inventories metadata, CRC-checked elevation grids, interned names and opaque ranges, with minimap/elevation previews; 54/54 maps, 64,639 names; texture layers and placement payloads remain the following tasks; see `NOTES-levels.md`
- [x] Read the shipped `RingsEditor.chm` for the level concepts and terminology — terrain palettes/vertex colour, separate walker/building passability, walkable meshes, painted lakes, placeables, markers, camera margins, lights and sound lines summarized in `NOTES-levels.md`
- [ ] Decode the terrain: heightmap, texture blending (`TerrainSets.H2O`, `TerrainSets.lqr`, `TerrainCompositions.lqr`), decals
- [ ] Decode object placement and link each placed object to its model through the resolver
- [ ] Decode pathing data (`MeshPathLookups.H2O`, walkable meshes, `PathingCategories.lqr`)
- [ ] Export a map as one glTF scene (terrain mesh plus instanced models) and view it

## Phase 3: engine prototype

**Gate:** a skirmish sandbox on one map where you select units, order them to move and attack, and they path, animate and fight with placeholder rules.

- [ ] Settle the engine and the asset-delivery decisions above
- [ ] Turn the Python tools into a first-run importer that reads the player's install and writes the engine's asset cache
- [ ] Load a map scene and units; RTS camera (pan, zoom, rotate), selection box, move and attack orders
- [ ] Animation state machine per unit: idle, walk, run, attack, death, using the original clip names
- [ ] Pathfinding on the decoded walkable data, with unit avoidance
- [ ] Fixed-step deterministic simulation separate from rendering (keeps multiplayer possible)
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
| `converted/units/` | 83 unit `.glb` files plus `index.json` |
| `tools/` | All code; start with `tools/README.md`, then `tools/NOTES-database.md` |
| `.claude/launch.json` | The `viewer` server config (Python HTTP server on port 8765) |

Run from `tools/`:

```bash
python h2o.py extract "../TLOTR - War of the Ring/Archives" ../extracted --verify
python lms2gltf.py units --out-dir ../converted/units
python lms2gltf.py model UNIT_G_HERO_ARAGORN --out ../converted/units/MODEL_G_ARAGORN.glb
python resolve_model.py UNIT_E_URUK_HAI
```

The viewer is `http://localhost:8765/tools/viewer.html` once the `viewer` server runs. To continue, open a Claude session in the project folder and name the phase and task from this doc; project notes for Claude are saved in its memory for that folder.
