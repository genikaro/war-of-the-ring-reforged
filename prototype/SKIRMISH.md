# Bilbo's Bluffs skirmish - builds 4 onward

Goal: a playable Good faction Tier 1 tech tree with working workers on the
stock 2-player map captured in the first local original-game session. This file
tracks what exists; planning lives in `tools/Roadmap.md` (Phase 4 economy).

## Try it

```powershell
python tools/prepare_prototype.py --skirmish   # one-time, builds converted/skirmish
./prototype/run.ps1 -Skirmish
./prototype/run.ps1 -SkirmishSmoke             # headless checks
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
| Buildings cached | The seven Tier 1 building models (Citadel 1, Mill, Mine, Tent, Barracks, Dwarf Hall, Watchtower) for the next builds |
| HUD | Resource strip (original population/food/ore icons) replaces the Allies/Enemies label in the console |

Team 0 is the player (Start Point 0), team 1 an inert Good base. The enemy has
no behavior and no AI; nothing in build-4 changes unit rules.

## Known limits

- The strip shows the manifest's start state; it is not yet driven by simulation.
- Workers can be selected and ordered like any unit; they cannot gather, build or
  repair yet. Buildings are scene dressing plus navigation obstacles and cannot be
  selected.
- Start Citadel orientation is 0 degrees, not the original game's. Start-worker
  placement is an approximation of the original layout.
- The preview lighting is dark on this map; no lighting or fog-of-war work yet.
- Tier 1 here means buildings and units requiring only the Citadel (see the
  Roadmap plan); Shieldbreaker (Skills structure) and Rohan Rider (Citadel 2) are
  outside it.

## Validation

`./prototype/run.ps1 -SkirmishSmoke` checks cache loading, 4 workers per side,
two start buildings and their obstacles, eight resource nodes, standable and
connected starts, the resource readout and worker selection panel. Python
tests (`tools/test_skirmish_map.py`) cover the marker decoder, resource pairing
and worker placement synthetically, plus the real map when extracted.
