# Large-match performance

The October 29 performance pass (build-119; concurrent music work advances the
shared checkout to build-120) addresses retained corpse assets and avoidable
simulation allocations.

Expired corpses previously kept their hidden models, skeletons, animations,
materials and health bars. After the existing 60-second lifetime and two-second
sink, those children and their managed references are now released. Each historical
unit keeps its simulation id and two empty, disabled lookup nodes. One shared
model template per model/team stays available for recruitment and transformations.
Dead units immediately leave the position, attack reservation, transit and target
indices; scripted revival adds them back. Empty target buckets are removed.

Blocked wide-ground movement no longer builds an occupancy map that its retry
does not use. Read-only proximity queries traverse spatial buckets without copying
them into temporary lists. Collision checks and frequently used ability getters
avoid captured delegates. Sandbox dummies now compare their effective unit type
when refreshing their model, avoiding a full model replacement every physics tick.

## Measurements

Release simulation benchmark, imported Sandbox combat rules, 800 ticks (40 game
seconds), durable mixed armies. Local comparison with a separate source snapshot
that restores the previous allocation paths:

| Units | Before, total CPU time | After | Before allocation | After |
| --- | ---: | ---: | ---: | ---: |
| 128 | 1,677 ms | 1,519 ms | 959 MiB | 629 MiB |
| 200 | 3,503 ms | 2,795 ms | 1,758 MiB | 1,098 MiB |

The 200-unit case uses about 20% less CPU time and allocates 38% fewer bytes.
All nine army benchmark digests match. Smaller and choke scenarios have mixed
timing results; these are diagnostic CPU measurements, not rendered FPS claims.
Generated evidence is in `converted/prototype/performance-119-*.json` and `.log`.

The recruitment/death smoke repeats three 64-unit waves. After burial, unit nodes
are 152 / 280 / 408 for 76 / 140 / 204 historical units, exactly two per unit.
Shared template nodes remain at 157 throughout; later recruits still animate.
The 512-unit durable soak passes 4,000 ticks with 41 replay checkpoints and
27.5 million collision/interpolation pair checks. Its measured p95 is 15.35 ms
per simulation tick, so very large battles still limit fast-forward speed.

## Build-121: keep tick work independent of casualty history

A late-match benchmark keeps 200 living units, geometry, orders and duration
identical while appending 0, 1,000 or 10,000 historical casualties. Before this
pass, many tick systems still visited all records and repeated death cleanup.

`UnitRoster` now maintains an active set in stable id order. Cached snapshots
remain safe across recruitment, damage and revival. Recent casualties complete
the existing cleanup phases and any avoidance-lock countdown before retiring.
Retired records remain available by id for projectiles, match statistics and
hero recruitment; reviving a record restores its position in the active order.
Fixtures that append or clear the public historical list rebuild the roster.

Movement, occupancy, effects, orders, regeneration, targeting, population checks
and bot decisions now traverse that active set. The wide-body occupancy path also
avoids an unnecessary historical scan when checking whether all bodies are small.
Place bonuses on ownership changes and hero recruitment history deliberately
continue to include historical records, preserving their existing state semantics.

| Historical casualties | Before CPU time | After | Before allocation | After |
| --- | ---: | ---: | ---: | ---: |
| 1,000 | 2,867 ms | 2,648 ms | 1,255 MiB | 1,090 MiB |
| 10,000 | 4,975 ms | 2,647 ms | 2,738 MiB | 1,090 MiB |

