# Bilbo's Bluffs skirmish - builds 4 onward

Goal: a playable Good faction Tier 1 tech tree with working workers on the
stock 2-player map captured in the first local original-game session. This file
tracks what exists; planning lives in `tools/Roadmap.md` (Phase 4 economy).

## Try it

```powershell
python tools/prepare_prototype.py --skirmish   # one-time, builds converted/skirmish
python tools/skirmish_construction.py --cache converted/skirmish  # upgrade any earlier skirmish cache (required for build-13)
python tools/skirmish_production.py --cache converted/skirmish    # add only build-12 training data to a build-8 through build-11 cache
python tools/skirmish_economy.py --cache converted/skirmish       # build-17 gathering rules and worker clips (economy format 2)
./prototype/run.ps1 -Skirmish
./prototype/run.ps1 -SkirmishSmoke             # headless checks
./prototype/run.ps1 -ConstructionSmoke         # construction, cancellation and both resources
./prototype/run.ps1 -StructureSmoke            # buildings, neutral interactables, palettes, counters and gap movement
./prototype/run.ps1 -ProductionSmoke           # Citadel worker training, queue cancellation, rally point and spawning
./prototype/run.ps1 -PlacementSmoke            # placement, Camp population, queued Barracks, Swordsman training, resume and cancel
./prototype/run.ps1 -DwarfHallSmoke            # Dwarf Hall, Axethrower training and thrown axes (credits a test bank first)
./prototype/run.ps1 -FrodoSmoke                # Frodo training, hero portraits and the One Ring (credits a test bank first)
./prototype/run.ps1 -TechSmoke                 # Citadel 2 and War Camp upgrades, Tier 2 locks and a Forge (credits a test bank first)
./prototype/run.ps1 -Tier2Smoke                # Rider, Shieldbreaker and Ranger: locks, training, rally and strikes (test bank + instant build)
./prototype/run.ps1 -ResearchSmoke             # Forge and Outpost research, cancel, a full Iron Blades and Attack 10 -> 11
```

