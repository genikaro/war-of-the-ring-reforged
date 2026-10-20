# Bilbo's Bluffs skirmish - builds 4 onward

Goal: a playable Good faction Tier 1 tech tree with working workers on the
stock 2-player map captured in the first local original-game session. This file
tracks what exists; planning lives in `tools/Roadmap.md` (Phase 4 economy).

## Try it

```powershell
python tools/prepare_prototype.py --skirmish   # one-time, builds converted/skirmish
python tools/skirmish_construction.py --cache converted/skirmish  # upgrade an existing build-4/5 cache
./prototype/run.ps1 -Skirmish
./prototype/run.ps1 -SkirmishSmoke             # headless checks
./prototype/run.ps1 -ConstructionSmoke         # construction, cancellation and both resources
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

- Mill/Foundry construction and gathering work. General placement, the remaining
  Tier 1 buildings, building selection, training, repair and combat against
  buildings remain open.
- Worker build/gather animation and equipment, exact work ranges and controlled
  original-game timing comparisons remain open.
- Start Citadel orientation is 0 degrees, not the original game's. Start-worker
  placement is an approximation of the original layout.
- The preview lighting is dark on this map; no lighting or fog-of-war work yet.
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