These runs cover 800 measured ticks after 100 warm-up ticks, using the same
imported rules as the earlier benchmark. With 10,000 casualties, measured CPU
time falls 47%, allocation falls 60%, and tick p95 falls from 11.66 to 4.11 ms.
All three before/after digests match. The 6,000-tick, 200-unit attrition soak also
matches the original final digest, with 61 replay checkpoints.

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -c Release -- --late-match-benchmark converted/skirmish-maps/Sandbox converted/prototype/late-match-profile.json
```

Evidence is in `converted/prototype/performance-121-*`. `-Profile` now reports
the active roster size alongside living and historical counts.

## Builds 157-160: rendered frames, unseen units and local scans (November 5)

The owner asked for a check against modern RTS practice. The rules for this pass were:
rendering first, then the simulation; no visual trade-off of any kind (skipping work
for what cannot be seen is fine); every simulation change keeps replay digests
identical, and anything that would change play is proposed separately. The target is
about 300 units (an 8-bot MP08_Rift late game) at 60 FPS at 1x.

| Modern practice | Before this pass | Now |
| --- | --- | --- |
| Fixed-tick deterministic simulation, interpolated views (Supreme Commander, SC2, Recoil/BAR lockstep) | Done: 20 Hz integer ticks, state digests | Unchanged |
| Measure rendered frame-time percentiles in a fixed stress scene | Missing: only an FPS sample every 2 game minutes | Build-157 |
| Only present what can be seen (pause off-screen and fogged units, update on change) | Every unit animated and re-read its appearance every frame | Build-158 |
| Data tables instead of runtime parsing; no redundant engine calls | Appearance JSON walked twice per unit per frame | Build-158 |
| HUD and minimap redraw on change | Rebuilt every frame | Build-159 |
| Spatial partitioning for every neighbour query | Combat used `UnitGrid`; movement checks, slot searches and idle guards scanned everything | Build-160 |
| Budget path and AI work in nodes, spread over ticks | Partly (combat search tokens, replan caps) | Proposal (changes timing) |
| Shared group routes: flow fields or sector/portal graphs (SupCom2, Planetary Annihilation, openage after Emerson) | Per-unit A* | Proposal (changes routes) |
| Simulation on its own thread with snapshots | No | Proposal, if ticks still stall frames |
| GPU crowds: MultiMesh with vertex-animation textures | No | Only if rendering becomes the limit; changes visuals |

### Build-157: the rendered stress benchmark

`./prototype/run.ps1 -StressBenchmark -Units 300` (`StressBenchmark.cs`) opens a window
(never headless) at 1600x900 with vsync off and `--fixed-fps 60`. On the Sandbox, two
mixed armies of N/2 (Gondor Swordsmen, Dwarven Shieldbreakers, Rohan Riders and Elven
Archers against Orc Slashers, Uruk-Hai, Warg Riders and Orc Bowmen, 10,000 health each)
attack-move into each other in the empty strip south of both bases, with 40 of them
selected. After 5 s, phase A frames the fight for 20 s, phase B looks north, away from
it, for 20 s, and phase C runs it at 4x for 20 s. Each frame records wall time, the
time in `_Process` and `_PhysicsProcess`, draw calls, objects, primitives, render CPU and
GPU time and managed allocation. `stress-benchmark-<N>.json` in the cache holds the
p50/p95/p99/max per phase; `-Profile` adds the `SimProfiler` sections
(`stress-benchmark-<N>-profiled.json`).

The armies appear on the first frame of world tick 10, and desktop input is blocked.
Cloud shadows hold still, since the shader's `TIME` also counts loading frames; they
are still drawn, so they cost the same. Godot's random seed is fixed. Every run of the
same build therefore draws the same frames, and the captures at the middle of each
phase (`stress-<N>-build-<B>-<phase>.png`) prove that a presentation change altered
nothing: `python tools/compare_captures.py before.png after.png --ignore 1000,30,1600,95`
reports the differing pixels outside the build/date/FPS line. Two runs of build-157 gave
identical captures.

`-Profile` now also gives frame-time percentiles for each window, writes its lines to
`profile-<time>.log` in the cache, and formats numbers in the invariant culture.
`-ProfileUntil <minute>` stops the match there and prints the state digest at exactly
that tick (build-160). `-Headless` runs any match without a window.

Build-157 baseline (this PC, gl_compatibility):

| Units | A: fight on screen | B: fight off screen | C: fight at 4x | Draw calls A / B | Allocated per frame |
| ---: | --- | --- | --- | --- | ---: |
| 100 | 76.6 FPS, p50 11.5, p95 19.2 ms | 82.3 FPS, p50 10.9 ms | 54.9 FPS, p95 33.3 ms | 2,861 / 1,025 | 1.5 MB |
| 200 | 45.4 FPS, p50 18.5, p95 34.2 ms | 49.4 FPS, p50 17.1 ms | 34.6 FPS, p95 54.5 ms | 4,996 / 1,553 | 2.4 MB |
| 300 | 29.9 FPS, p50 28.5, p95 49.7 ms | 33.3 FPS, p50 25.6 ms | 26.1 FPS, p95 64.9 ms | 7,114 / 2,079 | 3.4 MB |
| 500 | 18.0 FPS, p50 46.7, p95 84.5 ms | 19.8 FPS, p50 41.7 ms | 18.4 FPS, p95 82.3 ms | 11,542 / 3,182 | 5.4 MB |

Looking away from the fight saved almost nothing, although draw calls fell by more than
two thirds: the frame was CPU-bound, not GPU-bound. At 300 units the profiled run spent,
per frame, 7.3 ms in `godot units` (the presentation loops), about 7-8 ms in Godot's own
animation processing and 7-10 ms of render CPU (GPU about 7.8 ms). On top of that, each
simulation tick took about 19 ms, landing in one frame out of three; most of it went to
wide-body combat position searches.

The 8-bot MP08_Rift observer match (Gimli, Aragorn, Legolas, Saruman and four defaults,
4x, windowed) ran at 52-76 ms per frame (13-19 FPS) from 6:00. At 10:00 a tick cost
31 ms, 18 ms of it in `unit orders`.

### Build-158: unseen units and compiled appearances

- **Appearance tables** (`Presentation/AppearanceTable.cs`). Each model's `wotrAppearance`
  JSON is compiled once into arrays. `UnitAppearance.Apply` walks those arrays, and returns
  at once while the clip, state and weapon blood are unchanged and the clip time has not
  crossed the next key. It also remembers what each mesh shows, so Godot only hears about
  changes. `tests/simulation/AppearanceTableChecks.cs` keeps the old JSON walk and
  compares the two on every unit and building model in the pack (73 models, 251,198
  cases, 18,702 span probes). Run it with `--appearance-checks converted/skirmish`.
- **Unseen units** (`Main.UnitPresentation.cs`). A unit is drawn when the viewer can see
  it and it lies inside the camera's view. The view is widened by 30% of the camera
  distance plus 2 m, to cover this frame's pan or zoom step. The unit's shadow tip, along
  the map's sun direction, counts too. An unseen unit's AnimationPlayer switches to
  manual mode and banks the time it misses. The banked time is applied before any Play,
  Seek or speed change, and when the unit comes back into view, so it reappears in the
  pose it would have had. Appearance, seeks, health bars and ring rebuilds wait while a
  unit is unseen. Godot animates drawn units exactly as before. Main stepping them itself
  with `Advance` was tried, and it shifted bone-attached pieces (riders, weapons) by a
  frame against the captures.
- **No redundant engine calls.** Yaw, visibility, ring visibility and animation speed are
  mirrored in `UnitView` and written only on change. Each model's clip-to-animation and
  state-to-clip lookups are cached. Health bars share their quads (one per width and fill
  step) and materials (one per colour), instead of allocating a `QuadMesh` on every
  health change.
- **Presented units only.** The three presentation loops visit living units and unburied
  corpses (`_presented`), not the whole casualty history.

Captures of phases A and B at 100 and 300 units are pixel-identical to build-157, and
draw calls match. At 300 units, phase A went from 29.9 to 31.0 FPS and phase B from
33.3 to 74.6 FPS (p50 25.6 → 8.4 ms). At 100 units, phase B went from 82 to 182 FPS. When
the whole army is on screen, Godot's skeletal animation and rendering dominate the frame,
and invisible savings cannot remove them. In play, most of a large army is usually off
screen or in fog, and those units now cost almost nothing.

### Build-159: console, minimap and per-tick scans

- The console's per-unit text (order readouts, stats, tooltips) is rebuilt only when the
  tick, the selection or the side changes while the game runs. While paused it is
  rebuilt every frame, because the console can change the world without a tick.
- The minimap redraws only when a tick, sight, the camera footprint, markers, colours or
  its size change, or every frame while paused. Fogged minimap terrain is rebuilt as
  whole RGBA pixels instead of a 4-byte block copy per pixel. The bytes are the same; on
  a 512x512 map this was the observer HUD's main cost at 4x.
- Selected units, the allies and enemies counts, friendly heroes, minimap dots and the
  per-tick death scans for blood and speech now read the active roster (same id order)
  instead of the whole casualty history.
- The console's bank and population are set once per physics frame, after the speed
  burst, not after every tick in it.
- New profiler sections: `hud console`, `hud structures` and `hud observer`.

At 300 units, phase A went from 31.0 (build-158) to 38.4 FPS (p50 22.3 ms), phase B to
92.0 FPS and phase C to 30.6 FPS. Allocation fell to 1.4 MB per frame (from 3.4 MB), and
Gen0/Gen1 collections from 81/26 to 34/8 in 20 s. Captures are pixel-identical to
build-157.

### Build-160: local scans in the simulation

Each change gives the same answer as the full scan it replaces, a yes/no (so bucket order
cannot matter) or the same id-ordered list. `SimWorld.VerifyFastPaths` runs both versions
and throws on any disagreement. It is on in the default simulation checks and with
`--verify-fast-paths` on any run; timing benchmarks run without it.

- **Formation slots** (`GridNavigation.CanReserve`). `Nearest`, `NearestSequence` and
  `FormationSlot` tested up to a 49x49 window of cells, each against every node of the
  reserved set: thousands of occupied nodes in a big fight, so millions of distance checks
  per slot search. A set of 32 or more nodes is now bucketed by cell, and a cell only looks
  at reserved nodes within reach (2 body radii, or 700 mm). This was the `unit orders`
  spike: idle guards walking back to their post and guard orders.
- **Constrained movement.** `OccupiedNodes(id).Contains(transit)` became
  `NodeOccupied`, the body-overlap scan became `BodyBlockedAt`, and `CanReserveMovement`
  now uses `UnitGrid` candidates (with every unit on a graph link). The single-node test
  `GridNavigation.BodyCovers` matches `AddBodyNodes` exactly.
- **Idle guards** ask the grid for allies fighting within 10 m (`AllyFightingNear`).
- **Commands for the tick** are found by a binary search over the tick-ordered command
  lists instead of filtering the whole match's history every tick. A list out of tick
  order is still scanned whole.

All 2,698 simulation checks pass with the cross-check on. All nine army-benchmark digests
and the three late-match digests are unchanged. The 36-per-side choke scenario fell from
2,737 to 1,695 ms (p95 5.3 → 2.9 ms); the open-field ones changed little.

The 8-bot MP08_Rift match, headless at 16x, has the same state digest at tick 16,800
(14:00) on both builds: `D19E6B0A…A00B6`.

| Game time (units) | Tick before | After | `unit orders` before | After |
| --- | ---: | ---: | ---: | ---: |
| 06:00 (214) | 7.4 ms | 6.3 ms | 0.93 ms | 0.19 ms |
| 08:00 (257) | 15.1 ms | 11.4 ms | 3.26 ms | 0.51 ms |
| 10:00 (236) | 32.6 ms | 13.0 ms | 19.02 ms | 0.43 ms |
| 12:00 (219) | 28.4 ms | 13.7 ms | 13.93 ms | 0.40 ms |
| 14:00 (218) | 14.7 ms | 11.8 ms | 2.17 ms | 0.35 ms |

The headless run to 14:00 took 3:55 instead of 5:45.

### Where the stress benchmark stands after build-160

| Units | A: fight on screen | B: fight off screen | C: fight at 4x |
| ---: | --- | --- | --- |
| 100 | 89.8 FPS, p50 9.8 ms (76.6, 11.5) | 200.4 FPS, p50 3.7 ms (82.3, 10.9) | 61.0 FPS, p95 31.3 ms (54.9, 33.3) |
| 200 | 50.8 FPS, p50 17.0 ms (45.4, 18.5) | 124.6 FPS, p50 4.7 ms (49.4, 17.1) | 38.7 FPS, p95 50.4 ms (34.6, 54.5) |
| 300 | 34.9 FPS, p50 24.7 ms (29.9, 28.5) | 86.8 FPS, p50 6.4 ms (33.3, 25.6) | 29.2 FPS, p95 59.9 ms (26.1, 64.9) |
| 500 | 20.0 FPS, p50 42.7 ms (18.0, 46.7) | 49.9 FPS, p50 10.6 ms (19.8, 41.7) | 20.3 FPS, p95 75.6 ms (18.4, 82.3) |

Build-157 values are in brackets. Build-159's single 300-unit run measured 38.4 FPS in
phase A, and build-160's two runs measured 34.9 and 34.0 FPS. Build-160 changes nothing in
that scene's presentation, so the gap is run-to-run spread. In the profiled 300-unit run,
`godot units` fell from 7.3 to 3.4 ms per frame and all of `_Process` from 8.4 to 4.4 ms.
Allocation fell from 3.4 to 1.4 MB per frame. The simulation tick in this open-field
melee is unchanged, at about 19.6 ms (6.5 ms per frame on average), and the wide-body
combat position searches dominate it. Captures of phases A and B at 100, 200, 300 and 500
units are pixel-identical to build-157.

All 43 Godot smokes were run on build-160. Thirty-nine pass, ObserverSmoke included. Three
fail, and each fails the same way on an untouched build-156 checkout:
- StructureSmoke: `random_place_of_power_drawn`.
- PlacementSmoke: `swordsman_selectable_with_source_stats`.
- FormationSmoke: `attack_move_dispatch`.

ResearchSmoke hangs on both. On build-160 it first logged "Invalid unit definition".

### Build-162: cheaper combat position searches

In a big melee each tick spent most of its simulation time on wide-body combat position
searches (`CombatPosition`), above all on the flank step. That step builds attack
candidates around up to eight other enemies, testing a ring of standable points around
each one against every nearby body and held attack position. All changes are exact:

- Each candidate search works out every blocker's position, height, spacing and held
  attack position once (`CombatBlocker`), instead of again for every ring point. It
  gathers them straight from the grid, without copied and de-duplicated lists (same
  units, same order).
- A ring point's tests run cheapest first: standing, blockers, then the attack-move lane,
  then the terrain sweep. All must pass and each is pure, so the answer is the same.
- `FindPathToAny` asks the caller's lane filter once per node and search, not once per
  neighbour that reaches it. It reuses its queue and goal sets; priorities are
  (cost, node) and a node is queued again only at a lower cost, so the search order
  cannot change.
- The footprint test reuses its seen set, and body rasterization skips the surface-layer
  lookup on cells that have no layers.
- New profiler sections: `CombatPosition blocked`, `CombatPosition flank`,
  `flank opponents`, `flank candidates`, `candidates blockers`, `candidates geometry`
  and `candidates points`.

All 2,698 simulation checks and the appearance checks pass. The nine army-benchmark
digests, the three late-match digests, the seven 100-per-side soak scenarios (with
`--verify-fast-paths`) and the 8-bot Rift digest at 14:00 are unchanged. Army scenarios
ran 5-26% faster: 36-per-side melee 1,755 → 1,311 ms, 100-per-side melee 1,909 →
1,514 ms, choke 1,695 → 1,324 ms. These are single runs, and the PC varied by up to 30%
between runs during the day.

In the 300-unit stress fight, a tick fell from about 19.6 to 16.2 ms (5.4 ms per frame),
and `CombatPosition` from 2.9 to 1.9 ms per frame (1.45 to 1.0 ms per search). Phase A went from 34.9 to 38.8-39.0 FPS,
phase B from 86.8 to 100-105 FPS and phase C from 29.2 to 32.9 FPS. Captures are
pixel-identical to build-157. In the Rift match the search is only about 1 ms of a 13 ms
tick, so ticks there barely moved (12:00: 13.7 → 13.1 ms; 14:00: 11.8 → 11.1 ms). A
profiled run's phase C froze at 2,111 draw calls for every frame because the window
stopped being drawn (covered or minimized), so its timings were discarded.

### Build-167: fog, hero glows and buildings in real matches

The stress benchmark has no fog, few buildings and no heroes. A late-game play benchmark
measures them instead. `-FastUntil 10` plays a match at 16x until 10:00, then at 1x, and
starts a profile window right there. `-CameraTour` moves the camera to each player's base
in turn every 5 game seconds. With `-ProfileUntil 12`, the profile line then covers two
minutes of real-speed play in the window:

```powershell
./prototype/run.ps1 -Map MP08_Rift -Observe -Bots Gimli,Aragorn,Legolas,Saruman -Profile -FastUntil 10 -ProfileUntil 12 -CameraTour -Resolution 1600x900
```

Profiling that match showed three costs, all fixed exactly:

- **The fog overlay.** On every sight update it blurred every 2 m sight cell 3x3, and on
  every frame while anything faded it stepped every cell and rebuilt every byte; at 4x and
  above sight changes all the time. `Presentation/FogFade.cs` blurs with running row and
  column sums and fades only the cells still moving: the others keep their byte. That is
  all the old full pass wrote for them. `FogFadeChecks` compares it with the build-150
  loops over 1,540 frames of random sight, side changes and frame steps, on maps down to
  one cell wide: same targets, shown values, bytes and settling. The overlay no longer
  builds a string of the shown sides every frame.
- **Hero glows.** Each moving hero rebuilt its ground glow every frame, all 1,728 vertices
  with their own trigonometry and terrain lookups, even off screen. A hero out of view now
  hides its glow, which lies within the view margin, so nothing on screen changes. Drawing
  it again when the hero returns puts it where the hero is. A first version left the glow
  where it was, which could have shown a stale glow; HeroExperienceSmoke caught it. The 343 distinct ring points are worked out
  once per rebuild, with the same float expressions, into reused arrays. A temporary
  in-game check compared 390 rebuilds in the Sandbox smoke with the old loop, vertex for
  vertex.
- **Buildings.** Each frame the building loop set every building's animation speed in
  Godot and built three queries. The speed is now written only on change, with no
  queries, and a fire effect shows or hides only on change. Visibility is still set every
  frame, because the physics frame shows every standing obstacle's root. FogSmoke caught a
  version that skipped it: an unseen enemy base could appear in fog.

Profiler sections added: `hud fog`, `hud sites`, `hud ghosts`, `hud corruption`, `hud
selection`, `hud production`, `hud build panel`, `units loop A`, `units loop B` and
`units blood`.

8-bot MP08_Rift, 10:00 to 12:00 at 1x with the camera tour, 1600x900, against a baseline
with the same benchmark options:

| | Build-166 | Build-167 |
| --- | ---: | ---: |
| Frames in the 2 minutes | 7,678 (64 FPS) | 9,271 (77 FPS) |
| Frame p50 / p95 / p99 | 12.5 / 32.2 / 60.8 ms | 9.9 / 26.6 / 54.4 ms |
| `_Process` per frame | 3.33 ms | 1.56 ms |
| `godot units` | 2.15 ms | 0.69 ms |
| `units loop B` (hero glows) | 1.54 ms | 0.10 ms |
| `hud sites` (buildings and fog) | 0.66 ms | 0.35 ms |
| `hud fog` | 0.42 ms | 0.13 ms |

The simulation is the same in both (`godot physics frame` 14.7 ms per tick): it is now the larger cost.
Windowed frame rates swing between runs: an earlier build-167 run of the same benchmark drew 14,714 frames
(123 FPS) with nearly the same costs inside `_Process` (1.41 ms per frame), so compare the per-frame costs.

The baseline is build-166 with only the benchmark options added. Both runs have the same state digest at
tick 14,400 (`72BE0238…`), and so does a headless 16x run to 12:00 with and without the change. The change was
made and first measured on build-163 (8,129 against 10,806 frames, frame p50 12.4 against 8.8 ms, `E7D5C2E2…`
both) and landed after builds 164 and 166. On build-166 all 2,744 simulation checks pass, and so do 18 smokes,
among them the fog, hero, Evil, building, siege, victory and menu smokes. In the fogged
300-unit stress fight, captures of phases A and B are pixel-identical to the baseline, and
frame rates are the same within run-to-run spread (38.2 against 38.0 FPS in phase
A), since that scene has neither heroes nor many buildings. The Sandbox smoke's own
captures are not usable for such comparisons: cloud shadows follow the shader clock,
which counts loading time, so two runs of the same build differ by up to 12 levels over
a fifth of the pixels.

### Build-168: route searches, regions and occupancy in real matches

Build-167 left the simulation as the larger cost of the late-game play benchmark. Profiling the 8-bot MP08_Rift
match from 10:00 to 12:00 showed where a tick went: route searches, the occupied-node set rebuilt about nine times a
tick, ground sweeps, and allocations (5.5 GB in the two minutes). All changes are exact: the match, the stress
fight and every check play out the same, and captures are pixel-identical.

- **Connected regions.** Every building placed, finished or razed dropped the connected regions of every body
  graph, and the next `IsConnected` on each graph flooded its whole region again: about 410 floods of some 15 ms in
  the two minutes, 2.5 ms per tick, most of `FindPath`'s time and much of the bots'. `NavigationRegions.cs` updates
  the regions around the change instead. A change can only alter nodes and edges inside the area `InvalidateNear`
  already forgets (A), and any path that leaves A re-enters it through the ring of nodes next to it (B). Adding an
  obstacle can only split regions: a region keeps its name when a flood from one of its remaining nodes in A∪B
  reaches all the others (usually after a few hundred nodes; one that runs out of its budget forgets the region).
  Removing one can only merge them: the pieces of A∪B the new graph links merge the regions they touch and take in
  the new nodes. A forgotten region, or one never flooded, is flooded when next asked about, as before. Regions are
  only compared and counted, so `IsConnected` and `ComponentSize` answer as after a full re-flood; with
  `VerifyFastPaths` every update is checked against one. This needs an undirected cached graph: if a change ever
  leaves a ground edge across A's border cached open one way and closed the other, that graph goes back to forgetting
  all regions for good. In the Rift match that never happened; the window had 15 floods and 106 local updates.
- **Route searches.** `FindPath` and `FindPathToAny` are one search loop (`GridNavigation.Search`) with the
  per-neighbour work written out instead of five helper calls per neighbour: the node records (seen, closed, cost,
  predecessor) sit in one struct per node, the open set is a binary heap of (priority, node) packed into longs,
  blocked nodes are stamped into an array, ground costs come from the direction and the octile heuristic from the
  cell's coordinates. The neighbour order, the order of the tests (standing, blocked, closed, lane filter, cost),
  the heuristic and the keys are the build-167 ones. A node is queued again only at a strictly lower cost, so no two
  entries share a key and any correct heap pops them in the PriorityQueue's order. On a fixed set of 5,550 route and
  group searches on MP08_Rift for every body (`--route-search-benchmark`) a Release build takes 8.8 s instead of
  19.2 s and a Debug build 29.0 s instead of 53.7 s, with the same digest of every route (`DB42B44C7EA8E5F5`).
- **Occupancy.** `OccupiedNodes` reads ground heights from a table built once per map (shared by the body graphs)
  and centres from the row and column. Its five per-tick callers (blocked replans, attack-position and siege
  searches, route and waypoint smoothing) refill their own set: cleared, a set is filled in the same order as a new
  one. `SmoothWaypoint` built the whole set for every constrained step and used it only to test up to five shortcuts
  of a few cells; it now builds it only once a shortcut is tested and only from the units near those shortcuts
  (every unit that can hold a cell a shortcut asks about), checked against the full set with `VerifyFastPaths`.
  A blocked production exit built the whole set up to sixteen times a tick to test one cell each; it asks the grid
  instead (`NodeHeld`, `BodyBlockedAt`). The set now costs 0.19 instead of 1.72 ms per tick in the window.
- **Ground sweeps.** `CanTraverseGroundPoints` keeps the swept cells as row intervals and checks a row's terrain as
  one span, steps its sample points by exact quotient and remainder instead of two long divisions each, skips a
  sample in the cell the previous one tested, reads blocked nodes, the body's clearance profile and the cells that
  hold footprints from arrays instead of hash lookups, and skips rows of footprint-free cells as spans. The old
  column list and sample points are recomputed and compared under `VerifyFastPaths`.
- **Smaller scans and allocations.** The grid's movement and rectangle queries dedupe units on graph links with a
  stamp instead of a list search per unit (checked against the old list). `FreeGround`, ring geometry, region
  floods and movement heights no longer allocate per node. Towers pick the nearest target in one pass instead of
  sorting every candidate each tick, and a researched tower weapon is made once instead of every call. Wide and
  ranged units choose an opponent in one pass over reused lists (the same pool and tests in the same order, the
  same keys as the sorted query). Each is checked against the query it replaced under `VerifyFastPaths`.

8-bot MP08_Rift, 10:00 to 12:00, headless at 16x, ms per tick (sections nest):

| | Build-167 | Build-168 |
| --- | ---: | ---: |
| Whole step | 15.03 | 8.61 |
| Unit movement | 5.11 | 2.59 |
| `FindPath` (5 a tick) | 3.82 | 2.18 |
| Blocked replan | 2.13 | 1.13 |
| `OccupiedNodes` | 1.72 (8.8 builds) | 0.19 (6.6 builds) |
| `CanTraverseGroundPoints` (954 a tick) | 1.37 | 0.95 |
| `SmoothGroundRoute` | 1.07 | 0.65 |
| `SmoothWaypoint` | 0.77 | 0.03 |
| `ClearGroundStep` | 0.75 | 0.48 |
| Unit combat | 2.80 | 1.72 |
| `CombatPosition` | 0.68 | 0.28 |
| `ChooseOpponent` | 0.24 | 0.11 |
| Bots (`ai`) | 2.52 | 1.19 |
| Production | 0.87 | 0.71 |
| Defenses | 0.66 | 0.61 |
| Allocated in the window | 5,458 MiB | 4,109 MiB |
| GCs (gen 0/1/2) | 110/15/2 | 83/11/1 |

From 12:00 to 14:00 the step falls from 14.4 to 9.1 ms. The headless 16x run to 14:00 took 166 s instead of 230 s.

The late-game play benchmark (`-FastUntil 10 -ProfileUntil 12 -CameraTour -Resolution 1600x900`, windowed, run back to
back against build-167): 9,711 frames instead of 8,626 (79 instead of 71 FPS), frame p50 / p95 / p99 10.4 / 22.0 /
34.1 ms instead of 10.9 / 28.2 / 57.9, the step 9.0 instead of 14.2 ms per tick, and `_Process` the same per frame
(1.62 against 1.65 ms). It allocated 5,632 instead of 6,713 MiB (GCs 115/16/1 against 135/19/1).

300-unit stress fight (`-StressBenchmark -Units 300`, then with `-Fog`), build-167 against build-168:

| | Build-167 | Build-168 | Fogged, build-167 | Fogged, build-168 |
| --- | ---: | ---: | ---: | ---: |
| Phase A FPS | 38.6 | 39.9 | 39.8 | 42.2 |
| Phase A tick (mean `godot physics frame`) | 16.9 ms | 12.9 ms | 16.7 ms | 12.4 ms |
| Phase B FPS | 101.9 | 109.1 | 97.7 | 110.3 |
| Phase C ticks in 1,196 frames (effective speed) | 812 (1.14x) | 1,120 (1.30x) | 810 (1.16x) | 1,155 (1.42x) |

Phase A stays bound by drawing (0.67x of real speed). Phase C now runs more ticks in each frame's 25 ms budget, so
it shows fewer frames (27.8 against 33.5 FPS) but plays faster; its captures are never comparable. The phase A and B
captures of both fights are pixel-identical to build-167 (`compare_captures.py --ignore 1000,30,1600,95`: 0 of
1,440,000 pixels differ).

Proofs: the headless run to 12:00 prints `PROFILE_DIGEST tick 14400 72BE0238…` as build-167 does, and both builds
print `7F22CD22…` at tick 16,800 (14:00); the play benchmarks of both builds print `72BE0238…` too. All 2,749
simulation checks pass (2,744 before, plus `RouteSearchChecks`), with every scenario digest the suite prints equal to
build-167's. `RouteSearchChecks` keeps the build-167 searches (as `Old`, over the public navigation API) and asks both
the same questions: on 400 random grids with walls, decks, legacy and swept obstacles toggled on and off, a footprint
that arrives late, and four body sizes, 72,000 route and 72,000 group searches give the same routes, floods, budgets,
fallbacks, flooded nodes and lane-filter questions, and regions kept across 6,757 changes (4,804 kept, 5,709 grown,
342 joined, 1,632 forgotten) match a full flood; on MP08_Rift for all 38 body sizes 7,600 routes and 7,600 group
searches match (`--route-search-checks converted/skirmish-maps/MP08_Rift`). Fifteen smokes (Fog, Skirmish, Sandbox, Observer,
Formation, Motion, Combat, Melee, Orders, Engagement, AttackMoveEngagement, Obstacle, Construction, Siege and
Performance) report exactly what build-167 reports, digests included; all pass except FormationSmoke's known
`attack_move_dispatch`, which fails on build-167 too.

**Proposal, not part of this build: optimize the Debug build.** `run.ps1` builds `Reforged.csproj` in its default
Debug configuration, which turns JIT optimizations off, and every measurement in this file was taken that way.
Exported games (Release) are optimized. App code then pays for every method call (the route search is 1.9x faster in
Debug than build-167 but 2.2x in Release), while library code (`PriorityQueue`, `HashSet`, LINQ) stays optimized. A
scratch copy of this build in progress (before its tower and opponent changes) with `<Optimize>true</Optimize>` in
`Reforged.csproj` ran the same headless match with the same digest at 3.3 instead of 9.3 ms per tick from 10:00 to
12:00, and finished in 82 instead of 165 s. It changes no
simulation result; what it costs is debugging (stepping and locals in the debugger), so it waits for the owner's
choice.

## Repeating the checks

```powershell
./prototype/run.ps1 -PerformanceSmoke
./prototype/run.ps1 -Map MP08_Rift -Observe -Bots Gimli,Aragorn,Legolas,Saruman -Speed 8 -Profile
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -c Release -- --retention-checks
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -c Release -- --army-benchmark converted/prototype/army-profile.json converted/skirmish-maps/Sandbox
./prototype/run.ps1 -StressBenchmark -Units 300            # add -Profile for the section split
python tools/compare_captures.py converted/skirmish-maps/Sandbox/stress-300-build-157-A.png converted/skirmish-maps/Sandbox/stress-300-build-160-A.png --ignore 1000,30,1600,95
./prototype/run.ps1 -Map MP08_Rift -Observe -Bots Gimli,Aragorn,Legolas,Saruman -Speed 16 -Profile -ProfileUntil 14 -Headless
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation -c Release -- --appearance-checks converted/skirmish
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation -c Release -- --army-soak converted/skirmish-maps/Sandbox soak.json --verify-fast-paths
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation -c Release -- --route-search-checks converted/skirmish-maps/MP08_Rift
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation -c Release -- --route-search-benchmark converted/skirmish-maps/MP08_Rift
```

`--route-search-benchmark` prints the time and a digest of every answer (`ROUTE_BENCHMARK best … ms digest …`); it uses
only public navigation calls, so the same file runs on an older tree to compare builds.

Two builds play a match the same way when the `PROFILE_DIGEST` lines of their
`-ProfileUntil` runs match. Stress-benchmark timings vary by a few percent between
runs on the same build; compare medians of the same phase, and repeat a run before
trusting a small difference.

Add `--allocation-profile` to the simulation benchmark to enable runtime allocation
sampling by type. It requires no external profiler installation. Sampled bytes are
approximate; `allocated_bytes` in each scenario is the exact thread allocation count.

`-Profile` prints every two game minutes. Compare living versus historical unit
counts, buried bodies, native nodes/resources, managed heap, private memory,
window allocation and Gen0/1/2 GC counts. `godot units`, `godot effects` and
`godot hud` separate presentation costs from the simulation. Heap and private
memory alone do not prove a leak: GC and native allocators can retain capacity.

## Remaining costs

Historical simulation records and the two lookup nodes still grow with casualties.
Simulation tick work excludes settled corpses, and since build-159 the presentation
and HUD loops do too. Occupancy rasterization and combat planning still allocate,
particularly during congestion. Expensive movement retries and bot placement searches
remain candidates for further work.

After builds 157-160, with 300 units all on screen the frame is still bound by Godot's
skeletal animation and render CPU (about 7,100 draw calls in gl_compatibility, with
shadows) and by the simulation tick (about 13-19 ms in a 300-unit melee, in one frame
out of three; 16 ms after build-162). The exact work left is smaller: movement
(steering, ground steps, route smoothing), the occupancy set each position search
builds, and the position search itself (about 1.0 ms each in the 300-unit stress fight, from
1.45 ms). Everything else that would
reach 60 FPS with 300 units in view changes play or pictures, so it waits for the
owner's choice: shared group routes (flow fields or a sector/portal layer); spreading
bot thinks over ticks; throttling idle-guard acquisition and blocked-producer retries;
a simulation thread with snapshot interpolation; and instanced vertex-animation crowds
or cheaper shadows for distant units.

After build-168 the 8-bot Rift late game spends 8.6 ms a tick in the Debug build that `run.ps1` makes: route searches
2.2 ms (the searches themselves, each body graph's first region flood and long replans), combat 1.7, bots 1.2,
about 950 ground sweeps 1.0, production 0.7 and towers 0.6. Allocation fell by a quarter; the largest sources left are
the bots' LINQ queries and the visibility tests every tower makes of every unit each tick (`CanSeeUnit` asks
`PlacePowerAreas.Any` and `Tracks.Any` with a new closure each time). The biggest single saving left is the Debug
build itself (the proposal at the end of the build-168 section): about a third of the tick, with nothing else changed.

The standard observer smoke's Fate assertion fails in the original baseline and
the updated run. Both reach tick 22,621 with the same final digest; corpse-release
and the other observer checks pass.
