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
  experience and levels, regeneration, Sting's glow and the aura effect, the
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
need controlled original-game measurement. Interaction distances use the imported
footprint's farthest corner plus worker body radius and one navigation cell;
they are conservative approach envelopes rather than original work ranges.
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
construction texture keys. Progress/health labels and building animation freeze
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
- **Work spots.** The interaction envelopes stay conservative. Within one, a
  worker now picks the cell that minimises its walking distance plus twice its
  distance to the target. It therefore mines at the rocks, puts sacks down at the
  Citadel door and enters or hammers at the building rather than at the edge
  of the envelope. A shorter walk still wins when the near side is taken.

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