The cache is separate from `converted/prototype` (Helm's Deep), so both sandboxes
coexist. `python tools/prepare_prototype.py --skirmish --units-only` refreshes
unit models and rules in an existing skirmish cache.

## Build-4: map and start state

| Item | Source |
|---|---|
| Map | `Levels/Multiplayer/MP02_Bilbos_Bluffs.wor` (200 x 200, grasslands) |
| Start points | Editor marker block ("pre_terrain_records"): 14 records of 96 bytes, type 1 = Start Point 0/1 at (38.3, 40.1) and (160.7, 157.3), type 2 = creature spawn areas. Decoded by `tools/skirmish_map.py`. |
| Start building | `BUILDING_G_TOWN_CENTER_LEVEL_1` (Citadel 1) at each start point, with its oriented-collision footprint as a removable obstacle (ids 10000/10001) |
| Start workers | Four `UNIT_G_HUMAN_WORKER` per side on a ring 9-13 m from the Citadel, deterministic and connected to the other base |
| Resources | 4 ore piles and 4 food wells (4500 each) stay in the map scene; each base's nearest pair is recorded in the manifest (`start_team`); two pairs sit in the middle |
| Start state | `WorldVariables` default food 115 / ore 325; population 4 workers x 1 against capacity 10 (Citadel `AdditionalPopAdded`) - the same readout as `local-009` |
| Buildings cached | The seven Tier 1 building models (Citadel 1, Mill, Foundry, Tent, Barracks, Dwarf Hall, Watchtower) for the next builds |
| HUD | Resource strip (original population/food/ore icons) replaces the Allies/Enemies label in the console |

Team 0 is the player (Start Point 0), team 1 an inert Good base. The enemy has
no behavior and no AI; nothing in build-4 changes unit rules.

## Known limits

- All seven Tier 1 buildings can be built; gathering, selection and the
  Worker, Gondor Swordsman, Dwarf Axethrower and Frodo training work. Hero
  experience and levels, Sting's glow and the aura effect, the
  Axethrower's second (right-hand) throw, Watchtower defenses and garrisons,
  building rotation, repair and combat against buildings remain open.
- Worker gathering and hammering sounds, the ore-mining effect, repair, exact
  work ranges and controlled original-game timing comparisons remain open.
  Gathering amounts and times are owner-defined (build-17), not the source rates.
- Start Citadel orientation is 0 degrees, not the original game's. Start-worker
  placement is an approximation of the original layout.
- Fog of war, animated weather and original-game lighting reproduction remain open.
- Tier 1 here means buildings and units requiring only the Citadel (see the
  Roadmap plan); Shieldbreaker (Skills structure) and Rohan Rider (Citadel 2) are
  outside it.

## Validation

`./prototype/run.ps1 -SkirmishSmoke` checks cache loading, 4 workers per side,
two start buildings and their obstacles, eight resource nodes, standable and
connected starts, the resource readout, worker selection and live gathering. Python
tests (`tools/test_skirmish_map.py`) cover the marker decoder, resource pairing
and worker placement synthetically, plus the real map when extracted.

## Build-5: raw ore gathering

Select workers and right-click an ore pile. Workers approach, gather up to nine
ore, return to their own Citadel, credit the bank and repeat. Shift-right-click
queues gathering after the current order. Stop, Move and other explicit orders
interrupt gathering; carried ore stays with the worker until gathering resumes.
Walk/Run preserves the gather order. Death discards cargo. On raw-stock
exhaustion the final partial load is delivered and queued orders continue.
The resource strip now reads the simulation bank; carried ore is not spendable.
Worker portrait tooltips show carried amounts. The enemy base stays inert.

`tools/skirmish_economy.py` imports `MaxCarriedOre=9`, `MaxCarriedFood=10`, raw
ore rate `0.2`, raw extraction fraction `0.55`, raw food rate/fraction `0`, and
storage limits `99999` from the owner's Units, Objects and WorldVariables tables.
The importer is also connected to full skirmish preparation; an existing cache
can refresh only its economy metadata without re-exporting models.

The current adapter interprets the raw rate as units/second and the raw fraction
as the recoverable part of initial stock: 2475 of a 4500-unit pile. Integer
milliresources retain subunit progress at 20 Hz. These interpretations still
need controlled original-game measurement. Work ranges measure the imported
footprint itself: a worker may stand up to one worker body radius plus one
navigation cell beyond it, which is the nearest a standing cell can sit once the
footprint is rasterized. Build-20 replaced the earlier circle around the target
centre, whose radius came from the footprint's farthest corner and therefore let
workers mine and unload from two rows out on a footprint's flat sides. The
circle survives only as the scan bound for candidate spots, and as the fallback
on maps without precise footprints. The band is still an adapter policy rather
than a measured original work range.
Workers use ground navigation and shared body reservations for both trips.
Start workers are also checked against the first worker's connected body graph;
one original build-4 spawn was standable inside an isolated clearance pocket
and is now moved to connected ground.

Simulation checks cover fractional progress, full/partial delivery, shared stock,
replay/digest state, ownership, nonworkers, invalid sources, cancellation/resume,
queued orders, death, unavailable depots and full storage. The expanded Godot
skirmish smoke uses actual contextual clicks on a food well and an ore pile,
then checks cargo, delivery, live HUD values and resource conservation.
Build-5 validation: 588 simulation checks, 158 Python tests and 19 skirmish smoke
checks pass, including a completed delivery by every starting worker.
The existing Command, Orders and Ability Godot smoke checks also pass; the
prototype builds with zero warnings/errors and `git diff --check` is clean.

## Build-6: resource buildings and improved gathering

Select workers and click the Mill or Foundry portrait above the command row,
then click a food well or ore pile respectively. The ghost is green for valid
placement and red for invalid placement. Costs and time appear in tooltips.
Occupied foundations cannot be placed; move units clear if necessary. The site
footprint activates and the bank pays once on the command tick. Workers approach
the building and contribute only while alive and within its work envelope.

Stop or another order interrupts work without removing the foundation. Right-click
an owned unfinished foundation with workers selected to resume or assist. The
Cancel button targets an unfinished owned building, removes its footprint and
refunds 75% exactly once; fractional resources stay in the bank. Completed
buildings cannot use this construction refund. Shift-placement queues the order
without reserving money or ground until activation. A queued follow-up takes
precedence over the normal automatic switch from construction to gathering.

| Source rule | Mill | Foundry |
|---|---:|---:|
| `Buildings.CostFood / CostOre` | 0 / 50 | 0 / 80 |
| `BuildTime` (milliseconds, converted to 20 Hz ticks) | 25,000 / 500 | 40,000 / 800 |
| `MaxHealth` | 570 | 750 |
| `BuildingRequired1..3` | None | Citadel 1 |
| `RefundFoodPct / RefundOrePct` | 75% / 75% | 75% / 75% |
| `Objects.ResourceGatherRateWithBuilding` | 0.4 per second | 0.4 per second |
| `MaxGatherPercentageWithBuilding` | 100% | 100% |

`WorldVariables.AddlWorkerBuildFactor=0.1` is interpreted as additive work:
one worker contributes one tick per tick, each extra worker adds 10%. Initial
health is the imported 5%; health grows with progress. Original construction
states 19/17/18 show at thirds of progress, then state 0 shows the healthy
building. The cache restores hidden scaffold geometry and binds the original
construction texture keys. The world health bar and building animation freeze
when paused. Equal thirds, additive build speed, linear health growth, fixed
resource-site placement/orientation and interaction envelopes are adapter
policies awaiting original-game measurement.

A completed owned Mill enables food loads of ten; a completed owned Foundry
doubles the raw ore rate and unlocks the remaining 45% of the pile. Workers
still deliver carried resources to their own Citadel: neither source building
is marked `IsResourceDropOffBuilding`. Construction preserves existing cargo,
which is returned before gathering the new resource. Opponents cannot gather
through another team's improvement. Resource stocks remain shared and finite.

`skirmish_construction.py` refreshes an existing cache's navigation masks, body
clearance, building geometry/materials, icons, rules and hashes. Full skirmish
preparation also produces these assets. Original/extracted folders stay read-only.

Build-6 validation: 613 synthetic simulation checks (680 with the Helm's Deep
cache), 160 Python tests, 23 construction and 19 gathering Godot checks pass.
The construction check drives actual worker buttons and world clicks, checks
green/red previews, refunds, stop/resume, pause, all stages, deliveries, ownership
and exact food/ore conservation. A freshly prepared cache and an upgraded cache
both pass. Command, Orders and Ability smoke checks also pass. Construction views
are inspected at 1280x800 and 4K; original-game fidelity remains unverified.

## Build-7: worker corner stalls

The owner reported stalled workers during gathering and ordinary movement.
Long runs reproduced workers safely rounding the starting ore pile but ending
in a cell whose center has no open graph edges. Replanning returned no route;
new Move orders also rejected the disconnected starting cell.

Ground routes now try nearby graph connections from the worker's actual body
position when the cell-center route fails. Each connection must pass the same
swept terrain/footprint checks, avoid occupied connector cells, and reach the
destination's connected region. Workers walk the connector at normal speed;
impassable walls, body collisions and elevated-route reservations still apply.
Move acceptance, gathering/building approaches and route retries use this path.
No importer or collision-mask refresh is required.

In 10,000-tick raw-ore and Mill/Foundry cases, maximum stationary route waits
fall from 8,884/7,754 ticks to 7/8 ticks. Every worker completes 9–10 raw deliveries
or 17–18 deliveries after construction. The exact stuck position also accepts
and completes a new Move order. Synthetic checks cover Move, Attack-move,
connection to a deck, replay, swept safety and a genuinely impassable wall.

Validation: 624 synthetic / 691 Helm's Deep simulation checks and 29 worker-cache
checks pass. The Godot gathering smoke now runs 10,300 frames and requires
repeated deliveries by every worker, bounded route waits and an actual contextual
Move that all four finish (23 checks). Construction (23), Command (46) and
Orders (27) smoke checks pass. Resource conservation and body safety hold across
all four long worker scenarios. Build number is 7; restart to load the fix.

## Build-8: building clearance and selection

The reported Citadel/Foundry gap was narrower in navigation than in the authored
collision geometry. Object masks included half-cell padding as well as the
worker radius, and runtime movement checked those masks again. Skirmish caches
now opt into `swept-circle-v1`: graph masks use circle clearance against the
original projected convex polygons, and runtime sweeps test those same polygons
once using integer millimeters. The authored boxes keep their original size;
walls, rotated footprints, overlapping objects and vertical separation still
block the appropriate bodies. Placement and restoration use the same clearance.
Helm's Deep retains its existing cache policy and regression coverage.

The real-map probe moves one worker 20 m through the reported gap with the
Foundry active. The old cache takes 262 ticks and detours 12 m sideways; the new
cache takes 134 ticks with no sideways detour. Gathering, return trips and
ordinary Move orders share the corrected collision checks. The build-7 connector
recovery remains in place for off-center graph disconnections.

Click a visible Citadel, Mill or Foundry, including an unfinished foundation,
to select it separately from units. Shift-click toggles the structure; unit
clicks, unit portraits, control-group recall and drag selection return to unit
selection. Structures never enter unit groups or receive unit orders. Esc
clears selection. Enemy structures can be inspected. Picking tests visible mesh
triangles against terrain depth, and the outline follows the source footprint.

The console displays the original portrait, imported maximum health and armor,
live construction health/progress, and remaining resource stock where relevant.
Click its portrait to center the camera. Cancel on a selected owned foundation
targets that foundation and applies the existing 75% refund; completion hides
Cancel. Building combat, repair and training remain future work.

Existing skirmish caches require the upgrade command above for precise navigation,
structure metadata and the Citadel portrait. Full skirmish preparation produces
the same data; both a fresh cache and an upgraded cache pass. The local cache is
already refreshed. Restart to load build-8.

Validation: 633 synthetic / 700 Helm's Deep simulation checks, 30 worker-cache
checks and 162 Python tests pass. Godot checks pass for structure selection and
the actual contextual Move through the gap (26), gathering (23), construction
(23), commands (46), orders (27) and abilities (38). The structure check runs
headless and rendered at 1280x800 and 4K; both captures were visually inspected.
Original executable collision formulas and selection-art fidelity remain unverified.

## Build-9: neutral inspection and command palettes

All eight food wells/ore piles and the map's Random Place of Power are selectable
through their visible meshes. The importer follows `Objects.IsSelectable` and
`IgnoresMouseClicks`, so scenery stays outside the inspector. Other source-selectable
objects use the same path, including objects without a navigation obstacle; their
outline uses projected mesh bounds without adding collision. Neutral inspection
remains separate from unit selection, supports Shift-toggle and portrait camera
focus, and preserves unit groups and current worker orders.

The inspector uses original object portraits, a pale neutral outline and live
remaining resource stock. The source marks these nine objects `HasHealth=0`;
they have no fabricated health bar or armor. This adds inspection only; Place
of Power capture and spells remain open.

Unit action icons and hotkeys are hidden from building palettes. The selected
owned foundation retains its construction Cancel action. Enemy and neutral unit
palettes, neutral-object palettes and empty selections contain no action buttons.
The global army Walk/Run control beside the minimap remains available. Returning
to friendly units restores their actions, abilities or worker construction icons.

HP labels now center their actual text rectangle on the health bar, including
when the font's minimum line height exceeds the bar height. Population, food and
ore labels use the same rule against their icons. This keeps alignment stable
under uniform HUD scaling.

Existing caches need the upgrade command above for neutral metadata and portraits;
the local cache is already updated. Fresh preparation produces the same data.
Validation: 633 simulation checks, 164 Python tests, 51 structure and 49 command
Godot checks pass, plus construction (23), orders (27) and abilities (38). Every neutral placement is
clicked through the actual picker, including sites under completed resource
buildings; selection remains exclusive and sends no unit orders. Rendered checks
and visual inspection cover 1280x800 and 4K with upgraded and fresh caches.

## Build-10: structure box selection with unit priority

Dragging a selection box now inspects a structure when it selects no friendly
units. Any friendly units inside the box take priority; Shift-drag also retains
already selected friendly units instead of replacing them with a structure.
Units and structures never share a selection. A structure-only box opens one
inspector, preferring the visible structure whose ground anchor is nearest the
box center, with stable object-ID ties.

The box tests visible mesh triangles, so it can select part of a roof while the
building's ground anchor is outside. Terrain and nearer structures occlude the
same parts as ordinary clicks. Owned buildings, enemy buildings and neutral
interactables share this path. Reverse drags work; an empty drag clears selection,
while an empty Shift-drag preserves it. No cache refresh is needed after build-9.

Validation: the structure Godot check expands to 61 cases, covering mixed
unit/structure priority, partial mesh selection, owned/enemy/neutral structures,
reverse dragging, Shift retention and empty boxes. The command check retains
49 passing cases. Rendered selection checks pass at 1280x800 and 4K.

## Build-11: brighter environment and softer terrain shadows

The owner reported an overly dark environment, particularly the cloud-like
patches. The imported terrain contains very dark baked vertex shading, with
almost half of Bilbo's Bluffs' terrain vertices below 0.1 peak RGB in linear
space. This renderer has no animated cloud-shadow layer; increasing a sun light
alone cannot recover detail already multiplied almost to black.

Ambient energy increases from 0.45 to 0.70; sun energy remains 0.85. At runtime,
terrain vertex peak intensity becomes `0.24 + 0.76 * peak`. RGB ratios retain
the original tint, black becomes neutral gray, alpha is preserved and fully lit
terrain stays unchanged. Only the terrain mesh receives this adjustment. Water,
object textures, team colors, geometry and source/cache files retain their data.
Cast shadows remain enabled, with more ambient fill in shaded areas.

`Rendering/PreviewLighting.cs` owns the preview settings. Capture metadata reads
them directly and records the number of terrain surfaces/vertices and the
darkest intensity before/after the adjustment. This is a readability preference;
original-game lighting/weather fidelity remains unverified. No cache refresh is
required: restart to load build-11.

Validation: clean C# build; the 61-case structure/selection smoke passes with
rendered captures at 1280x800 and 4K. Helm's Deep's gameplay smoke and import
audit pass, retaining mipmaps, vertex-color/water bindings and team materials.
Both maps' rendered views were inspected.

## Build-12: Citadel worker training

Select your Citadel and click the Human Worker portrait in the action panel.
Each click queues one more worker, up to five. The cost is paid and the
population is reserved on the click, as in the original manual and `local-019`
(4/10 becomes 5/10 before the worker exists). The selection panel shows the
worker in training on the original purple `console_g_progressbar`, followed by
the queued portraits. Click any of those portraits to cancel that unit, or press
Backspace to cancel the last one queued, as the manual describes. The Train
button is disabled with its reason when food, population or queue space runs
out. Pause freezes training.

The finished worker appears outside the Citadel on the training tick and inherits
the army's Walk/Run pace. It is selectable, receives an animated model and offers
the normal worker construction actions. Right-click the battlefield with your
Citadel selected to set a rally point, shown by the original `MODEL_G_GATHER_POINT`
banner in team color. New units walk to it. Right-clicking again moves it.
Enemy Citadels show no training controls.

| Source rule | Value |
|---|---|
| Citadel `UnitProduced1..6` | Human Worker; Frodo (model not exported), Gimli/Aragorn/Legolas (Citadel 2) and Gandalf (Citadel 5) are listed as unavailable |
| Human Worker `FoodCost / OreCost / PopulationCost` | 35 / 0 / 1 |
| `UnitTrainingTime` (milliseconds, rounded up to 20 Hz ticks) | 12,000 / 240 |
| Citadel / Camp `AdditionalPopAdded` | 10 / 11 |
| `WorldVariables.DefaultInitialPopulationCapacity / MaxPopulationCap` | 0 / 100 |

`tools/skirmish_production.py` imports these rules for every Tier 1 producer and
records why each listed unit is unavailable. Barracks (Gondor Swordsman) and
Dwarf Hall (Dwarf Axethrower) data is already present for later builds. The HUD
population now comes from the simulation: living units plus queued reservations
against building capacity.

Adapter policies awaiting original-game measurement: cancelling refunds the full
cost (no source refund field exists for training); the queue holds five units;
a unit appears on the nearest free standing cell to the building center that
connects to the base, rather than at a recovered door position. When every exit
cell is occupied, the finished unit waits at 100% until space clears. A rally
point is a ground move; rallying onto a resource or unit is not interpreted.

Existing build-8 through build-11 caches need
`python tools/skirmish_production.py --cache converted/skirmish` (seconds; it
exports only the banner and training data). The full upgrade and fresh
preparation also include it. The local cache is already updated. Restart to load
build-12.

Validation: 655 synthetic / 722 Helm's Deep simulation checks, 30 worker-cache
checks and 166 Python tests pass. The new production checks cover payment,
reservation, exact timing, spawning just outside the footprint, queue and
population limits, active/last cancellation refunds, ownership, rally arrival,
pace inheritance, blocked exits and replay. The 17-case Godot production check
drives the real Train button, queue portrait, Backspace, right-click rally, pause
and enemy inspection, then waits for the trained worker to reach the rally point
and selects it. Structure (61), command (49), gathering (23), construction (23),
orders (27) and ability (38) checks, plus the Helm's Deep gameplay and melee
smokes, pass. The structure check now expects the Citadel palette to contain
training and right-click to set only a rally point. Rendered captures at 1280x800
and 4K were inspected.

## Build-13: Tier 1 building placement

Workers now offer all seven Tier 1 buildings in the order of the original worker
panel (`local-009`): Citadel, Mill, Foundry, Watchtower, Camp, Barracks and Dwarf
Hall, with Cancel in the eighth slot. Unaffordable buildings and those with a
missing prerequisite are disabled, and the tooltip gives the reason. Mill and Foundry
still snap to their resource sites. The other five go on open ground: the ghost
follows the pointer over ground cells and turns red where placement fails. Click
to place, or Shift-click to queue the placement after the current order. Right-click an
unfinished foundation to resume or assist, select it to cancel it, or use Cancel.

| Source rule | Citadel | Watchtower | Camp | Barracks | Dwarf Hall |
|---|---:|---:|---:|---:|---:|
| `CostFood / CostOre` | 250 / 250 | 105 / 105 | 0 / 80 | 60 / 105 | 80 / 100 |
| `BuildTime` (ticks) | 1,400 | 800 | 600 | 600 | 640 |
| `MaxHealth / ArmorPoints` | 750 / 4 | 400 / 1 | 400 / 1 | 700 / 1 | 750 / 2 |
| `MustBePlacedNearOtherBuildings` | no | yes | yes | yes | yes |
| `AdditionalPopAdded` | 10 | 0 | 11 | 0 | 0 |

Placement uses source data where it exists:
- Every ground cell under the building's oriented-collision footprint must be
  painted in the map's `PATHING_CATEGORY_BUILDING` grid. That grid is stricter
  than the walker grid on slopes (15° against 30°). The importer now exports it
  as `building_allowed_base64`.
- The same cells must also be free for walkers. Footprints cannot touch another
  structure or the reserved Mill/Foundry footprint over a resource.
- Units inside the footprint block placement.
- Buildings marked `MustBePlacedNearOtherBuildings` need their center within
  `MaxRadiusAllowingNewBuildings` of one of your completed buildings (Citadel
  25 m, others 15 m).
- Some selected worker must be able to reach the work envelope; otherwise
  nothing is paid.

The placed footprint becomes a runtime navigation obstacle. It is rasterized in
C# with the same swept body-circle test as runtime movement, separately for every
body profile (including graphs created later). Its vertical extent spans the
terrain under the footprint. Cancelling removes the foundation and footprint and
refunds the source 75%.

Completion uses the same cooperative work, scaffold states (19/17/18) and health
growth as the Mill/Foundry. Workers then go idle. A completed Camp adds 11
population, and the HUD capacity follows. A completed Citadel accepts deliveries
and trains workers through the build-12 queue. Barracks and Dwarf Hall can be
built and inspected, but their units are not exported yet, so they offer no
training. Every Tier 1 building model is now restored with its scaffold geometry;
start Citadels select their healthy state explicitly.

Adapter policies awaiting original-game measurement: buildings are not rotated;
the placement point snaps to the one-meter navigation cell under the pointer;
proximity uses center distance to completed buildings only; covered cells are
those whose center lies within half a cell of the footprint. Construction-panel
order comes from the `local-009` capture.

Existing caches need the full upgrade (`python tools/skirmish_construction.py
--cache converted/skirmish`, about ten seconds). It refreshes navigation with the
building grid, restores the building models and adds the placement rules and the
four new building portraits. Fresh preparation produces the same data. The local
cache is already updated. Restart to load build-13.

Validation: 678 synthetic / 745 Helm's Deep simulation checks, 30 worker-cache
checks and 167 Python tests pass. The new placement checks cover payment,
per-body rasterization (including graphs created after placement), prerequisites,
passability, proximity growth, structure/resource spacing, unit blocking, map
bounds, cancellation refunds, queued placement joining one foundation, Camp
population, Citadel delivery and training, and replay. The 15-case Godot
placement check drives the real construction buttons, green/red ghost, invalid
click, placement, Shift-queued Barracks, completion, inspection, right-click
resume and cancellation. Command (49), orders (27), abilities (38), gathering
(23), construction (23), structure (61) and production (17) checks, plus the
Helm's Deep gameplay and melee smokes, pass. An upgraded and a freshly prepared
cache both pass. Rendered captures at 1280x800 and 4K were inspected.

## Build-14: Barracks trains the Gondor Swordsman

The skirmish roster now exports `UNIT_G_GONDOR_SWORDSMAN` with its source
model, attack clips and hit events, weapon, armor and body profile, plus its
original portrait. A completed Barracks therefore becomes a producer with the
Swordsman in its training panel; the Rider of Rohan stays unavailable because
it requires Citadel 2. Training, queue cancellation, population reservation and
rally points reuse the build-12 queue unchanged.

| Source rule | Gondor Swordsman |
|---|---:|
| `FoodCost / OreCost / PopulationCost` | 50 / 0 / 2 |
| `UnitTrainingTime` (ticks) | 280 |
| `MaxHealth / ArmorPoints` | 155 / 0 |
| Melee weapon `CuttingDamagePerSecond` | 10 |
| Main collision body (radius / height) | 570 mm / 1,798 mm |

The trained unit shows 155 HP, Attack 10, Armor 0 and Victories 0 when selected,
which matches the original selection captured in `local-020`. Its melee timing
uses the existing combat adapter (three source clips, one or two hit events).

A trained unit can be larger than the worker whose position anchored the
producer's exit. The exit search now starts from the nearest cell that body can
stand on, so a larger body can still leave a placed Barracks.

Refresh an existing cache with
`python tools/prepare_prototype.py --skirmish --units-only` (a few seconds). It
exports the Swordsman model, combat rules, body clearance, portrait and the
updated producer lists. Fresh preparation includes them. The local cache is
already updated. Restart to load build-14.

Validation: 680 synthetic / 747 Helm's Deep simulation checks, 30 worker-cache
checks and 167 Python tests pass. A new simulation check trains a body that
cannot stand on the recorded exit anchor. The Godot placement check grows to 18
cases. After the Barracks completes, it clicks the real Train button, sets the
Barracks rally point, waits for the Swordsman to arrive and selects it to read
the source stats. Structure (61), production (17), construction (23), gathering
(23), command (49), orders (27) and ability (38) checks, plus the Helm's Deep
gameplay and melee smokes, pass. Upgraded and fresh caches pass. Rendered
captures at 1280x800 and 4K were inspected.

## Build-15: Dwarf Hall trains the Dwarf Axethrower

The skirmish roster now also exports `UNIT_G_DWARVEN_AXETHROWER`, so a completed
Dwarf Hall trains it through the same queue. The Dwarf Shieldbreaker stays
unavailable because it requires the Skills structure.

| Source rule | Dwarf Axethrower |
|---|---:|
| `FoodCost / OreCost / PopulationCost` | 60 / 10 / 2 |
| `UnitTrainingTime` (ticks) | 340 |
| `MaxHealth / ArmorPoints` | 140 / 0 |
| Missile weapon (DPS, range) | 9, 0.5–10 m, `PROJECTILE_DWARF_AXE_LEFT` |
| Melee weapon (DPS, gap) | 9, 0.5 m |
| Main collision body (radius / height) | 624 mm / 1,822 mm |

Ranged units were previously hard-coded to the Elven Archer. The rules export
now uses an explicit ranged list (Elven Archer, Dwarf Axethrower). The source
Uruk-Hai also lists a missile weapon (77), but the accepted Helm's Deep demo
keeps it melee. Each ranged unit carries its own projectile presentation: the
weapon's source projectile model, its launch sounds and the trail of its first
bound effect (the Archer's `EFFECT_TEST_ARROW_TRAIL`, the Axethrower's
`EFFECT_DWARVEN_AXETHROWER_AXE_SWIPE`). Rendering picks the style of the
shooting unit. A diff of the refreshed Helm's Deep rules shows every unit
definition, unit pair, the Archer's trail and its six launch sounds unchanged.

The Axethrower has two source throwing clips. `Attack_Missile_01` releases
through the decoded `missile_release` event (left hand, tick 24 of 44).
`Attack_Missile_02` uses an undecoded event type 33 whose payload is the
right-hand joint (24, `J_ R Hand`), presumably with `PROJECTILE_DWARF_AXE_RIGHT`.
Until that event type is decoded, the second throw is skipped and recorded under
`Source.SkippedAttackClips`; the Axethrower throws only its left-hand axe.
Axes fly along the same target-following path as arrows; spinning flight is not
reproduced.

Refresh existing caches: `python tools/prepare_prototype.py --skirmish
--units-only` for the skirmish, and `python tools/prepare_prototype.py
--rules-only` for Helm's Deep, whose rules move the arrow to the new per-unit
projectile format. Both local caches are already updated. Restart to load
build-15.

Validation: 680 synthetic / 747 Helm's Deep simulation checks and 167 Python tests
pass. Three new cached checks (33 worker-cache checks in total) run the exported
Axethrower against a dummy: the axe releases at the decoded source event, it damages
the target on arrival, and the skipped clip is never used. The new 9-case Godot
Dwarf Hall check credits a test bank (300 food, 300 ore) and then uses only UI
input. It places and completes the hall, trains the Axethrower, sets its rally
point, selects it (140 HP, Attack 9) and force-attacks a friendly worker. Axes are
released, rendered with the axe model and trail, play launch sounds and hit. On
Helm's Deep, the gameplay, combat and melee smokes record the same arrow
releases, visuals, hits and damage as build-14. All other skirmish and command
checks pass, and fresh and upgraded caches pass. Rendered captures at 1280x800
and 4K were inspected.

## Build-16: Build menu and working construction clicks

The owner reported after playtesting that building icons could not be clicked.
Since build-9 the command panel hid and disabled every construction button at
the start of each frame and showed them again later in the same frame. A real
click presses in one frame and releases in another; hiding the button in between
removes Godot's mouse focus, so the release never reached it. The foundation
Cancel button, the structure portrait and the training-queue portraits lost
clicks the same way. Automated checks pushed press and release together, so they
missed it. Now each control's visibility has exactly one owner and is assigned
once per frame. The smokes press and release in separate frames for the Build
button, a building icon, the foundation Cancel button and a training-queue
portrait. With the old per-frame hiding temporarily restored, the placement
check fails.

Following the owner's request, construction now uses a Warcraft-style Build
menu instead of the original panel's always-visible list:

- Selected workers show their normal commands plus a Build button (the original
  `console_g_action_build` icon) in the top-left action slot. B opens it too.
- The build page replaces the unit commands with the seven Tier 1 buildings in
  the original order (Citadel, Mill, Foundry, Watchtower / Camp, Barracks, Dwarf
  Hall) and a Back button in the bottom-right slot.
- Choosing a building arms placement with the ghost. A successful placement
  returns to the unit commands. Esc first cancels placement and stays on the build
  page; a second Esc (or Back) returns to the commands. A unit command or a
  different selection also closes the page.
- To cancel a foundation, select it and use its Cancel button. The separate
  "cancel by clicking a foundation" worker mode is gone.

The seven buildings fit the 4x3 grid for Tier 1. Later tiers will need paging
or categories; the owner deferred that decision.

Validation: the Godot placement check grows to 23 cases. New cases cover the
worker card with Build and commands, the build page replacing the commands,
returning after placement, the B hotkey, and Esc stepping from placement to the
page to the commands. Construction (23), structure (61), production (17), Dwarf
Hall (9), gathering (23), command (49), orders (27) and ability (38) checks, the
Helm's Deep gameplay and melee smokes, 680 synthetic / 747 cached simulation
checks, 33 worker-cache checks and 167 Python tests pass. Captures of both cards
at 1280x800 and 4K were inspected. No cache refresh is needed after build-15.

## Build-17: Warcraft-style gathering, building animation and debug cheats

The owner redefined gathering after the build-16 playtest. Source rates no
longer set the amounts; the trips below do. Every phase uses the worker's own
source resource states, so the pickaxe, sacks and put-down come from the model.

- **Raw ore pile** (like a Warcraft III tree). The worker swings its pickaxe
  (`UNIT_ANIM_STATE_RESOURCE_ORE_GATHER`, 28 ticks). Each swing mines one ore
  on tick 16, the clip's supply-gather sound event (fraction 0.569). After five
  hits the fifth swing finishes. Then the ore pick-up clip plays (51 ticks; the
  sack appears at 0.293), and the worker carries the sack home. Several workers
  can mine one pile. The raw 55% extraction limit still applies.
- **Mill / Foundry** (like a Warcraft III gold mine). A worker walks to the
  completed owned building and disappears inside it. Only one worker can be inside
  at a time; the others wait at the door. After two seconds (40 ticks) it comes
  out on its entry cell carrying a full sack: 10 food (Mill) or 9 ore (Foundry),
  the source `MaxCarriedFood` / `MaxCarriedOre`. If someone stands on that cell,
  it uses the nearest free connected cell. A worker carrying anything delivers it
  before entering. A Foundry completed while a worker mines its pile ends raw
  mining after the current swing.
- **Delivery.** Near its Citadel the worker plays the source put-down clip (33
  ticks; the sack disappears at 0.56). The bank is credited when the clip ends.
  Carried loads use the resource walk and idle states with the right sack. A
  running worker plays the walk clip faster, in proportion to the run/walk speed,
  so its steps keep pace.
- **Building.** A worker at work on a foundation faces it and loops the source
  `UNIT_ANIM_STATE_REPAIR` clip (`G_Human_Worker_Repair`), which shows the hammer.
  Construction rules are unchanged.
- **Work spots.** Within the standing band a worker picks the cell that minimises
  its walking distance plus twice its distance to the target. It therefore mines at
  the rocks, puts sacks down at the Citadel door and enters or hammers at the
  building rather than at the far side of the band. A shorter walk still wins when
  the near side is taken.

A worker inside a building has no body: it does not collide, block placement or
spawning, or count as a target, and it cannot be clicked or selected. Entering
removes it from the current selection, as in Warcraft III. It stays in its control
group, and recalling the group selects only the members that are outside. Orders
sent while it is inside do not reach it. Population still counts it. A worker
interrupted while mining (made to give way, for example) restarts the swing and
keeps its partial load.

| Rule | Value | Source |
|---|---:|---|
| Raw ore per trip | 5 (one per hit) | Owner |
| Time inside Mill / Foundry | 40 ticks (2 s) | Owner |
| Buildings occupied at once | one worker | Owner (Warcraft III gold mine) |
| Mill / Foundry load | 10 food / 9 ore | `Units.MaxCarriedFood / MaxCarriedOre` |
| Swing / hit tick | 28 / 16 | `G_Human_Worker_Resource_Gather` and its sound event |
| Pick-up, put-down | 51, 33 ticks | `Resource_Pick_Up`, `Resource_Put_Down` (rounded up like attack clips) |

On the real map a 500-second run gives about 35 raw trips (175 ore) per worker.
With a Mill and a Foundry, four workers make about 240 trips. A Mill or Foundry
can take at most 10 food / 9 ore every two seconds. This economy is much faster
than the source gather rates (0.2 ore/s raw, 0.4/s with a building).

`UnitAppearance` now selects the choice by state when states share a clip (food
and ore sacks), and applies clip `show_attachment` / `hide_attachment` events.
Only the worker uses them among the exported units. The economy cache also
carries the worker's repair state for the building animation.

**Cheats** (F4 debug menu, skirmish only): **+1000 food and ore** credits your
bank up to its storage limit. **Build and train instantly** completes your
foundations as soon as a worker is assigned, and your training finishes on the
next tick. Cheats change state directly, so a replay of the recorded commands
alone will differ.

Refresh an existing cache with `python tools/skirmish_economy.py --cache
converted/skirmish` (economy format 2 adds the worker clips). Fresh and upgraded
preparation include it. The local cache is already updated. Restart to load
build-17.

Validation: 704 simulation checks (24 new: exact hit, pick-up and put-down ticks,
five-ore loads, interrupted swings, one-at-a-time Mill/Foundry visits, hidden-worker
rules, exits beside a blocker, Foundry completion during mining, replay and both
cheats), 33 worker-cache checks on the real map and 168 Python tests pass. The
gathering smoke grows to 27 cases and samples the gather clip, the ore sack
appearing in the pick-up, carried home and disappearing in the put-down. The
construction smoke grows to 28. Its new cases cover the hammer clip while
building, workers hidden and unselectable inside, one worker inside at a time,
and 10 food / 9 ore per visit. Its group-recall checks now expect only the
workers outside. Structure (61), production (17), placement (23), Dwarf Hall
(9), command (49), orders (27) and ability (38) checks, and the Helm's Deep
gameplay and melee smokes, pass. Close-up 1280x800 renders of mining, pick-up,
carrying, put-down and hammering were inspected.

## Build-19: Frodo and the hero portraits

Frodo, the last Good Tier 1 unit, trains at the Citadel. He is second in its
source `UnitProduced` list, after the Worker. The skirmish roster now exports his
model, clips, rules, portrait and ability icons.

| Rule | Value | Source |
|---|---:|---|
| Cost | 150 food, 100 ore, population 0 | `Units` FoodCost, OreCost, PopulationCost |
| Training | 720 ticks (36 s) | `UnitTrainingTime` 36000 ms |
| Body | 305 HP, armor 1, Attack 15 (hero damage, 0.5 m) | `MaxHealth`, `ArmorPoints`, weapon 114 "Frodo's Sting" |
| Unique | one per player, alive or queued | `IsUnique` |

While Frodo lives or waits in a queue, his training button is disabled with
"Frodo is already in play". After he dies he can be trained again; the source
`HeroDeathRefreshTime` (15) is not applied.

**Abilities.** Source BattleGear 7, 50 and 12; their `SpecificIndex` gives the
Q, W and E slots.

- **Q · The One Ring** (toggle). Press Q or click the icon to put it on, and again
  to take it off. The icon stays pressed while the Ring is worn, and the original
  on/off sounds play. The source OnActivate actions apply:
  - `SET_VISIBLE` 0.5: Frodo is drawn at half opacity.
  - `SET_PASSIVE`: he refuses attack orders and no longer picks fights. Move,
    Stop and the other orders still work.
  - `ENABLE_DETECTION` off: enemies cannot target him, and enemies already
    fighting him lose him.
  - `ENABLE_REGENERATION` off: regeneration is not simulated anywhere yet.

  OnContinuous `UPDATE_HEALTH` drains 2 HP per second. Adapter: the drain stops
  at 1 HP, because the source has no floor and a forgotten toggle would otherwise
  kill him.
- **W · Sting** (passive). Damage ×1.35 against Orc Slasher, Orc Bowman, Uruk-Hai
  and Goblin Spearman (target type 68). The blade glow (attachment flag 31)
  that appears near those units is not shown.
- **E · Heroic Aura** (passive). Allies within 15 m, Frodo included, recharge
  abilities 1.5× as fast (child spell 21, `SET_BATTLEGEAR_RECHARGE_MULTIPLIER`).
  Overlapping auras do not stack (StackableType 3). Its ground effect (148) is
  not shown. No other skirmish unit has a recharging ability yet.

**Hero portraits.** As in the original, each living hero gets a framed portrait
at the right edge of the screen. A green health bar and a blue experience bar
sit beneath it. The layout comes from `Screens/GameConsole_Hero_Portraits.lsr`
(`LIQDSCRN` records of normalized rectangles on a 1024 × 768 screen):

- Five slots, `CONTROL_CONSOLE_HERO_PORTRAIT_0` to `_4`, at x 0.947. The first
  is at y 0.248, then one every 0.091. Each portrait is 0.047 × 0.063, with the
  health and experience bars just below it.
- A separate `SPECIAL` slot above them (y 0.155) has no recovered use and stays
  empty. In `campaign-004` Gimli also sits in slot 0.
- The strip is anchored to the right edge and scales with the console.
- The art is the unit portrait, `console_frameportrait`,
  `console_g_doublebarbackgroundsmall`, `console_g_healthbarsmall` (tinted green)
  and `console_g_staminabarsmall`.

A click selects the hero, Shift-click adds or removes him, and a double click
also centers the camera, as in `campaign-004` and `campaign-009`. Hero
experience and levels are not simulated, so the blue bar stays empty. The Helm's
Deep sandbox shows Aragorn in the strip.

Refresh an existing skirmish cache with
`python tools/prepare_prototype.py --units-only --out converted/skirmish`, and the
Helm's Deep cache with `--rules-only` and `--ui-only`. The local caches are
already updated.

Validation: 722 synthetic / 789 cached simulation checks pass, including 14 new Frodo checks:

- the Ring toggle, exact drain and its 1 HP floor;
- enemies neither acquiring nor attacking the Ring-bearer;
- refused attack orders on both sides, and fights ending when the Ring goes on;
- Sting's target types, and the Heroic Aura's 1.5× recharge within 15 m only;
- replay.

Three unique-training checks are new too. 170 Python tests pass, including
Frodo's source abilities and production rules from the owner install.

The new `./prototype/run.ps1 -FrodoSmoke` (13 cases) uses a credited test bank. It
covers training, the unique-hero refusal, the source portrait layout, click
selection, double-click centering, the Q/W/E icons, and the Ring on, fading,
draining on the HUD and off. The production smoke now expects Frodo beside the
Worker. All other skirmish and Helm's Deep smokes pass. 1280 × 800 and 4K
captures were inspected.

## Build-20: footprint work ranges and world health bars

A worker may stand at most one body radius plus one navigation cell beyond its
target's footprint, measured against the footprint itself. The earlier envelope
was a circle around the target's centre whose radius came from the footprint's
farthest corner, so on a rotated square ore pile it reached 2.86 m past the flat
sides and crews gathered from two rows out. The standing band on a flat side is
now 1000 mm, a single row. The circle survives as the scan bound for candidate
spots and as the fallback on maps without precise footprints.

The floating `Name 42% 300/1200` label over a foundation is replaced by the world
health bar the original draws: a billboarded black strip with a green fill at the
foot of the unit or building, as in the October 21 campaign captures
(`campaign-012`, where the selected Citadel carries one). Because construction
health climbs from the imported 5% to full with the work done, the same bar reads
as the build progress; the selection panel still prints `Construction NN%`.

Two adapter policies: the bar shows only below full health, where the original
also showed it for a selected unit at full health, and its width comes from the
footprint's mean horizontal span (a unit's from its body radius) rather than a
measured original size. Bars are drawn without depth testing, so they stay
readable through the model they belong to, and they are added after a structure
registers its pick geometry so the strip is never ray-picked as the building.

## Build-21: combat against buildings and repair

Every skirmish building now has health in the simulation, so the health bar above
has something to show. Start buildings become completed **prebuilt** construction
sites at load, which gives the Citadel the same health, selection detail and bar
as anything a worker puts up; it is counted once for population, from its
`StartBuildings` entry rather than twice. Maximum health and armour come from the
source `Buildings` table: Citadel 750/4, Mill 570/3, Foundry 750/3, Barracks
700/1, Dwarf Hall 750/2, Camp 400/1, Watchtower 400/1.

Right-clicking an enemy building with armed units selected sends them to it. They
walk to the standing band around its footprint, using the build-20 work-range
code with their own weapon reach, then swing their ordinary attack clips at it;
a missile unit shoots a projectile aimed a metre above the footprint's centre.

Three adapter policies in the damage: a building carries no damage type in the
source table and takes no part in `UnitToUnitModifiers`, so a strike keeps its
full rate and loses only the building's `ArmorPoints`; there is no attacker limit
around a footprint; and a building does not retaliate, since the Watchtower's own
weapon is not implemented yet.

A ruined building frees its navigation footprint, its producer, its delivery
point and its model, and a ruined start building also stops supplying its
population and its prerequisites. A Mill or Foundry reverts to an unclaimed
resource site, exactly as a cancelled foundation does, so the pile can be claimed
again. Its attackers go idle rather than wandering on.

Workers repair. Right-clicking a damaged owned building with workers selected
gives them the ordinary Build order, and they loop the same source repair
(hammer) clip they use on a foundation. Repair restores a full health bar over
the building's own `BuildTime`, with extra workers adding the same
`AddlWorkerBuildFactor` as construction, and costs nothing: no repair cost
appeared in the `Buildings` or `WorldVariables` rows read here, so both the rate
and the free repair are adapters awaiting that check.

Not in this build: the Watchtower's own defenses, which the source defines in
`BuildingDefenses` as weapon 40 (Good Watchtower Arrow, 14 cutting damage per
second, 19 m) fired at one shot per second, rising to 1.2/1.4/1.6/1.75 with one
to four garrisoned units; the source fire and damage visual states; and a
skirmish win condition.

Validation: `./prototype/run.ps1 -SiegeSmoke` (14 cases) spawns a wave beside the
player's Citadel, lets it bite, removes it, repairs the building to full with the
start workers, then spawns a player squad beside the enemy Citadel and
right-clicks it into rubble. 722 simulation and 34 worker-cache checks, 170
Python tests and all other skirmish and Helm's Deep smokes pass. `-FormationSmoke`
still reports `attack_move_arrived:false`; that failure predates build-20 and was
reproduced on build-19 in a clean worktree.

## Build-22: force-attack on buildings and minimap orders

Owner playtest of build-21: force-attack did not work on buildings, and the
minimap took no orders.

The armed Attack command (A, or the panel button) already reached an *enemy*
building, because a contextual right-click and an armed click share the same
path. What it could not do was force a target the contextual rules refuse, so
clicking one's own building fell through to an attack-move at the ground under
it and looked like nothing happening. An armed Attack now forces the building it
clicks, of any team, which is what force-attack means everywhere else; the status
line says `Force-attack <name>` when the target is the player's own. A plain
right-click stays contextual, so a friendly building is still a repair order and
never a demolition by accident. The order carries the forcing in
`SimCommand.Cell`, and the besieging unit remembers it, so a queued forced siege
stays forced.

Neutral selectable objects — ore piles, food wells and the place of power — still
cannot be attacked. They are not construction sites and have no health.

Right-clicking the minimap orders the selection to that point, as the original
manual describes and the October 21 capture session observed; left-click and drag
still move the camera and nothing else. Ctrl marches instead of moving and Shift
queues, matching a right-click on the battlefield, and the camera does not jump
to the point. An order to unwalkable ground snaps to the nearest standable cell,
and with nothing selected the status line asks for a selection.

Validation: the siege smoke grows to 16 cases with force-attack on the player's
own Citadel and the armed Attack command on the enemy's; the minimap smoke grows
to 15 with the move order, the untouched camera and the Ctrl march. 722
simulation and 34 worker-cache checks, 170 Python tests and all other skirmish
and Helm's Deep smokes pass.

## Build-23: besiegers face the building

A melee unit hitting a building kept whatever heading it arrived with, so a squad
around a Citadel swung at it sideways. A missile unit was already fine: its
facing lives in the simulation and the siege pass turns it toward the target.

A melee unit's facing is presentation only, chosen each frame from its order. The
existing cases cover a caster, a missile unit, a fighter with a unit target and a
worker at a resource or foundation; none matched an attack on a building, because
a besieger has no unit target. A besieger now turns to the centre of the building
it is striking, through the same rotation the melee case uses, and its movement
heading is reset so it does not drift back to its approach direction.

The turn starts once the unit is in range and swinging, not while it walks in, so
a squad still marches naturally and then wheels to face the wall.

Validation: the siege smoke grows to 17 cases. It samples every besieger's
rendered yaw against the direction to the building centre once the Citadel is
visibly losing health, over about two thousand samples per run, and the check
fails on build-22's presentation.

## Build-24: axes at buildings and force attacks without retaliation

Owner playtest fix (commit `da2b80b`). Missiles aimed at a building carry no
target unit, and the projectile presentation looked up that missing unit every
frame, so axes thrown at buildings never drew; they now fly to their recorded
impact point. A unit no longer turns on whoever force-attacked it, nor on a
teammate whose missile was already in flight. The Dwarf Hall smoke also
force-attacks the Citadel and checks that the axes damage it and fly toward it.

## Build-25: Watchtower defenses

A completed Watchtower now shoots. The source gives the tower no weapon of its own
in `Buildings`; its `BuildingDefenses1` points at `BUILDING_DEFENSES_G_WATCHTOWER`,
which fires weapon 40 (Good Watchtower Arrow: 14 cutting damage per second, 0 to
19 m, projectile 15) at 1.0 shots per second, rising to 1.2/1.4/1.6/1.75 with one
to four garrisoned units. `tools/skirmish_construction.py` exports that row per
placeable building as `construction.defenses`, with the arrow's model, launch
sounds and trail through the same exporter the Archer and Axethrower use.

The arrow leaves the tower's projectile joint. `ProjectileSourceJointFlag` is 11,
`JOINT_FLAG_BUILDING_PROJECTILE`, and skeleton joint flags are bit masks: the
tower skeleton's `J_Projectiles` carries flag `2048 = 1 << 11` (and `J_Entrance`
carries `1 << 10`). At the model's 0.8 scale the joint sits 3.43 m up, 94 mm across
and 159 mm forward of the building origin; the smoke measures the simulation's
arrow origin against the drawn bone at 0.6 mm.

The tower shoots the nearest present enemy within range, keeps that target while
it stays valid, and switches when it dies or leaves. Each arrow carries the
weapon's damage per second less the target's armour over one firing interval (20
ticks at the base rate), so the rate sets the cadence and the damage per second
stays the source's. A unit wearing the One Ring is not a target. An unfinished
or ruined tower is silent, though an arrow already in flight still lands.

An idle armed unit that is shot turns on the tower with an ordinary (unforced)
attack on the building, as it would answer a unit. Workers, units holding ground
and units already busy carry on; a unit already fighting someone keeps fighting.
Because the tower shoots one unit at a time, its victims turn on it one after
another rather than all at once.

Adapters: ranges are centre distances from the building's origin; arrows fly at
the 25 m/s the ranged units use and need no line of fire; the weapon has no
damage type, so type matchups and `UnitToUnitModifiers` do not apply; garrisons
are not simulated, so the tower always fires at its base rate; and the source
`MaxSeekAngle`/`MaxSeekDistance` of the arrow are not modelled (arrows follow
their target, as unit arrows do).

The cache must be refreshed once: `python tools/skirmish_construction.py --cache converted/skirmish`.

Validation: `./prototype/run.ps1 -DefenseSmoke` (10 cases) places a Watchtower
through the Build menu, sends the builders home, spawns three enemy swordsmen
beside it and lets the tower kill them as each victim turns on it, then spawns
eight more that ruin it and checks it stops shooting. 13 new synthetic checks
cover the cadence, damage, armour, nearest-target choice, range and team limits,
retaliation, silence when unfinished or ruined, and replay. 737 simulation and
34 worker-cache checks, the Python suite and 15 other skirmish and Helm's Deep
smokes pass.

## Build-26: the Razing win condition

A skirmish can now be won and lost. The October 19 capture session played the
stock skirmish with the "destruction" game type; decoding the localized menu text
(`Menu.ltx`, a `LIQDTEXT` table of id plus length-prefixed UTF-16 strings) shows
that this is **Разрушение, Razing** (text 4401, `MULTIPLAYER_GAME_MODE_RAZING`).
The manual's rule: be the first to destroy all enemy buildings, regardless of how
many enemy units remain.

`prototype/Simulation/Outcome.cs` applies it. Each team that starts with a
building is a contender. At the end of every tick, a contender that owns no
standing building is defeated; foundations still under construction and claimed
Mill or Foundry sites count as buildings, an unclaimed resource site belongs to
nobody, and units never count. The last contender standing wins on that tick. If
the final contenders fall together, nobody wins. A world with fewer than two
contenders, such as the Helm's Deep sandbox, never decides a match.

The match only ends: the simulation keeps running, and a defeated player's units
stay on the field. When it ends, a centred panel over a dimmed battlefield shows
the original result line in English ("You won!", Menu 4602; "You lost!", 4702),
pauses the game and blocks battlefield input. Its two choices are the original
Continue (4803), which resumes play on the decided match, and Restart (4806),
which reloads the skirmish. The original end-of-game statistics screen
(`Menu_Stats.lsr`) is not reproduced; a single panel is a modern convenience.

Adapters: the result titles are translations of this Russian install's lines, not
the retail English text; a defeated player's units are left on the field.

Validation: `./prototype/run.ps1 -VictorySmoke` (9 cases) right-clicks a player
squad onto the enemy Citadel, checks the victory panel, the pause and the blocked
battlefield, presses Restart, then lets an enemy squad raze the player's Citadel,
checks the defeat panel and presses Continue. 9 new synthetic checks cover the
running match, the winning tick, units not counting, the decided match staying
decided, foundations and unclaimed sites, a sandbox without contenders, razing
one's own last building, and replay. 746 simulation and 34 worker-cache checks
and 16 other skirmish and Helm's Deep smokes pass, including the siege smoke,
whose final ruin of the enemy Citadel now also ends its match.

## Build-27: the skirmish AI

The enemy base is no longer passive. Owner scope: the core loop (economy,
building, training, assault, base defence), no scouting, raids, flanking or
heroes yet, and a medium default personality.

**Source data.** `TLOTR - War of the Ring/AIPlayers/*.rai` are INI text with
cp1251 names: `Difficulty` 0-2 and 0-100 `Aggressiveness`, `Tactics`,
`Scouting`, `Harassment`, plus `UsesSpecialAbilities` and `UsesHeroes`. Rings.exe
turns the knobs into 0-1 profile alphas (`BattleScriptInterface::AIProfileSet*Alpha`).
The tables supply the rest: `Buildings` AI columns (class, `AIMustHaveFirstPass`,
`AIMaxDefault`, `AILowerPriorityIfExists`; Barracks and Dwarf Hall are military
with a cap of 3, the Watchtower defensive, Mill/Foundry/Camp economy),
`Units.AIAssaultEffectiveness` (Swordsman 70, Axethrower 60) and `AINeverAddUnit`
(the worker), and `AIWARPARTY_ASSAULT` (5 to 12 units, 80% "Assault Melee" and
20% "Assault Missile" member classes). `tools/skirmish_ai.py` exports all of it
as `skirmish.ai`, with the twelve shipped personalities plus a `Default`.

**What is an adapter.** The code that turns these numbers into decisions is in
Rings.exe and is not recovered, and the October 19 session's "Default medium AI"
has no file: its values are in the exe too. `Default` is therefore Difficulty 1
with every knob at 50, approved by the owner. The decision logic in
`prototype/Simulation/SkirmishAi.cs` is new, built on the source numbers:

- It thinks once a second and acts only through the commands a player issues,
  so it obeys every placement, cost, population and pathing rule.
- Economy: workers up to 8 + 2 x difficulty; four gatherers per completed Mill
  (one worker fits inside at a time), the rest on ore.
- Building: one project at a time with two builders. Economy buildings come
  first (all first-pass), then one of each military building, then the
  Watchtower (before the military for aggressiveness below 40), then more
  military buildings up to `AIMaxDefault` once the bank can pay twice over
  (`AILowerPriorityIfExists`), plus a Camp whenever population is within three
  of the cap and a Mill on any further well within 80 m. Placement tries rings
  around home toward the enemy, 10 m clear of resources; a project that makes no
  progress for 30 s is cancelled for the source refund and its spot refused.
- Training: the next building's cost is reserved first; producers train toward
  the Assault shares, best Assault effectiveness first.
- Assault: once the idle army reaches the party size (12 at aggressiveness 0
  down to 5 at 100; 8 for `Default`), it attack-moves on the nearest enemy
  building, turns on defenders within 12 m (a unit razing a building ignores the
  units hitting it), razes the building and moves to the next. Survivors fight
  on; the next party forms at home.
- Defence: enemies within 30 m of any AI building draw every home soldier.

`./prototype/run.ps1` now plays against the AI. `-AiPersonality Gimli` (any `.rai`
file stem) picks a shipped personality and `-NoAi` keeps the enemy passive; the
smokes written for a passive enemy keep it passive. The F4 debug menu shows the
AI's personality and what it is doing.

The cache must be refreshed once: `python tools/skirmish_ai.py --cache converted/skirmish`.

**Timeline** of the default personality against a player who does nothing
(`-AiSmoke`, game time): first worker 0:13, Mill 0:24, Foundry 1:04, Camp 1:36,
Barracks 2:08, first Swordsman 2:23, Dwarf Hall 2:43, Watchtower 3:25, an
assault of eight leaves at 4:26, reaches the player's Citadel at 5:32 and razes
it at 5:48.

Validation: `-AiSmoke` (9 cases) records that timeline. 11 synthetic checks on a
two-base fixture cover the party sizes, worker training and assignment, the
build order, the assault razing the enemy keep, base defence, that the AI never
commands another team, and replay of a whole AI match. Python tests cover the
`.rai` parser, the 13 personalities and the source AI columns. 757 simulation, 34
worker-cache and 174 Python checks and 17 other skirmish and Helm's Deep smokes pass.

## Build-28: observer mode, bot against bot and game speed

Step 1 of the skirmish setup plan in the roadmap: watch bots fight, fast.

**Observer.** `./prototype/run.ps1 -Observe -Bots Gimli,Aragorn` runs a bot on
every start base, in base order (a missing entry takes the default personality),
and nobody plays. The interface now asks for one `PlayerTeam` instead of assuming
team 0 in about fifty places; observing, it is -1, so every command path finds no
units of its own and nothing reaches the simulation, which the smoke checks by
counting player commands (zero). Box and click selection inspect either side; the
panel reads "Player 2 unit · Inspecting" and shows no commands. A panel at the top
left lists each player in its colour with food, ore, population and buildings,
and the bot's own status line (workers, army, assault, current project). Selection
rings take the team colours, the resource strip and the allies/enemies counter
are hidden, and the result panel names the winner: "Player 1 wins!".

**Speed.** Pause and 1x, 2x, 4x, 8x or 16x, with `-` and `+` in any mode, the
observer panel's buttons, or `-Speed` at launch. The simulation keeps its fixed
50 ms tick; faster speeds run several ticks per physics frame, so a fast-forwarded
match is tick for tick the match it would have been at 1x. Presentation keeps
up: arrows and sounds follow every tick's events. The smoke measured 16 ticks a
frame at 16x headless; frame rate at 16x on a real display with large armies is
not measured yet.

**Bot fixes found by watching bot against bot.**

- A party's lone survivor kept the assault slot busy while twenty soldiers waited
  at home. Below a third of the party size, survivors now fall back home and the
  next party forms.
- A moving enemy worker led a squad in circles, because attack-move's pursuit
  rules ignore bait. Defenders and intruders now get explicit attack orders.
- An enemy unit tucked into a pocket beyond melee reach (between a Mill and the
  map edge in the fixture) held a party, or the whole home army, forever. A target
  the bot cannot kill in 30 s is now given up on, and units locked on it are freed.
- Both bots starved of food: ore passed 3000 while food sat near zero, so armies
  stopped. Gatherers now split by what the bank lacks (75% on food while ore piles
  up, 25% while food does, else half; at most six per Mill), and the worker target
  grows by two per Mill. Food remains the limit on army size, because a Mill admits
  one worker at a time (the build-17 owner rule); that rule may deserve a second
  look now that bots show its effect.

**Result.** Gimli (defensive, Difficulty 1) against Aragorn (aggressive,
Difficulty 2): Aragorn's assaults from 4:00 cost both sides their armies, Gimli
rebuilds faster and razes Aragorn's base, winning at 30:51 of game time. In the
synthetic duel an aggressive bot crushes the default one at 7:54.

Validation: `-ObserverSmoke` (9 cases) checks both bots, the panel, read-only
inspection, zero player commands, the speed button (16 ticks a frame) and keys,
and a fast-forwarded match to its named winner, and records a two-minute
timeline of both bots. 3 new synthetic checks: a bot against bot match decides
and replays identically, and a broken assault falls back so the next one forms. 760
simulation and 34 worker-cache checks, the Python suite and the 18 other smokes pass.
The economy fix moves the `-AiSmoke` timeline earlier: the default bot's assault
leaves at 3:53 and razes a passive player at 5:12 (5:48 in build-27).

## Build-29: pathfinding performance, first pass

Owner report: large skirmishes dropped frames, worst in bot matches at high
speed. A new profiler (`./prototype/run.ps1 -Profile`, any mode) prints, every
two game minutes, the living unit count and the time spent per simulation section
and hot helper, and Godot's own frame time. On the Gimli against Aragorn observer
match it put the cost almost entirely in pathfinding: presentation (headless)
was about 0.1 ms per tick and the occupancy scans about 0.2 ms.

Two fixes, both leaving every route exactly as it was:

- **Map changes no longer wipe the route caches.** Laying, cancelling or razing a
  building toggled its obstacle, which cleared every cached edge test, including
  the expensive swept-body clearance checks, in the base graph and in every
  body-size graph; the next searches recomputed them across the whole map. Only
  the area a toggled obstacle can affect is forgotten now: its footprint, its
  rasterized nodes and edges, and a margin of a body radius plus three cells. The
  one global switch, a graph's first exact footprint, still drops everything.
- **Searches use flat arrays.** "Can a body stand here" and "is this edge open"
  are cached per cell, and A* runs on reusable stamped arrays with the same
  priorities, neighbour order and tie rules instead of dictionaries, hash sets and
  an allocating neighbour iterator. A search that fails, when units wall off a
  reachable goal, still floods its region, but at a fraction of the cost.

Measured on the same match, headless at 16x: a typical mid-game tick fell from
5-6 ms to 1.7-2 ms, the worst two-minute window from 16.8 to 4.2 ms per tick, and
the frame rate at 16x from 5-11 to 17-26 fps. The match is unchanged: the same
winner on the same tick and the same final state digest.

Still open: stuck units re-plan routes that flood the map, which causes most of
the remaining spikes, and unit combat targeting is the next largest cost. Frame
cost on a real display at 16x is not measured yet. The ideas, measured costs and
the corpse rule are kept in the roadmap's [performance backlog](../tools/Roadmap.md#performance-backlog).

Validation: 760 simulation and 34 worker-cache checks and all skirmish and Helm's
Deep smokes pass; the observer smoke's final digest matches build-28's.

## Build-30: owner playtest fixes

Five issues from an owner skirmish, each following Warcraft III where the
original game is silent or clunky.

**Attack-move razes buildings.** An attack-move fought only units and walked past
enemy buildings. With no enemy unit to fight, it now takes the nearest enemy
building within the 10 m units are acquired at (measured to the footprint), razes
it with the ordinary siege approach and strikes, and resumes its march to its own
destination when the building falls. Units always come first: one coming into
range drops the building, and after the fight the attack-move finds it again. A
building it cannot reach is passed by. The order stays an attack-move throughout
(`SimUnit.SiteTarget`), so its queue, destination and formation slot are kept;
a plain move still ignores buildings. The bots' assaults use attack-move, so they
now also raze what lies on their way (a Watchtower, a Mill) instead of only their
chosen target.

**Foundations go down when a worker gets there.** Ordering a building spent the
money and raised the foundation at once, wherever the workers were, so a bot's
Mill appeared across the map long before its builders. The order now only checks
that the building could go there and that a worker can reach a spot beside it;
the workers walk there (`OrderKind.PlaceBuilding` for open ground, a Build order
on the unclaimed site for a Mill or Foundry), and the first to stand beside the
footprint lays it and pays. Workers bound for the same spot join that foundation.
A translucent ghost of the building marks every pending or queued placement. On
arrival the usual rules apply again: if the money was spent meanwhile, or the
ground is no longer valid, the worker gives up and the console says why
("Construction abandoned: Insufficient resources"). Units standing on the spot no
longer block the order, only the laying: the player's own idle units step aside,
and the worker waits up to 10 s for anyone else. The bots reserve the cost while
their crew walks and give up a crew that cannot lay its building in 30 s.

**Grid hotkeys.** The Build page and the training buttons take Warcraft III's grid
layout: a button's key is its place on the four-by-three grid, rows Q W E R,
A S D F, Z X C V, read by key position so other keyboard layouts work, and the
letter is drawn in the button's corner. The Build button is Q (B still works),
Back and a foundation's Cancel are V (Back also Esc). The unit commands keep their
letters (A, H, G, T, S, M). A key for an unaffordable building says why above the
console instead of doing nothing.

**Waypoint flags.** With units selected that have orders queued, or while Shift is
held, a dashed line runs from the units through each order's point in turn
(green for moves, red for attacks, gold for gathering and building), and the
original gather-point banner (`MODEL_G_GATHER_POINT`, the rally flag) stands at
each point on open ground. Targets need no flag: the unit, building or resource
marks the point, and placements show their ghost. Offscreen captures now include
the lines.

**Rally on a Mill or Foundry.** With the Citadel selected, right-clicking a Mill,
a Foundry, a well or an ore pile sets the rally point on that resource
(`OrderKind.SetRallyResource`): each new worker starts gathering there, and other
units walk to it. A rally point on the ground replaces it.

Validation: 17 new synthetic checks (attack-move razing a building, fighting the
units beside it first and resuming its march, replayed identically; a plain move
walking past; a far placement paying nothing until a worker arrives; joining a
foundation; giving it up when the money is gone; idle units stepping aside; a
Mill claimed on arrival; rally on a resource). 777 simulation and 34
worker-cache checks pass. `-PlacementSmoke` gains four cases (27: the camp waits
for a worker, queued placements show lines and ghosts, the grid keys open the
page and arm the building in their slot), `-ConstructionSmoke` three (31: the
Mill waits for a worker; with the Citadel selected, a right-click on the Mill and
the Q key train a worker who then gathers there) and `-MinimapSmoke` one (16:
Shift-queued orders show two flags and two lines). Frame-scripted smokes now hold
their script while workers walk to a foundation. All other skirmish, AI, observer
and Helm's Deep smokes pass, except `-FormationSmoke`'s `attack_move_arrived`,
which fails the same way on build-29. The default bot's assault now leaves at 3:50
and razes a passive player at 5:10 (3:53 and 5:12 in build-28). Bots razing
buildings on their way changes the observer match: Aragorn now beats Gimli at
28:15 (Gimli won at 30:51).

## Build-31: corpse removal and stuck-unit replanning

Second performance pass. Build-30 belongs to the parallel playtest-fix session.

**Bodies leave after a minute.** A dead unit's model used to stay in the scene,
skinned and animated, for the rest of the match. Sixty seconds after death (the
owner's choice; the original's `WorldVariables.UnitCorpsePersistSeconds` is 10 s,
with a fade) the body now sinks 1.5 m into the ground over two seconds, then its
model is hidden and its node disabled, so its animation player and skinning stop
running and the per-frame presentation loops skip it. The sinking stands in for
the original fade. The simulation keeps the dead unit: unit ids are list indices.

**No more repeated floods for a stuck unit.** The profiler showed the remaining
spikes came from blocked units re-planning: the search from the unit's own cell
fails, then the ground-position fallback retried from each nearby start cell, and
every retry flooded the same walled-off region again (about 20 ms each). A start
that an earlier failed search from the same position already reached cannot reach
the goal either, because its route would have extended that search's, so such
retries are now skipped. Results are unchanged; in the worst two-minute windows
blocked replanning fell from about 7 s and 5.8 s to under 1 s.

The Gimli against Aragorn observer match ends exactly as on build-30 (Aragorn wins
at tick 33895 with the same final digest), and 153 bodies left the field during it.
Its closing siege still shows heavy blocked replanning, about 4 s in the last two
minutes, as armies crowd around the last buildings. Remaining costs, mostly unit movement and
combat targeting, are listed in the roadmap's [performance backlog](../tools/Roadmap.md#performance-backlog).

Validation: the observer smoke gains a corpse check (10 cases): every removed body
is hidden and disabled and was dead for at least 60 s, and no body outstays its
minute and sinking. 760 simulation and 34 worker-cache checks and all skirmish and
Helm's Deep smokes pass.

## Build-32: swept-body tests and exact scan trims

Third performance pass. Every change keeps the simulation bit-identical: the Gimli
against Aragorn observer match ends at tick 33895 with build-30's digest
(61CA70AC...).

The profiler now splits movement and combat into their helpers, which found the
biggest remaining cost: route smoothing. A moving unit tests whether its body can
go straight to a waypoint far down its route, and the swept-body test listed the
cells under that path by scanning the path's whole bounding box. For a long
diagonal that box holds thousands of cells around about a hundred real ones, and
the footprint check after it scanned the same box again. The swept band's test is
linear along each row, so each row's cells are now solved as one interval (same
cells, same order; a temporary check compared both methods on every call of a full
match and all test suites). The traversal test also reuses one buffer and stops at
the first blocked cell.

Smaller exact trims: combat position searches only test bodies and claimed attack
spots that can reach the search area (within its outer range plus spacing); the
nearest-opponent pickers find the minimum instead of sorting every enemy and skip
the acquisition rules for farther units; the occupied-cell set is built with plain
loops in its old insertion order.

| Two-minute window (observer, 16x) | build-31 step | build-32 step | movement before -> after |
|---|---:|---:|---:|
| 10:00 | 2.22 ms | 1.70 ms | 0.89 -> 0.52 ms |
| 12:00 | 2.79 ms | 2.03 ms | 1.39 -> 0.64 ms |
| 14:00 | 2.62 ms | 2.22 ms | 0.97 -> 0.58 ms |
| 20:00 | 1.58 ms | 1.13 ms | 1.08 -> 0.62 ms |
| 28:00 (final siege) | 4.12 ms | 3.27 ms | 3.79 -> 2.90 ms |

Combat stays at 0.8-1.3 ms per tick in the big fights. Most of it is the bounded
search for an attack position (`FindPathToAny`) running into its 2,048-node
budget, and threat scanning (`combat engage`). The final siege is dominated by
blocked replanning. A tried shortcut, proving a goal unreachable by flooding
back from it, was dropped: once in the match the backward flood called a goal
sealed that A* then reached, so the open-edge relation is not exactly symmetric
somewhere, and only 14 of 224 failed floods were that kind anyway.

Validation: 777 synthetic, 844 Helm's Deep and 34 worker-cache checks pass; the
observer smoke passes with the unchanged digest.

## Build-33: unit grid for threat scans, one-pass attack-position search

Combat targeting pass, again bit-identical (the observer match keeps its digest).

**Unit grid.** `UnitGrid` files every unit in an 8 m bucket by ground position;
SimUnit's position setters move it between buckets, so the index is always
current. The scans whose tests have a distance limit now ask the grid for the units
within that limit and keep their exact tests: the immediate-opponent check of wide
bodies (within the attacker's reach bound: its attack or melee reach, or both
bodies' radii plus its gap), small melee units' 10 m opponent choice, the
`Opponents` pool (reach or 10 m, plus targets allies share), room to fight, and
the attacker-slot counts around a target. The grid keeps the largest body radius,
reach and gap seen, so every bound is conservative. A temporary check confirmed
that no query ever missed a unit within its radius, through all test suites and
the full match. The melee threat scan has no distance limit and still checks
every unit; it was already cheap (about 0.05 ms per tick).

**Attack-position search.** A wide body looked for a position within reach and,
if that failed, ran a second search for a waiting position. Both searches expand
the same nodes in the same order, and the first usually failed only after its
2,048-node budget. One pass now returns the first in-reach position closed, or
else the first waiting position it closed along the way, which is exactly what the
second search found.

| Two-minute window | build-32 combat | build-33 combat | whole step before -> after |
|---|---:|---:|---:|
| 10:00 | 0.80 ms | 0.50 ms | 1.67 -> 1.43 ms |
| 12:00 | 1.02 ms | 0.55 ms | 2.02 -> 1.67 ms |
| 14:00 | 1.31 ms | 0.70 ms | 2.27 -> 1.76 ms |
| 16:00 | 0.99 ms | 0.47 ms | 1.83 -> 1.41 ms |

What is left in combat is mainly attack-position searches that still exhaust their
budget (0.1-0.35 ms per tick in big fights). Cutting those further needs a change
in play, such as a smaller budget or remembering a failure for a few ticks.

Validation: 777 synthetic, 844 Helm's Deep and 34 worker-cache checks pass; the
observer smoke passes with the unchanged digest.

## Build-34: smaller attack-position search budget

A deliberate, small change in play, chosen by the owner after an experiment. A
wide body's search for an attack position may now close 1,024 nodes (about an
18 m walk) instead of 2,048; beyond that it waits and plans again later, as it
already did when the search failed. Exact counts from the observer match showed
that about three in four position searches fail in crowded fights, and that a
search running out of budget cost about 1 ms.

Two options were measured on the Gimli against Aragorn observer match (one run
each; any change in play sends the match down a different path, so these are
different battles, not a matched comparison):

| Variant | Winner, end | Failed / all searches | Budget-exhausted search | Combat (match average) |
|---|---|---:|---:|---:|
| build-33 | Aragorn, 28:15 | 3,559 / 4,788 | 997 us | 0.209 ms per tick |
| remember a failure 2 s | Aragorn, 33:34 | 7,924 / 10,171 | 996 us | 0.367 ms per tick |
| 1,024-node budget (kept) | Aragorn, 30:05 | 4,511 / 5,785 | 489 us | 0.184 ms per tick |

Remembering failures was dropped. It skipped only 681 searches, because the planner
already waits a second between attempts and targets change before a repeat. The
match still diverged: bigger armies and longer late fights. The smaller budget
halves each exhausted search and trims 0.1-0.15 ms from the heaviest fight windows.
In the 18-against-18 crowd benchmark all 18 units on each side land hits (17 and 18
before). The trade-off: a wide body gives up sooner on a position that is
reachable only by a long walk around a crowd.

The profile line now also prints each section's exact call count, and failed
position searches are counted as `CombatPosition failed`. The observer match's new
reference digest is 31E28C29... (Aragorn wins at tick 36111).

Validation: 777 synthetic, 844 Helm's Deep and 34 worker-cache checks pass, and
the observer smoke passes.

## Build-35: any multiplayer map, any number of players

Steps 2 and 3 of the skirmish setup plan in the roadmap, done together: extra
player slots can only be tested on maps with more start points.

**Maps.** `python tools/prepare_prototype.py --skirmish --map MP04_Wasteland` imports
any of the 32 multiplayer maps into `converted/skirmish-maps/<stem>` (Bilbo's
Bluffs keeps `converted/skirmish`), and `--all-maps` imports all of them, reporting
any map that fails and continuing. Every start point gets a Good Citadel and four
workers. `./prototype/run.ps1 -Map MP04_Wasteland` plays one. Three maps also carry
editor markers of kinds 4 (script waypoint) and 5 (water ripple generator), which
the decoder rejected before. Mighty Silverlode and Sandbar are river maps: the
ground splits into two halves with two start points each. The importer kept only
the largest walkable region, so it found no ground for half the workers. Now each
base keeps the largest region that reaches its worker ring. In game, a worker
must connect to the first worker of its own side, not to player 1's. All 32 maps
import.

**Crossings.** `SimulationTests --crossing-probe <cache>` lists, for every unit
body, which start bases it can walk between. Every map connects all its bases for
a Swordsman except Mighty Silverlode: its three rope bridges only fit Frodo's
0.46 m body, so both armies stay on their own bank and the match cannot end.
Sandbar crosses its river on wider Human Bridges. Rope bridges wide enough for
armies are left for navigation work.

**Players.** The importer writes eight slot colours from the source
`TeamColors` table: blue and red as before, then green, purple, yellow, cyan,
orange and pink. White `TEAM_COLOR_GRAY` is left out. The order is an adapter,
because the original lobby lets each player choose. Minimap dots take a lighter
shade of each slot's colour. Selection rings mark one's own side, not team 0. A
bot runs every side but the player's (every side while observing). `-Bots`
names personalities by slot in play too; `-AiPersonality` sets the rest. `-Bots
Gimli,Aragorn` passed from PowerShell used to arrive as one space-joined name; it
is now a list. The human is still always player 1; the setup screen (step 4)
will lift that.

**Reachable targets.** A bot's assault party used to march on the enemy building
nearest in a straight line, even across a river it could not cross. It now takes
the nearest building it can stand beside on its own ground (judged for the party's
widest body), and a party counts as sent only once it has a target. Bilbo's Bluffs
is unaffected (same digest).

**One play change.** Idle units of the demo's enemy (team 1) chased any enemy
they noticed, while every other side's idle units only guarded nearby
(`IdleGuard`). The simulation now holds this as `AggressiveIdleTeams`; a skirmish
fills it with every side without a player. A refactor run with the old set
kept the observer match identical (digest 31E28C29...). With the new set, Gimli's
side (player 1) fights back when idle as Aragorn's always did, and the Gimli
against Aragorn match now ends with Gimli winning at 33:33 (tick 40258, digest
75D5F383...). Every two-player fixture with a player keeps the old rule
(only team 1 chases).

**Observer smoke** now accepts any map and number of bots: every side runs its
named bot, the panel lists every player, and the time limit grows by 20 game
minutes per extra player.

| Map | Bots | Result |
|---|---|---|
| Bilbo's Bluffs (2) | Gimli, Aragorn | Gimli wins at 33:33 |
| Wasteland (4) | Gimli, Aragorn, Legolas, Saruman | Gimli wins at 45:01 |
| Mighty Silverlode (4, river) | Gimli, Aragorn, Legolas, Saruman | Legolas and Saruman razed by 24:00, then a stalemate across the river |

In the Wasteland match Legolas had lost every worker by 12:00 (food 0, ore 19)
and never recovered; Saruman also ended without workers. A bot with neither
workers nor money to train one cannot come back, a weakness for the bot work
ahead.

Validation: 778 synthetic, 845 Helm's Deep and 34 worker-cache checks pass, as do
the Python tests, the observer smoke on Bilbo's Bluffs and Wasteland, and the
skirmish, minimap, AI, victory and siege smokes. The observer smoke on Mighty
Silverlode fails by design until rope bridges carry armies.

## Build-36: spectator HUD

The owner asked for a more useful, more compact observer interface, built the way
other RTS observer modes are (StarCraft II, Age of Empires II, Warcraft III). The
build-28 panel at the top left, one long text line per player, is gone.

**Overview (top right).** A panel under the build label lists every player in its
colour, with two tabs:

- **Economy:** food and ore in the bank, income (food and ore delivered in the
  last game minute; the tooltip splits it), population used / capacity, workers,
  army (every living unit that is not a worker) and buildings including foundations
  (the tooltip lists them). Column heads are the console's resource icons and the
  worker, Swordsman and Citadel portraits.
- **Units:** a portrait per living unit type, workers first, with its count.

E and U open a tab; pressing the open tab's key, clicking its button, the chevron
or Tab folds the panel down to its tab strip. A click on a player's name centres the
camera on that player's Citadel (or what is left of the base). A defeated player's
row dims; the bot's status line moved to the name's tooltip. The panel scales with
the console (HUD scale setting).

**Match panel (console).** While observing there is nothing to command, so the
action grid shows the match instead, as the command card does in those games: the
map's name on the strip above it, the game clock, the state ("Duel",
"Free-for-all · 4 players", "3 of 4 players left", "Player 1 · Gimli wins"), a
pause button and a notched speed slider for 1x to 16x (click, drag or scroll it).
`-` and `+` still step the speed in any mode and move the slider. Control group
buttons and the walk/run button are hidden while observing, and the empty
selection reads "Click any unit or building to inspect it".

**Income source.** Each side's bank now also counts what workers have delivered
(`TeamEconomy.GatheredFoodMilli` / `GatheredOreMilli`), credited at the same
put-down that fills the bank. Cheats and refunds do not count. The totals are
derived, so they stay out of the state digest: the Gimli against Aragorn match
ends exactly as on build-35 (Gimli wins at tick 40258, digest 75D5F383...).

Offscreen captures now draw the overview and the match panel too, and give the
capture's console the observer's `PlayerTeam`.

Validation: the observer smoke grows to 15 cases. New: the match panel replaces
the commands (map without the mode suffix, clock, state, no groups or pace
button), the Units tab's counts add up to each side's living units, the tabs
switch and fold by click, E and Tab, a player's name centres the camera on that
base, the slider's 16x notch runs 16 ticks a frame, and the pause button pauses
and resumes; the result check also reads the winner off the match panel. With
`-CapturePath` it saves frames at 10:00 (Economy) and 12:00 (Units). Two new
synthetic checks cover the delivered totals (each credited load counted once, no
effect on the digest): 780 simulation and 34 worker-cache checks pass, as do the
command, orders, ability, skirmish, construction, structure, production, minimap,
victory and observer smokes.

## Build-37: large-map performance

The owner watched an 8-bot free-for-all on MP08_Rift (512 x 512 m, the largest
map; Aragorn, Gandalf, Saruman, Smaug, Gimli, Legolas, Grishnakh, Shelob at 4x)
and saw 1 FPS. A windowed profile, sampled every 5 game seconds, showed two
problems.

**Frame scheduling (no play change).** From about 3:45 of game time, with 150
units, a tick cost 8-28 ms, so 4x (80 ticks a second) could not keep up. Godot
then ran up to eight catch-up physics frames, each a burst of four ticks, before
drawing one frame. Now a speed burst stops once it has used 25 ms of simulation
time in a physics frame, and Godot runs at most one physics frame per drawn frame
(`physics/common/max_physics_steps_per_frame=1`). When the simulation cannot keep
up, the game runs slower than the chosen speed instead of the frame rate dropping;
the ticks are the same ones, spread over more frames. While observing, the match
panel's key hint turns amber and reads "CPU-bound · running at 2.3x" whenever the
measured speed falls below 85% of the chosen one.

**Simulation cost (play changes, owner's choice: "fix both").** The grid has
262,144 cells, 6.5 times Bilbo's Bluffs, so every whole-map search costs that much
more. In the heavy phase two costs dominated:

- *Route rebase, 23%.* Any building placed or destroyed anywhere made every moving
  unit re-plan its whole route, about 0.5 s each time, 25 times in two minutes.
  The navigation now records the cell rectangle around each toggled obstacle
  (`GridNavigation.ChangedAreas`). On a change, only units whose remaining route
  passes within a body radius and three cells of one re-plan, along with units
  whose route never reached its goal, so a removed building can open their way.
  Other routes stay as they are and may miss a shortcut a destroyed building
  opened. Each change now costs about 90 ms on Rift.
- *Blocked-unit replans, 42%.* A unit stuck behind other bodies re-planned its
  whole route around them. When bodies sealed the goal off, the search flooded the
  whole region, 76 ms each on Rift. Three changes:
  1. A stuck unit first tries a local repair: a detour of at most 1,024 nodes
     around the bodies, back onto its own route past the last body within 24
     nodes ahead.
  2. Failing that, the full replan stops after 32,768 nodes. Measured with no cap,
     every successful replan on Bilbo's Bluffs closed at most 16,384 nodes; on
     Rift 98% stay under 32,768.
  3. A retry after a failed replan, which leaves no route to repair, gets 8,192.
  A search that runs out counts as a failure, and the unit yields as before.

**Bot fix found on the way.** With routes changed, a synthetic bot duel left 43
soldiers idle. The bot judged whether its assault could reach a building from one
party member, and that soldier stood inside its own Mill's footprint, from which
nothing is reachable. The check now uses a widest member standing on open ground.

| Rift, 4x, windowed (game time, units) | before: FPS (median) / tick | after: FPS (median) / tick |
|---|---|---|
| 0:06-1:30 (32-86) | 22-171 (112) / 3.1 ms | 75-167 (121) / 1.4 ms |
| 1:45-3:30 (92-134) | 2-102 (58) / 6.6 ms | 9-94 (63) / 5.1 ms |
| 3:45-4:10 (143-163) | 1-16 (2) / 17.6 ms | 7-43 (20) / 11.3 ms |
| 4:15-5:15 (164-197) | not reached | 7-19 (17) / 17.3 ms |
| 5:20-6:20 (201-215) | not reached | 9-20 (13) / 16.0 ms |

With the frame budget alone, 3:45-4:10 ran at a median of 15 FPS with 18 ms
ticks. From 3:45 on, per tick, blocked replans fell from 9.8 to 5.1 ms, failed and
capped searches from 6.1 to 0.7 ms, and route rebase from 5.4 to 0.9 ms. Big
fights on Rift still cost 11-17 ms a tick, so 4x runs at about 2-3x there.
`OccupiedNodes` (every unit scanned about 17 times a tick, 3.2 ms)
and long but successful replans remain; see the roadmap's performance backlog.

**Observer match.** The routes changed, so the matches changed, and two reference matches now stall instead of finishing. Gimli against Aragorn on Bilbo's Bluffs, which Gimli won at 33:33 in build-35, starves both sides: by 24:00 Aragorn has no workers, Gimli bleeds its last workers and neither army reaches assault size again, so the observer smoke's 40-minute result check fails (the other 14 checks pass). The 4-bot Wasteland match (Gimli won at 45:01 in build-35) freezes at 40:00: three bots have no workers, and Saruman lost its Citadel, so its six workers cannot deliver and it cannot afford a new one. With the bots swapped (Aragorn, Gimli) Bilbo's Bluffs ends with Aragorn winning at 26:00 (tick 31205). Going by the bots' status lines, these are bots that cannot recover once their economy is gone (open since build-35), not armies stuck on routes. (Build-38 resolves both: see below.)

Validation: 780 synthetic, 847 Helm's Deep and 34 worker-cache checks, and the
sandbox, combat, melee, surface, obstacle, engagement, command, orders, ability,
skirmish, construction, structure, production, placement, Dwarf Hall, minimap,
Frodo, siege, defense, victory and AI smokes pass. The observer smoke fails only
its result check, as described above. The formation smoke keeps its known
`attack_move_arrived` failure (since build-19).

## Build-38: bots expand, go all in and surrender

Build-37's new routes left two reference bot matches in a stalemate. The owner
asked for a recovery rule, for bots that expand at around 10-15 minutes, and for a
bot with no units and no money for one to surrender. All of it is in
`prototype/Simulation/SkirmishAi.cs`. It acts through the same commands a player
issues and replays identically.

- **Expansion.** From 10:00 (aggressiveness 0) to 15:00 (aggressiveness 100), the
  bot lays a Citadel by the unclaimed resource nearest home that lies outside every
  own Citadel's reach (its 25 m building radius), at least 30 m from enemy
  buildings, on ground a worker can walk to. It lays one more every 10 minutes, up
  to three. The source Citadel has no "near your buildings" rule, accepts deliveries,
  trains workers and adds 10 population, so an expansion feeds itself; Mills now go
  on wells within 80 m of any own Citadel, not only home. A Citadel spot with no room
  is never tried again, so its reserve cannot freeze the army.
- **Rebuilding.** A lost Citadel is the first building the bot wants back, at the
  old home unless an enemy building is near it.
- **All in.** With no soldier trained or sent for 3 minutes and none affordable,
  every idle soldier attacks whatever the party size, and does not fall back.
- **Surrender.** A bot gives up when it can never field an army again: no soldier,
  hero or unit in training, no worker able to earn (none alive, or no Citadel to
  deliver to), and no money for any unit its buildings offer, nor for a new Citadel
  while workers remain without one. Its buildings are torn down the way razing
  removes them, so the ordinary Razing rule defeats it. The overview's tooltip
  reads "Surrendered".
- **Safer gathering.** Workers and new Mills avoid resources within 30 m of enemy
  buildings while a safe one exists. Each lost worker had been retrained from the
  food meant for soldiers.
- **Patience by progress.** A crew walking to lay a building is given up after
  30 s without getting closer, not after 30 s in total, so a long walk to an
  expansion goes through.

Watching the matches exposed three bot freezes, each fixed:

- *Reachability from a blocked cell.* A unit at a work spot can stand on a
  footprint cell, from which nothing is reachable. Expansion sites are now judged
  from a worker on open ground, like build-37's assault check.
- *Workers counted as a threat.* With expansions in the middle of the map, enemy
  workers gathering nearby kept a bot "threatened" for good, and a threatened bot
  never attacks. They are still attacked, but only enemy soldiers hold the assault
  back now.
- *Alternating targets.* The 30 s give-up timers (party defenders and home
  intruders) restarted whenever the nearest enemy changed. Two units stuck in
  pockets, taking turns as the nearest, held a party at home for 20 minutes. Each
  enemy is now timed from when it came within range without a break, and enemies
  that left are forgotten.

| Match (observer, 16x) | build-37 | build-38 |
|---|---|---|
| Bilbo's Bluffs, Gimli vs Aragorn | no result in 40 min | Gimli wins at 33:26 (tick 40133, digest 9C0DC103...) |
| Bilbo's Bluffs, Aragorn vs Gimli | Aragorn wins at 26:00 | Aragorn wins at 25:18, Gimli surrenders |
| Wasteland, Gimli, Aragorn, Legolas, Saruman | frozen from 40:00 | Gimli wins at 48:15, Legolas surrenders at about 10:00 |

In these matches Gimli (aggressiveness 35, due 11:45) had a second Citadel by
14:00 and a third by 24:00; Aragorn and Saruman had their second by 16:00. The
timeline samples every two minutes.

Validation: 5 new synthetic checks (a bot with no units and no money surrenders;
one whose workers can still earn does not; a small army waits while it may grow,
then a spent bot sends its last soldiers all in; the bot expands by far ore after
its time and not before). 785 synthetic, 852 Helm's Deep and 34 worker-cache
checks pass, as do the sandbox, combat, melee, surface, obstacle, command,
orders, ability, skirmish, construction, structure, production, placement, Dwarf
Hall, minimap, Frodo, siege, defense, victory, AI and observer smokes. The
observer smoke's result check passes again.

## Build-39: skirmish setup screen

Step 4 of the skirmish setup plan in the roadmap: a match no longer needs
command-line flags. `./prototype/run.ps1 -Skirmish` on its own (or `-Setup`) opens
a lobby, `prototype/SetupScreen.cs`, laid out like those of Warcraft III and
StarCraft II.

- **Maps.** Every source map in `extracted/Levels/Multiplayer` and every imported
  cache, sorted by player count. Each shows its title and start positions; one not
  imported yet is dimmed, and its tooltip and the status line give the
  `prepare_prototype.py --skirmish --map <stem>` command.
- **Preview.** The map's walkable ground shaded by height (the in-game minimap's
  look), wells and ore piles, and each start position numbered in its slot colour.
  Yours is ringed in gold and a closed one is struck out. Map sizes come from the
  first bytes of `navigation.json`, so listing 32 maps parses no grids; a grid is
  read only for the preview of the map on show.
- **Slots.** One row per start position: You, Closed, or a bot from the cache's
  13 `.rai` personalities with its difficulty (Easy, Normal, Hard). Choosing You
  for a slot hands the previous one to the default bot. With no slot set to You,
  you watch (observer mode). Start needs two open positions.
- **Speed.** The match panel's notched slider, 1x to 16x.

Start keeps the choice in a static field and reloads the scene (`prototype/Main.Setup.cs`).
The reload loads that map with you in the chosen slot (`PlayerTeam`), a bot in every
other open slot and nothing in the closed ones. A closed slot gets no workers, Citadel,
bank, drop-off, producer or bot, and its start footprint is freed, so the Razing
outcome counts only open slots. Restart replays the same choice. The result panel's
new Setup button and the debug menu's Skirmish setup button return to the screen,
which starts from the last choices. Launch flags that choose a match skip the screen,
so every existing smoke runs as before.

**You in any slot.** The human was always player 1. The last places that assumed
it now ask `PlayerTeam`: the result title, the foundation Cancel button, repairing
your own Mill or Foundry, the colours of the placement ghost and the rally banner,
and whether a clicked unit is an enemy.

Validation: `-SetupSmoke` (10 cases) drives the screen with press-and-release clicks
across three scene reloads:

1. It lists every map, Bilbo's Bluffs (2) and Boneyard (8) imported.
2. On Boneyard it puts you in slot 3, closes slots 6-8, names Gimli and Aragorn for
   slots 1-2, sets 2x, and checks the status line. The match must load Boneyard with
   you as player 3 (four workers, your resource strip, no bot of yours), five
   contenders, nothing in the closed slots (no units, buildings or bank; footprints
   inactive), bots in slots 1, 2, 4 and 5 with the named personalities, at 2x.
3. The debug menu's button returns to the screen with those choices kept. Then
   Gimli against Aragorn on Bilbo's Bluffs, observed at 16x, must load as chosen.

The sandbox, combat, melee, surface, obstacle, command, orders, ability, skirmish,
construction, structure, production, placement, Dwarf Hall, minimap, Frodo, siege,
defense, victory, AI and observer smokes pass. The observer match keeps build-38's
digest (Gimli wins at tick 40133, 9C0DC103...), so making the human's slot a
choice changed no bot match. No simulation code changed.

## Build-40: expansion guards, home Foundry, trapped spawns

Owner playtest of build-39 (1v1 on Bilbo's Bluffs): expansions fell to the first
raid, Aragorn's bot built its Foundry on an expansion's ore pile but never on the
pile at its main base, and from about 13:00 the frame rate dropped to 18-20 FPS
and never recovered.

- **Expansion guards (owner rule).** Every Citadel the bot raises more than 30 m
  from its start, foundations included, keeps half an assault party (rounded up:
  six for the most defensive personality, three for the most aggressive; Aragorn
  4, Gimli 5) as its guard. Guards are recruited from the idle home army, nearest
  first, while the base is not under threat, before an assault may form, and
  never join one. They hold a cell 12-24 m from the Citadel toward the nearest
  enemy building, 10 m clear of resources so they stay out of the gatherers' way.
  They attack enemies within 30 m of their Citadel (soldiers first, given up on
  after 30 s like the home army's), walk back once the fight is over or has led
  them 10 m beyond that radius, and are released when the Citadel falls. The home
  army still answers raids on an expansion too. The bot's status reads
  "guards N".
- **Foundry at home.** The plan skipped any ore pile where `CanStartConstruction`
  refused with "Placement blocked by a unit": at a raw pile the bot's own miners
  always stand on the Foundry's foundation, so the first Foundry went to a pile
  nobody mined, which on Bilbo's Bluffs is the expansion's. The plan now takes the
  nearest pile anyway. While a crew walks over to claim it, the pile's empty-handed
  miners stop and wait within 15 m (no new orders), the arriving crew moves the
  idle ones off the foundation as it does for open-ground buildings, and once the
  foundation is down they go back to mining.
- **Trapped spawns (the FPS drop).** `-Profile` showed the `production` section
  climbing from 0.1 ms to 21 ms per tick between 08:00 and 12:00, and on.
  Aragorn's second Barracks recorded its builder's cell as the exit; a Swordsman
  (body radius 570 mm) cannot stand there, and the nearest cell it can stand on lay
  in a ten-cell pocket between buildings. Five Swordsmen spawned into it; once it
  was full, the sixth waited for room forever, re-running the exit search every
  tick. Each search rescanned a 49 x 49 window up to 16 times, testing every cell
  against every unit's node.
  - A trained unit (and a worker leaving a Mill or Foundry) now exits into a
    region of at least 200 cells (`SimWorld.OpenRegionCells`); the nearest
    standable cell in a smaller one is passed over while an open region is within
    the window (`GridNavigation.NearestOpen`, region sizes counted by the
    connected-region flood, `ComponentSize`).
  - The exit search is one sorted pass (`GridNavigation.NearestSequence`) that
    tests the region before the reserved cells. A synthetic check compares it with
    the repeated `Nearest` calls on random grids for both body kinds.
  - The trapped Swordsmen had also stalled Aragorn's war: a party member that can
    never arrive keeps the party alive, and no new party forms while one exists,
    so the home army piled up around the Barracks (29-39 units within 30 m). Units
    standing in a pocket smaller than 200 cells no longer join a party or a guard,
    and leave a party they are in.

With the spawn fixed, the reference match no longer slowed down but froze instead
(no unit died from 24:00 to 40:00). A dump of every unit showed both home armies
standing at home on explicit Attack orders against units 150 m away at the other
base, with no route planned. The orders came from base defence: Gimli had put a
Mill by the ore pile Aragorn mined, so Aragorn's miners there were intruders, and
an explicit attack (like a bot's idle units) pursues an enemy anywhere. A soldier
on such a chase is never idle, so no party formed again.

- **Defence leash.** A home soldier whose enemy is more than 12 m away and more
  than 40 m (the defence radius plus 10 m) from every building of its side stands
  down. A soldier fighting an enemy beside it is left alone, wherever it is.

The four-bot Wasteland match then froze too, from about 44:00 to the 80-minute
limit: Saruman kept 29 soldiers at home behind a party of two (a third of its party
size of six, so not "broken") that never came back, and only one party is ever out.

- **Reinforcements.** Once a full party's worth of idle soldiers stands at home
  while a party is out, they join it and attack-move on its target; the party keeps
  its target and its count in `PartiesSent`.

| Match (observer, 16x) | build-39 | build-40 |
|---|---|---|
| Bilbo's Bluffs, Gimli vs Aragorn | Gimli wins at 33:26 (tick 40133, digest 9C0DC103...) | Aragorn wins at 17:54 (tick 21478, digest F4F223E2...) |
| Wasteland, Gimli, Aragorn, Legolas, Saruman | Gimli wins at 48:15 | Aragorn wins at 18:33 (tick 22259, digest AA0B9FFE...) |
| Simulation per tick on Bilbo, two minutes to 10:00 / 12:00 | 11.9 / 22.2 ms and climbing (the stuck Barracks) | 0.65 / 0.74 ms, at most 1.1 ms all match |

On Bilbo's Bluffs both bots' first Foundry goes on their home pile. Aragorn
expands at about 15:00 with its four guards there, and its second party, grown by
reinforcements to 21, razes Gimli by 17:54. Each bot keeps one Foundry, as before:
the Foundry's `AILowerPriorityIfExists` column is true. Reinforced waves make the
aggressive personalities much stronger; with leash but without reinforcements
Aragorn won at 31:33 (tick 37862), and Wasteland froze. The timings are headless;
the GPU side was not measured.

Validation: 12 new synthetic checks (the exit search's single pass matches repeated
`Nearest` calls on random grids; a trained unit exits beside its producer, not into
a nine-cell pocket by the anchor; the first Foundry goes on the mined home pile, not
a far one; an expansion draws half a party as its guard, which holds a cell beside
it, stays out of the assault, attacks a raider there and is released when the
Citadel falls; home soldiers chasing an enemy far from every building stand down;
idle soldiers enough for a party reinforce the party out; soldiers walled into a
pocket stay out of the assault party). The pocket, Foundry, leash and
trapped-soldier checks each fail with their fix reverted.

## Build-41: bot armies regroup and keep walking

Owner report from a bot game: Saruman's army often stopped, moved a little and
stopped again, and bot armies marched out in a thin line that defenders picked off
at the first narrow place.

- **Stop-and-go (simulation).** A party fights off defenders near it with explicit
  Attack orders, and the home army answers intruders the same way. A body wider
  than 350 mm (Swordsman 570 mm, Axethrower 624 mm) finds its attack position with
  a bounded search: `PositionSearchBudget` closes 1,024 nodes, about an 18 m walk
  (build-34). Every position lies within 4.5 m of the target, so against a target
  20-90 m away the search always failed: the unit stood with no route, retried
  once a second (and only on its planning turn, one in `Units.Count`/3 ticks) and
  stepped forward only when the target happened to come close. A party trace on
  Bilbo's Bluffs (Saruman against Aragorn) found Saruman's party members standing
  on such orders in 2,286 of 10,036 samples. Now a wide body more than 12 m
  (`PursuitRangeMm`) from its target walks a full route toward it first
  (`SimWorld.Pursuit`: the nearest open node by the target, the ordinary
  replan budget), re-planned every second as the target moves, and a pursuer with
  no route left plans at once instead of waiting for its turn. Within 12 m the
  bounded position search takes over as before. Player orders get the same fix:
  Swordsmen told to attack an enemy 60 m away used to stand still too.
- **Regrouping (owner request).** A party used to receive one attack-move to its
  target. Now it marches in legs (`AiPlayer.Legs`). It follows the widest member's
  route: for a new party from the member nearest home, after razing a building from
  the member nearest the party's centre. A new party first musters 15 m along that
  route, then regroups every 25 m, each time on the most open ground within the
  next 8 m of route (the most standable cells in a 7 x 7 m square, never a
  passage). It marches the last 25 m (15 m after the muster) straight at the
  target and razes it as before. The party moves to the next waypoint once every
  member stands within 12 m of the current one, or once the member farthest from
  it has not got closer for 10 s (one boxed in or unable to reach it). A fight, or
  a building razed in passing, holds the march. Members that stopped short (back
  from a chase, a reinforcement, a slot taken) are sent on to the waypoint, and
  reinforcements head for it too. The bot's status reads "regrouping N/M".
- **Not changed.** Party size, targets, defence, the fall-back and the all-in
  rules. Formation slots at a waypoint come from the ordinary group-move
  formation, so a waypoint in a narrow valley still lines the party up. The
  openness test keeps waypoints out of passages, but a long narrow stretch
  between waypoints is still walked in file.

Bilbo's Bluffs, Saruman against Aragorn, traced every 10 ticks (one party
member sample each):

| | Build-40 | Build-41 |
|---|---|---|
| Saruman's party standing still on a move or attack order, not fighting | 39% | 7% |
| Party spread while marching (median of the 90th-percentile member's distance from the centre) | 42.6 m | 4.1 m |
| Result | no winner by 50:00 (both economies broken) | Aragorn wins at 19:23 (tick 23269) |

Cost, with `-Profile` on the 8-bot MP08_Rift free-for-all (Aragorn, Gandalf,
Saruman, Smaug, Gimli, Legolas, Grishnakh, Shelob), physics time per tick: pursuers
now plan full routes, but they no longer re-run failing position searches every
second. The build-41 run was stopped at 10:00 at the owner's request; the matches
diverge, so this is a trend, not an exact comparison.

| Game time | Build-40 | Build-41 |
|---|---|---|
| 06:00 | 24.4 ms | 8.9 ms |
| 08:00 | 26.2 ms | 14.1 ms |
| 10:00 | 37.3 ms | 15.9 ms |

Build-40 then climbs to 56-106 ms per tick from 18:00 (not measured for build-41).

Validation: `SimulationTests` passes all 800 checks (3 new). A Swordsman-sized body ordered
to attack an enemy 65 m away walks there without standing still and strikes
(with the fix disabled it stands for the whole minute), and a new bot party
musters on its route, not at the target, and moves on only once every member
stands within 12 m of the muster point.

## Build-42: buildings on the minimap, start Citadels clear of resources

Owner requests: show buildings on the minimap at their real size, and keep
buildings (start Citadels included) at the minimum distance from resources.

- **Minimap buildings.** Every standing building and foundation is drawn as its
  footprint polygon (`GridNavigation.Footprints`) in its team's colour, over the
  resource markers, so a Mill or Foundry shows its owner; foundations are half
  transparent until complete. A Citadel is 9.6 x 7.3 m: about 8 px on Bilbo's
  Bluffs (200 m), 3 px on the 512 m maps.
- **Start Citadels.** The source rule is `Objects.MinBuildingPlacementRadius`: no
  building within 6 m of an ore pile or 7 m of a food well (measured here from the
  resource's centre to the Citadel's footprint). The importer used to put each
  Citadel straight on its start marker, and on most maps that put a pile or well
  inside the radius, some under the Citadel's walls (MP02_Jungle_Fever,
  MP08_Crazy_Corners). `skirmish_map.start_position` now keeps the marker when it
  is clear, else moves the Citadel to the nearest spot on a 0.5 m lattice within
  20 m where every resource is clear and every cell under the footprint is source
  building ground (`PATHING_CATEGORY_BUILDING`). Walker bits and scenery are not
  checked: several markers (MP04_Spire_of_Mordor, MP07_Choke) already stand over
  them in the source. Workers spawn around the new position.
- **Moves.** Bilbo's Bluffs, MP04_Mighty_Silverlode and MP04_Wasteland are
  unchanged; elsewhere most Citadels move 1-10 m, the largest 16 m
  (MP04_Spire_of_Mordor, slot 3, where building ground is narrow). The other 31
  map caches need a re-import (`prepare_prototype.py --skirmish --all-maps`).
- **Placement (owner follow-up).** Every open-ground building the player or a bot
  places obeys the same radius: `CanPlaceBuilding` refuses a footprint within it
  with "Too close to a resource". The radius travels in the economy cache as
  `sources.<kind>.building_clearance_mm` (refresh old caches with
  `python tools/skirmish_economy.py --cache <cache>`); the runtime keeps it in
  `SimWorld.ResourceClearanceMm`, outside the state digest. Mills and Foundries go
  on their resource as before. Bots keep their own 10 m centre clearance on top.

Validation: 4 new importer tests (polygon distance, a clear marker stays, the
shortest step out of a well's radius, unbuildable ground avoided and no room
rejected), the economy import tests check the exported radius, a new placement
check refuses a Camp inside a well's radius and accepts it just outside, and
`-MinimapSmoke` gains `buildings_drawn_at_footprint_size` (17 checks). On Bilbo's
Bluffs the home pile and well are 12-13 m from the Citadel, so `-PlacementSmoke`
now looks for its three spots up to 25 m out instead of 19 m.

## Build-43: no more frozen fighters

Owner report after build-41: unit groups still froze at random. Traced bot matches
on Bilbo's Bluffs (every party member logged every 10 ticks) sorted the remaining
freezes into three causes:

- **Off the unit's own graph.** A unit can stand on a node its body's graph
  refuses: its body clears the obstacle, but the centre of its cell does not
  (beside a building it razed, at a work spot on a footprint). Nothing is connected
  to such a node, so every combat search from it (attack positions, the build-41
  pursuit, choosing an opponent) failed and the unit waited for good, up to four
  minutes. This was about half of the frozen fighters. Combat now plans from the
  nearest node within 4 cells the unit can stand on (`SimWorld.PlanningNode`),
  where its route leads it first; the pursuit route starts there too.
- **A whole army on one enemy.** The home army, expansion guards and a party
  fighting off defenders all got one explicit Attack on the same enemy. Only a few
  fit around it, and the rest stood waiting 6-12 m away. Bots now spread the
  attacks (`AiAttackSpread`) evenly over every enemy in range not given up on, at
  most four on each (`AiAttackersPerEnemy`), the nearest to each soldier first and
  enemy soldiers before workers. Every enemy in range is now timed for the 30 s
  give-up, not only those up to the first one still valid. When every enemy has
  its share, the rest join the nearest.
- **A march held by one siege.** One Axethrower whose attack-move passed in range
  of the party's target building started razing it alone. The build-41 regroup
  treated any siege as a fight and held the march, so 37 soldiers waited 14
  minutes at a waypoint. Now a member razing the target takes the whole party in,
  and buildings razed in passing no longer hold the march (fights still do).

Same seeds, build-42 against build-43 (party members ordered to move or attack,
not striking):

| Match | Standing still | Freezes of 3 s or more | Longest |
|---|---|---|---|
| Saruman vs Aragorn | 33% -> 8% | 28 -> 1 | 240 s -> 16 s |
| Gimli vs Aragorn | 34% -> 7% | 75 -> 2 | 195 s -> 51 s |
| Aragorn vs Gimli | 19% -> 9% | 18 -> 4 | 69 s -> 33 s |

Saruman beats Aragorn at 22:42 (tick 27234), Gimli beats Aragorn at 13:47 (tick
16534) and Aragorn beats Gimli at 12:36 (tick 15121). Still open: a regrouped
party of 30 or more can jam in a narrow pass on its final approach (about 40 s
in one Aragorn match); that is crowd traffic, not a stuck order.

Validation: `SimulationTests` passes all 803 checks (2 new). A wide body left
where its cell centre is too close to a wall walks around it to its target and
strikes (with `PlanningNode` disabled it never moves), and eight home soldiers
answer three raiders 3/3/2.

## Build-44: Citadel 2 and the Tier 2 buildings

Owner request: carry on with the Good technology tree from the October 22
original-game captures. This build opens Tier 2; its units come next.

| Item | Source / capture |
|---|---|
| Citadel 1 to 2 | `Buildings.PromotesTo`; Citadel 2 costs 115 food / 105 ore, `UpgradeTime` 50 s, 1000 health, armor 4 (capture 011) |
| Camp to War Camp | 0 food / 55 ore, 40 s, 500 health, population 15 instead of 11 (capture 015: 21 to 25) |
| Wilderness Outpost, Ranger Post, Forge | Placed by workers, each `BuildingRequired1` = Citadel 2: 85/110, 95/100 (capture 014), 75/100 (capture 010) |
| Prerequisites | Every building's and unit's source `BuildingRequired` list; an upgraded building still counts as what it was built as, so Citadel 2 satisfies "Citadel 1" |
| Upgrade panel | The next building's portrait in the bottom-left action slot (Z), as the original shows Citadel 3 there (capture 011); during the upgrade its portrait heads the queue with the purple bar (capture 003) |

- `SimWorld.Upgrades` maps a building to the one it becomes. `OrderKind.Upgrade`
  pays the promoted building's cost; `CancelUpgrade` refunds its source
  `RefundFoodPct`/`RefundOrePct` (75%). On completion the site's `Building`
  changes in place: health rises by the difference of the maxima (damage carries
  over), a start building's `StartBuildings` entry follows, and a producer takes
  the new building's unit list.
- Training waits while the building upgrades; queued units stay queued. The
  original shows the upgrade at the head of the queue, so this is an adapter.
- The upgraded building keeps its footprint. The source boxes match: the Citadel
  levels share one, the War Camp is 1 cm wider than the Camp.
- Locked buttons say what is missing ("Requires Citadel 2"), on the Build page,
  the training buttons and the upgrade.
- The view swaps to the promoted model (`MODEL_G_KEEP_LEVEL_2`,
  `MODEL_G_CAMP_UPGRADE`), and picking follows the new geometry. Citadel 2 has
  no source construction state 18; a missing state shows the healthy one.
- Importer: `skirmish_map.TIER2_BUILDINGS`/`SKIRMISH_BUILDINGS`,
  `skirmish_construction.upgrade_data` (`construction.upgrades`) and
  `export_buildings`, which converts every skirmish building model (twelve now)
  on a refresh. Production exports each unit's `prerequisites`. The interface
  export takes every Good building and unit portrait.
- The Build page now holds ten buildings plus Back, 11 of the 4x3 grid's 12
  slots. Tier 3 adds three more (House of Lore, Elven Sanctuary, Nature Haven), so
  it needs the paging or categories the owner deferred at build-16.
- Not yet: Citadel 3-5, Ballista Tower, Forge and Outpost research, the Rider,
  Shieldbreaker and Ranger, and bots using any of it (they skip what they cannot
  build). Aragorn and the other heroes at the Citadel need fate points in the
  source and stay unavailable.

Refresh an existing cache with
`python tools/skirmish_construction.py --cache <cache>` (it also refreshes the
interface art and production data) and `python tools/skirmish_ai.py --cache <cache>`.
Owner rule (Oct 23): tech-tree builds are developed on Bilbo's Bluffs only and the
other map caches are refreshed once when the series is done; until then other maps
in the setup screen may ask for this refresh.

Validation: `-TechSmoke` (15 cases, credited test bank) drives it all through the
UI: Tier 2 buttons locked, the Citadel upgrade with its tooltip and cost, the
worker queued behind it, cancel and the 75% refund, the Z hotkey, training paused
for the upgrade, Citadel 2 with 1000/1000 health and its own model, training
resuming, the War Camp upgrade taking capacity from 21 to 25, and a Forge laid for
75/100. `-PlacementSmoke` now expects the ten-building page. `SimulationTests`
passes 813 checks (10 new in `UpgradeChecks`: cost, paused training, 75% refund,
promotion keeping damage, prerequisite lineage, replay and digest). Python tests
check the exported upgrades, Tier 2 costs and population. Bot matches are
unchanged: the observer smoke still ends with Gimli winning at tick 16534.

## Build-45: the Tier 2 units

The Rohan Rider, Dwarf Shieldbreaker and Ranger join the roster
(`skirmish_map.SKIRMISH_UNITS`), with source models, clips, weapons and portraits.

| Unit | Trained at | Requires | Cost | Health / attack / armor |
|---|---|---|---|---|
| Rohan Rider | Barracks | Citadel 2 | 85 food, 30 ore, pop 3, 24 s | 235 / 13 blunt / 1 |
| Dwarf Shieldbreaker | Dwarf Hall | Forge | 90 food, 65 ore, pop 3, 24 s | 305 / 10 blunt / 1 |
| Ranger | Ranger Post | - | 50 food, 40 ore, pop 2, 22 s | 200 / 8 cutting / 0 |

All match the October 22 captures (skirmish-006/007, -021/030, -025/028). Locked
training buttons name the missing building; bots skip units they cannot train yet.

Not yet: the Ranger's Camouflage, Eagle Eye and Tracking, the Rider's knockback
immunity (there is no knockback), and Forge research (Shield Crushing, Axes of
Durin). Bots still never upgrade their Citadel, so they train none of these.
The Rider's collision body is the source bounding circle, 883 mm (a Swordsman's is
570 mm); a horse is longer than wide, so this is an adapter.

Two fixes found while testing them:

- **Rally points gather units around them.** Every trained unit used to walk to
  the exact rally cell. The first one parked there and the next waited beside it
  forever, still on its Move order. A trained unit now takes the free cell nearest
  the rally point (`SimWorld.RallySpot`), Warcraft style. Bots set no rally points,
  so bot matches are unchanged by this.
- **A wide body re-picks a blocked siege spot.** A Rider sent at a building walked
  to a spot between two fighters already striking it, could not fit, and waited
  mid-step for good. Once a second, a unit still walking to its siege spot checks
  whether a standing unit blocks its body there and takes a spot it fits instead
  (`FitsAmongStanding`). Known limit: a wide body blocked on its route rather than
  at its spot (squeezing between two fighters on the way) can still stall; the
  general wide-body movement fix is open.
  Bots' sieges use the same code, so the reference bot match moves: Gimli still
  beats Aragorn on Bilbo's Bluffs, now at 17:18 (tick 20773) instead of 13:47
  (tick 16533; the same with the re-pick disabled). Whether that is better play
  was not measured.

Validation: `-Tier2Smoke` (11 cases, credited bank and the instant build/train
cheat as fixture) places Barracks, Dwarf Hall and Camp, checks the Rider ("Requires
Citadel 2") and Shieldbreaker ("Requires Forge") locked, upgrades to Citadel 2,
places a Ranger Post and Forge, trains all three with their tooltips and costs,
rallies them to one point, checks the Rider's panel, box-selects them and
force-attacks the Citadel until all three strike. It passes in both the headless
and the capture window layout (they pick different building spots).
`SimulationTests` 814 checks (one new: two units trained to one rally point stand
on different cells; it fails without the fix). `-PlacementSmoke` and
`-DwarfHallSmoke` now expect the second, locked unit at the Barracks and Dwarf Hall;
every other Bilbo smoke passes unchanged.

## Build-46: Forge and Wilderness Outpost research

Research from the source `Techniques` table (`tools/skirmish_research.py`, manifest
`research`). A technique is offered when its building is in the skirmish set, its
`BuildingRequired` is too, and its whole effect is a unit damage or armor bonus:

| Building | Q | W | Cost | Time |
|---|---|---|---|---|
| Forge | Iron Blades: +1 attack | Iron Plating: +1 armor | 75 food, 75 ore each | 90 s |
| Wilderness Outpost | Etched Blades: +1 attack | Improved Leather: +1 armor | 75 food, 75 ore each | 90 s |

Iron Blades and Iron Plating cover the Gondor Swordsman, Rohan Rider, Dwarf
Axethrower and Dwarf Shieldbreaker (the four units the captured tooltip names,
skirmish-017/018); Etched Blades and Improved Leather the Ranger (and the Elven
Archer, not yet in the skirmish roster). Names follow the shipped English manual's
hotkey table (PDF page 21), which also gives the grid positions: the source
`SpecificIndexForPurchasing` (Q = blades chain, W = armor chain). The capture's
"Iron Weapons"/"Iron Armor" translate the Russian interface.

- **Bonus values (adapter, verify in game):** each affected unit has a source
  `TechniqueUnitModifiers` row with `DamageAddifier` 1 or `ArmorAddifier` 1, added
  here to its weapon DPS and armor, so a Swordsman shows Attack 11 and an
  upgraded Rider Armor 2. The technique row itself also carries
  `DamageModifier` 1.2 (x1.2 would make Attack 12); how the two combine is not
  recovered. A capture of a Swordsman's panel after Iron Blades settles it.
- Research is paid on request, takes the source `BuildTime`, one technique at a
  time per building (not while it upgrades), and applies to the team's living
  units and every unit trained later (`SimUnit.TechniqueDpsMilli`/`TechniqueArmorMilli`,
  `SimWorld.Researched`). Cancelling refunds the full cost (no source refund field).
- The research heads the building's queue with its purchase icon and the purple
  bar, like the Citadel upgrade; its portrait (or Backspace) cancels it.
- Next steps of each chain (Steel, Mithril; Mystic, Runic; Hard and Studded
  Leather) need Citadel 3 or 5 and stay unavailable. Not yet simulated: Shield
  Break and Camouflage (abilities), Dwarven Engineering and Lookout Tower
  (buildings), Eagle Eye and Eagle Fletching (sight and range), Razored Edge and
  Flaming Axe (weapon changes). Bots do not research.

Refresh an existing cache with `python tools/skirmish_research.py --cache <cache>`
(it also exports the purchase icons).

Validation: `-ResearchSmoke` (6 cases; credited bank, instant build until the base
and a Swordsman stand, then real time): the Outpost offers Etched Blades and
Improved Leather, the Forge Iron Blades (Q) and Iron Plating (W) with source
costs; Q starts Iron Blades and pays, the queue shows it and W waits; cancel
refunds; a second Q completes after exactly 1800 ticks, and the Swordsman trained
before it shows Attack 11. `SimulationTests` 827 checks (13 new in
`ResearchChecks`, including a duel where the researched bonus raises melee
damage). Python tests check the exported techniques.

## Build-47: bots climb the tree

Owner request: bots upgrade the Citadel, build Tier 2, train the new units and
research. The source has no decision code for any of it (the `.rai` files hold
only the 0-100 knobs), so these are adapter rules in `Simulation/AiTech.cs`:

- **Citadel 2:** once a military producer stands and the clock passes
  4:00 + 4 s per point of aggressiveness (Gimli 6:20, Aragorn 8:40, Saruman
  10:00), the home Citadel upgrades as soon as the bank pays beyond the
  construction reserve. Once the bot fields a full assault party it also
  reserves the cost; earlier it does not, because food is the bots' bottleneck
  and an early reserve starved their training (Aragorn's army shrank and he lost).
- **Tier 2 buildings:** the Ranger Post is a military building and the Forge and
  Wilderness Outpost (source AI class UPGRADE) now follow the military buildings in
  the first-copy pass, so each comes once Citadel 2 stands.
- **Units:** equal Assault effectiveness (Rider and Swordsman are both 70) now goes
  to the dearer unit. While the army is short of a party, a producer trains the
  best unit it can pay for; once the army has a party, it saves for its best unit
  instead of spending the food on a weaker one.
- **Research:** an idle Forge or Outpost researches the first technique, in grid
  order, that helps a unit type the bot has alive, from money nothing else has
  reserved.
- Bug found on the way: bot orders with no units were dropped by `AiOrder`, so the
  first version never upgraded although its status said so. Building commands pass
  the building's id, as training already did.

Reference matches on Bilbo's Bluffs (observer smoke, 16x):

| Match | Winner, time | Tier 2 fielded |
|---|---|---|
| Gimli vs Aragorn | Gimli 17:46 (tick 21333) | Gimli: Citadel 2 at 7:05, 5 Riders, 2 Shieldbreakers, 12 Rangers, all four researches |
| Aragorn vs Gimli | Aragorn 14:47 (tick 17735) | Aragorn: 2 Riders, 3 Rangers |
| Saruman vs Aragorn | Saruman 24:26 (tick 29333) | both: Riders and Rangers; Aragorn: Etched Blades |

The observer report now lists every soldier each bot fielded and its research
(`armies`, `research`). The corpse check no longer counts a unit killed on the
final tick, whose death the view has not presented yet; the new reference match
ends on such a kill.

Validation: `SimulationTests` 830 checks (3 new in `AiChecks`: a rich bot upgrades
its keep no earlier than the tech time, builds the research building and
researches; with level 2 its first barracks soldier is the dearer knight). Every
Bilbo smoke passes.

## Build-48: one content pack for every map

Owner request: restructure the caches so adding a unit, building or research no
longer means refreshing 32 map caches. Every skirmish map cache used to carry its
own copy of the Good faction: unit and building models, combat rules, HUD art,
production, research and AI tables, re-exported by six per-section refresh scripts.

- **Content pack:** `tools/content_pack.py` builds `converted/content/good/`
  once (11 unit models, every building, 318 files, 18 MB) with a `content.json`
  holding the faction sections the map manifests used to hold.
- **Map caches:** `prepare_prototype.py --skirmish` now writes only the map
  (terrain, navigation, heights, resources, construction sites, start layout) and
  a relative `content` path to the pack. Spawns name their unit instead of an index
  into the model list, so the pack's roster may grow.
- **Navigation:** the map's body profiles cover all 52 collision bodies of Good
  and Evil units (`faction_bodies`), so a new unit never needs a map re-import.
- **Loading:** `CacheManifest` merges the pack under the map manifest (objects
  key by key, the map wins), resolves each file in the map folder first and then
  in the pack, and verifies both folders' hashes. A cache without `content`
  (Helm's Deep, every older map cache) loads unchanged as self-contained. The
  setup screen and the simulation tests read caches the same way.
- **Refresh scripts:** `--units-only`, `--rules-only`, `--ui-only` and the
  `skirmish_*.py --cache` refreshes stop with a message on a pack-based cache;
  they keep working for self-contained caches.

Each map cache drops its 18 MB copy of the faction. All 32 map caches are
re-imported (`python tools/prepare_prototype.py --all-maps` after building the
pack, about 2 minutes per map: clearance now runs for 52 bodies); a cache that
was not re-imported keeps loading as self-contained.

Bug found on the way: Bilbo's Bluffs had not been re-imported since map titles
came from the file stem (build-35), which reads "Bilbos Bluffs" and failed the
setup smoke's observer check; `skirmish_map.TITLES` restores the apostrophe.

Validation: `SimulationTests` 830 checks, 34 worker-traffic checks on the slim
Bilbo cache and 897 on Helm's Deep; the Python importer tests; all 16 Bilbo
smokes, the Helm's Deep smoke and the setup smoke (all maps) pass.

## Build-49: bot difficulty and style

Owner model (Oct 23): only two `.rai` knobs were ever read (Difficulty for the
worker count, Aggressiveness for party size and timings), so Default played like
Frodo, Grishnakh like Legolas and Shelob, and Gandalf like Saruman. A bot now has
three layers, of which this build adds the first two; player-picked traits follow
in build-50. All of it is adapter data in `Simulation/AiRoster.cs`, so no cache
needs refreshing.

**Difficulty is execution** (`AiSkill`), the same for every style. Hard plays fair:
no income bonus.

| | Easy | Normal (as before) | Hard |
|---|---|---|---|
| Tactical think (defence, guards, assault) | 3 s | 1 s | 0.5 s |
| Strategic think (building, workers, tech, training) | 3 s | 1 s | 1 s (every other think) |
| Base workers | 8 | 10 | 12 |
| Builders per project | 1 | 2 | 3 |
| March | straight attack-move | regroup legs (build-41) | regroup legs |
| Target choice | nearest, not spread | spread, nearest first (build-43) | spread; among enemies within 6 m of the nearest, the most wounded first, ranged before melee |
| Party falls back below | a fifth | a third | half |

**Style is the game plan** (`AiStyle`): a point in an Aggression / Expansion /
Tech triangle whose values sum to 100 (A, E, T below). It replaces every
Aggressiveness read:

| Value | Rule |
|---|---|
| Party size | 12 - (7 x A + 50) / 100 (A 0: 12, A 100: 5), as before |
| No assault before | (100 - A) x 6 s (A 90: 1:00, A 10: 9:00) |
| Worker target | difficulty base + E x 6 / 100, + 2 per Mill |
| First expansion | 15:00 - 6 s x E (E 0: 15:00, E 100: 5:00) |
| Expansion interval | 10:00 - 4 s x E |
| Most Citadels | 2 + (E + 16) / 33, at most 4 |
| Citadel upgrade from | 10:00 - 4 s x T (T 0: 10:00, T 100: 3:20) |
| Research | T 50+: every technique, its cost reserved before training; T 20-49: as build-47 (for fielded units, from unreserved money); below 20: only once the bank holds twice the cost |
| Watchtower before military | A 30 or less |
| More military buildings | A 50+: once every producer is training; otherwise once the bank pays twice over, as before |

The label names the style: "Balanced" when every value is 25 or more, else the
axes of 30 or more, heaviest first.

**The roster.** Each named bot keeps its `.rai` difficulty and gets a fixed style
and a lobby description; no two share a style. A personality missing from the
roster (a new `.rai`) plays Balanced.

| Bot | Difficulty | A / E / T | Style |
|---|---|---|---|
| Farmer Maggot | Easy | 10/70/20 | Expansion |
| Gollum | Easy | 60/30/10 | Aggression-Expansion |
| Saleme | Easy | 20/30/50 | Tech-Expansion |
| Default | Normal | 34/33/33 | Balanced |
| Frodo | Normal | 20/60/20 | Expansion |
| Gimli | Normal | 30/20/50 | Tech-Aggression |
| Grishnakh | Normal | 70/20/10 | Aggression |
| Legolas | Normal | 40/0/60 | Tech-Aggression |
| Shelob | Normal | 10/50/40 | Expansion-Tech |
| Aragorn | Hard | 50/20/30 | Aggression-Tech |
| Gandalf | Hard | 30/10/60 | Tech-Aggression |
| Saruman | Hard | 30/50/20 | Expansion-Aggression |
| Smaug | Hard | 90/0/10 | Aggression |

**Lobby.** Each bot item in a slot's dropdown now reads its English name
("Bot · Farmer Maggot (Easy)") and has a tooltip with its difficulty, style and
description. A card below the slots describes the bot last chosen or clicked (or
the first bot slot): name, difficulty and style, its three values, a small triangle
with its point (the shaded centre is Balanced) and how it plays. It fits the
existing frame. The observer overview's name tooltips add the difficulty and style,
the debug menu's AI line the style, and the bot status shows "attacks from m:ss"
until the assault gate passes.

**Changed on the way.**
- The plan let Aggression 50+ build more military buildings freely. In the first
  reference match Aragorn then stood three Barracks and three Dwarf Halls by 8:00,
  spent the food (the bots' bottleneck) on producers it could not keep busy, sat on
  1800 ore with 5-25 food and lost at 14:01. An aggressive style now adds a producer
  once every producer it has is training.
- The observer report's `armies` and `research` were keyed by bot id, so a mirror
  match (Default against Default) crashed the report; they are keyed "Player 1 Gimli".
- **Frozen armies (owner playtest).** Twice in a row Saruman's army stood still, its
  units starting to swing and cancelling at the last moment. Since build-28 a bot
  gave up on any enemy that had stood within range for 30 s, to stop chasing one
  tucked into a pocket it could never reach. In a fight lasting 30 s that also gave
  up on enemies the army was hitting: every think stopped the soldiers on them, an
  idle soldier picked the same enemy again, and the next stop cancelled its swing
  (every order cancels one). Normal bots lost swings too; a Hard bot, stopping them
  twice a second, landed almost none. Now the 30 s count restarts whenever the enemy
  loses health, so only an enemy the bot cannot hurt is given up on, and one given up
  on is forgiven once it loses health. In Saruman against Aragorn the swings Saruman's
  army landed against those it cancelled went from 14:26 to 67:41 (sampled once a
  frame at 16x), and Saruman now wins.

**Difficulty check.** Hard against Easy with the same style (Default's) can only be
judged on a real map: the synthetic two-base fixture puts the bases 52 m apart,
inside each other's 30 m defence radius, so its duels turn on whose buildings stand
farther forward. On Bilbo's Bluffs the first slot wins every mirror (Default
against Default too), so the result there is the slot's. Observer runs
with a temporary difficulty override (not committed), 16x:

| Map | Slot 1 vs slot 2 | Winner |
|---|---|---|
| Sorrow | Hard vs Easy | Hard, 15:29 |
| Sorrow | Easy vs Hard | Hard, 20:54 |
| Dust and Blood | Hard vs Easy | Hard, 10:50 |
| Dust and Blood | Easy vs Hard | Hard, 11:57 |
| Bilbo's Bluffs | Hard vs Easy | Hard, 21:02 |
| Bilbo's Bluffs | Easy vs Hard | Easy, 23:16 |
| Bilbo's Bluffs | Normal vs Normal | slot 1, 22:55 |

**Reference matches** on Bilbo's Bluffs (observer smoke, 16x):

| Match | Winner, time | Notes |
|---|---|---|
| Gimli vs Aragorn | Gimli 13:15 (tick 15903) | Gimli: Citadel 2, 6 Riders, 6 Rangers, two researches; Aragorn: no research |
| Aragorn vs Gimli | Aragorn 15:48 (tick 18973) | both research everything |
| Saruman vs Aragorn | Saruman 14:02 (tick 16843) | Saruman expands and guards it; Aragorn ends with one worker |
| Smaug vs Shelob | Shelob 21:08 (tick 25360) | Smaug's parties of six do not break Shelob, who guards her expansion and researches everything |

`-AiSmoke` timeline (Default, now Balanced, party 10): the assault waits for the
6:36 gate and leaves with 12 at once, reaches the player's Citadel at 7:48 and razes
it at 8:05 (was 4:26 and 5:48).

**Frame time.** An 8-bot free-for-all on MP08_Rift with the four Hard bots (Aragorn,
Gimli, Gandalf, Legolas, Saruman, Shelob, Smaug, Frodo; `-Observe -Speed 16 -Profile`,
stopped after 9 minutes of wall time at 16:00 of game time): 12-14 FPS and 17-18 ms
a tick once 235-280 units are on the field (build-37 recorded 13-20 FPS and
11-17 ms; no build-48 baseline was run for this match). The bots' thinking costs
1.0-2.5 ms a tick, unit movement 11-13 ms, so Hard's twice-a-second tactics are not
a hot spot; its strategy runs once a second like Normal's.

Validation: `SimulationTests` 844 checks (14 new in `AiChecks`: roster styles sum
to 100 and differ, an invalid style is refused, think rates 10/30/60 in 30 s,
builders 1/2/3, profile values move with Expansion and Tech, a Tech 50+ style
researches a technique for no fielded unit and a Tech 30 one does not, a party of
three in eight falls back on Hard but not Normal, Easy marches without legs, Hard
attacks the wounded raider and Normal the nearest, the assault gate holds until
5:00 at Aggression 50, a Hard against Easy match replays identically, a Hard army
keeps hitting harmless raiders it has fought for two minutes with no more than one
cancelled swing in ten, and an enemy given up on is forgiven once it loses health). The
existing checks now build their test bot with an explicit style and no assault gate.
`-SetupSmoke` 12 cases (2 new: the bot tooltip, and the card following the slot
last chosen); the observer smoke's name check includes the tooltip's style. The
observer reference is now Gimli wins at tick 15903 (digest 437816B8...). All 16
Bilbo smokes, the setup smoke (all maps), the Helm's Deep smoke and 183 Python
tests pass.

## Build-50: bot order discipline, idle guard and target priority

Owner requests after the build-49 playtest: a bot should let its units finish what
they are doing unless something important comes up; its idle units should guard
(they stood by while a fight went on next to them); and a party should not go on
razing a Barracks while a Watchtower and the enemy army cut it down, unless a
strategy says so. Traits move to build-51.

**Order discipline** (`SimWorld.Dispatch`, `AiOrder`). Any order cancels the
swing under way. A bot re-deciding every think cancelled swings before they
landed, and a fighting unit is nearly always mid-swing (the next swing starts as
the last ends), so the bot cannot simply skip busy units.
- **Patient orders:** a bot's order to a unit whose swing has not dealt its damage
  yet (its last hit is still to come, at most one swing) is held on the unit
  (`SimUnit.HeldOrder`) and applies the tick after the hit; then only the
  follow-through is cut, and the cooldown is kept. `SimCommand.Patient` and
  `Enqueue(..., patient: true)` carry the flag; player orders stay immediate,
  as in Warcraft III and StarCraft II, and drop any held order.
- **No repeats:** a bot skips a unit already carrying out, or holding, the same order
  (same kind and target, or the same commanded cell, `SimUnit.OrderCell`).
- **Urgent orders** apply at once: a party falling back and, for Hard, a badly
  wounded soldier pulled out of the fight.

**Hard may pull out the wounded.** A party member below a quarter of its health that
an enemy unit or a tower is hitting rolls once per party, the first time: one in
three walks home at once (urgent); the rest fight on. The first version pulled out
every one, and the owner's playtest found the wounded impossible to finish. The roll
is a stateless hash of unit, tick and team, like a unit's choice of swing, so
replays stay identical. A soldier that retreated defends the base but joins no
party or guard until it is above half its health again (owner rule). Nothing
regenerates yet, so for now it stays home; regeneration is planned (build-52 adds it).

**Stragglers rally** (owner request). Soldiers left idle in the field, outside the
party and more than 40 m from every own building, were still counted as the home
army but never gathered, and stood there for the rest of the match: stopped by the
chase leash, guards of a fallen expansion, a party with nowhere left to go. One idle
there for 10 s joins the party out (attack-moving to its current waypoint), or
walks home attack-moving to join the next one. (Enough idle soldiers for a party,
wherever they stand, already form one.)

**Idle bot units guard.** Bot sides used "aggressive idle": an idle soldier attacked
any enemy within 10 m and nothing farther, with no leash. They now use the
player's idle guard (fight what comes within 10 m of their spot, walk back after)
and also answer any enemy within 16 m of their spot that is fighting one of their
units or buildings, with a 20 m leash. Sides without a bot (the passive smokes'
enemy) keep aggressive idle.

**Target priority** (`AiProfile.TargetOrder`, default Army, Defenses, Military,
Tech, Economy, Workers; `ThreatsFirst`, default on, for a later trait to turn off).
A unit razing a building ignores whatever hits it, and the party only looked for
defenders within 12 m of its centre. Now:
- **Threats first:** every enemy within 12 m of any member, or hitting one from
  farther away, is fought before any building, soldiers before workers. Members
  razing a building switch once their swing lands.
- **Towers next:** with no such enemy left, a Watchtower shooting any member becomes
  the party's target in place of an unarmed building; members within 30 m attack it
  and the rest walk over.
- **Then by kind:** the next building is chosen by the target order, each rank down
  counting as 12 m farther, among buildings the party can reach. A Barracks 16 m
  away now goes before a Citadel 10 m away.

**Difficulty now shows on every map.** Hard against Easy with Default's style
(temporary difficulty override, not committed; observer, 16x):

| Map | Slot 1 vs slot 2 | Build-49 | Build-50 |
|---|---|---|---|
| Sorrow | Hard vs Easy | Hard, 15:29 | Hard, 13:02 |
| Sorrow | Easy vs Hard | Hard, 20:54 | Hard, 23:39 |
| Dust and Blood | Hard vs Easy | Hard, 10:50 | Hard, 10:41 |
| Dust and Blood | Easy vs Hard | Hard, 11:57 | Hard, 22:21 |
| Bilbo's Bluffs | Hard vs Easy | Hard, 21:02 | Hard, 18:14 |
| Bilbo's Bluffs | Easy vs Hard | Easy, 23:16 | **Hard, 18:06** |
| Bilbo's Bluffs | Normal vs Normal | slot 1, 22:55 | slot 1, 25:58 |

**Reference matches** on Bilbo's Bluffs (observer smoke, 16x). Small rule changes
swing these matches a lot (before the retreat roll and the straggler rally, Aragorn
beat Gimli from both slots), so they track regressions rather than prove balance:

| Match | Winner, time | Notes |
|---|---|---|
| Gimli vs Aragorn | Aragorn 13:52 (tick 16656) | was Gimli 13:15; both lose every worker |
| Aragorn vs Gimli | Gimli 19:28 (tick 23360) | was Aragorn 15:48 |
| Saruman vs Aragorn | Saruman 23:25 (tick 28103) | Saruman researches everything and fields 13 Riders, 20 Rangers |
| Smaug vs Shelob | Shelob 18:50 (tick 22600) | Smaug never researches |

`-AiSmoke`: the Default bot still leaves at 6:36 and now razes the passive player's
Citadel at 8:09 (was 8:05).

**Frame time.** The 8-bot MP08_Rift free-for-all (four Hard bots, `-Speed 16
-Profile`, 7 minutes of wall time) reaches 16:00 of game time at 13-17 FPS, 12-23 ms
a tick with 230-290 units, the bots' thinking 1-2.3 ms of it: as in build-49. An
earlier build-50 run, before the retreat roll and the straggler rally, stopped
reporting after 08:00 for its last 7 minutes; profiled every 10 s, the final code
showed no such stall, and the cause of the first was not found (another process
on the machine is possible).

Validation: `SimulationTests` 854 checks (10 new in `AiChecks`: a patient order waits
for the swing to land and an urgent one cancels it; a bot's idle soldier joins a
fight 15 m away; a party razing a building turns on an enemy hitting a member far
from its centre; a party under a tower's arrows razes the tower first; Hard retreats
2-7 of 12 wounded members and Normal none; a retreated soldier stays out of parties
below half health; a party picks a barracks over a nearer keep; stragglers join the
party out, or walk home with none out). All 16 Bilbo smokes and the setup smoke
pass; the observer reference is now Aragorn wins at tick 16656 (digest E6844779...).

## Build-51: bot traits

The third layer of the owner's bot model (build-49): traits the player picks for each
bot slot, applied after the bot's style. All of it is adapter data in
`Simulation/AiRoster.cs` (`AiTrait`, `AiRoster.Traits`, `AiProfile.For`); no cache
needs refreshing.

| Trait | Effect |
|---|---|
| Rusher | Parties of the war party's smallest size (5) with no assault gate; trains the cheapest soldier and never saves for a better one; more Barracks once every producer is busy; no research and no expansion before 15:00; 2 fewer workers; no tower first |
| Expansionist | First expansion at 4:00, then every 3:00, up to 5 Citadels; 4 more workers |
| Defender | A tower first and two by every Citadel (counted within 42 m, placed by the Citadel with fewest); expansion guards of a full party; no assault before 20:00 unless 3 towers stand and the army holds 12 soldiers |
| Relentless | Parties never fall back and the wounded never retreat; half a party idle at home reinforces the party out |
| Marksman | An army of 60% ranged units instead of 20% |
| Demolisher | Parties ignore the soldiers and towers hitting them (`ThreatsFirst` off) and raze military, then research, then economy buildings, towers last |

Rusher excludes Defender and Expansionist (it waits until 15:00 to expand).
Demolisher is the owner's "a party may raze buildings under fire, but only as a
strategy" from build-50. Raider, Scout and hero use need new behaviour and come
later.

**Picking traits.**
- Command line: a bot slot reads `Id+Trait+Trait`, for example
  `-Bots Gimli+Defender,Aragorn+Rusher+Marksman` or `-AiPersonality Gimli+Marksman`.
  `AiRoster.Parse` is the only parser; an unknown trait or an excluded pair stops the
  launch with a message naming the known traits.
- Lobby: every slot row has a Traits menu (greyed out unless a bot holds the slot)
  whose check items stay open, so several can be ticked; each has a tooltip with its
  effect and what it excludes, and ticking one drops the traits it excludes. The
  button shows Traits, the one trait, or a count. The bot card lists the traits;
  the choice is written as `Gimli+Defender` into the setup, so Restart and the
  return to setup keep it. The dropdown narrowed to 214 px to fit.
- The observer overview's name tooltip and the debug menu's AI line list the traits.

**Freeze found on the way.** In a Gimli+Defender against Aragorn match, one of
Aragorn's party members kept an explicit attack on Gimli's last worker, out of its
reach, and stood there for 18 minutes; since build-41 any member on an attack held
a regrouping march, so a party of 17 froze with it and the match never ended. Now a
member on an attack while the party has no threat to answer is called back to the
waypoint (or sent at the target on the final approach), and only a fight on the
spot (a member swinging, or one whose target is within 12 m) holds the march.
Aragorn now wins that match at 21:45.

**Trait matches** on Bilbo's Bluffs (observer, 16x): Default in slot 1, which wins the
plain mirror, against Default with one trait in slot 2. Single matches swing on small
things, so read them as "the trait changes the game", not as balance:

| Slot 2 | Winner, time | What showed |
|---|---|---|
| Default | slot 1, 22:53 | the mirror |
| Default+Rusher | **Rusher**, 23:19 | 92 soldiers fielded, 73 of them Swordsmen; parties of five, six sent |
| Default+Expansionist | slot 1, 24:11 | the Expansionist loses every worker spread over its bases |
| Default+Defender | **Defender**, 25:26 | one party sent, behind its towers, while slot 1 spends its army against them |
| Default+Relentless | slot 1, 23:14 | its parties fight to the end |
| Default+Marksman | slot 1, 24:59 | 32 Axethrowers and 6 Rangers against 22 Swordsmen |
| Default+Demolisher | **Demolisher**, 22:16 | slot 1 loses its production first |

Also: Gimli+Defender against Aragorn, Aragorn wins at 21:45 (the freeze above).

Validation: `SimulationTests` 863 checks (9 new: `Id+Trait` parsing and order; an
unknown trait, an excluded pair and a missing personality refused; each trait's
profile values and none without traits; a Rusher sends five at once and trains the
cheaper soldier; a Defender keeps two towers by its keep; a Relentless party of two
does not fall back; a Demolisher party keeps razing while hit; a regrouping party
calls back a member chasing a non-threat). `-SetupSmoke` 14 cases (2 new: traits
ticked in the lobby, Rusher dropping Defender, reach the bot and come back with the
setup); `-ObserverSmoke -Bots Gimli+Defender,Aragorn` passes, its tooltip check
covering the traits. All 16 Bilbo smokes pass; the regroup fix moves the observer
reference (Gimli against Aragorn) to Aragorn winning at tick 20180, 16:49 (digest 6E2A8FEB...).

## Build-52: health regeneration

Owner request, after the build-50 rule that a retreated soldier rejoins parties only
above half its health. The source has regeneration data, but its base constant is
in Rings.exe:

- `UnitAnimStates` gives every animation state a `HealthRegenerationRate` and a
  `HealthRegenerationRateHero`: 1.0 for idle, walking, the attack stance and the
  special (casting) states; 0 for running, gathering and carrying, mounting and
  dying; 0 mid-swing (heroes 0.167); 2.0 for repairing and extinguishing.
- `Units.HealthRegenerationMultiplier` is 1.0 for every soldier and worker and 2.25
  for the heroes.
- The place-of-power modifier `HEALTH_REGENERATION_RATE` "increases or decreases
  the HP/second health regen rate" (Ancient Fountain +1, Balrog Glyph +15 for the
  Balrog), and Frodo's One Ring disables regeneration on activation and enables it
  again on deactivation (`SPELL_ACTION_ENABLE_REGENERATION`).

So the state rate reads as HP per second, the place of power adding to it. An idle
Swordsman (155 HP) heals fully in about 2.5 minutes, Aragorn (485 HP) in about
3.5; a soldier in a fight heals nothing, and only between fights.

**Simulation** (`Simulation/Regeneration.cs`): every tick after combat a living
unit regains its state's rate times its multiplier, in whole milli-HP (fractions
dropped: a hero mid-swing gets 0.36 instead of 0.375 HP a second), never above its
maximum. States, in order: casting, mid-swing (bounded by the swing's length: a
unit keeps its last swing's start after a fight), gathering or carrying a load,
building or repairing on the spot, walking, running, in an attack stance, idle.
Nothing inside a Mill or Foundry, while the One Ring is on, or when dead.

**Data:** `combat_rules.py` exports each unit's `RegenerationPpm` and a
`Regeneration` block of the eight state rates for units and heroes (with the source
state names in `RegenerationSource`); `python tools/content_pack.py` rebuilds the
pack, and no map needs re-importing. Rules without the block (the Helm's Deep demo
cache, older packs) leave health alone.

**Bots:** a Hard bot's soldier pulled out wounded (build-50) now heals at home: from
a quarter to half its health, about 40 s for a Swordsman, after which it joins
parties again.

Not simulated: places of power. The HP-per-second reading and the state mapping are
adapters; a timed capture of an idle wounded unit in the original game would settle
the rate (Roadmap, combat validation).

Validation: `SimulationTests` 873 checks (10 new in `RegenerationChecks`: idle
soldier and hero rates, the cap at full health, the dead, running against walking,
mid-swing for a soldier and a hero, regeneration after a fight, gathering, carrying
and building, the One Ring, rules without rates, and the content pack's rates); a
new Python test for the export. All 16 Bilbo smokes and the setup smoke pass; the
observer reference (Gimli against Aragorn) is now Aragorn winning at tick 27344,
22:47 (digest 0713B2C9...).
