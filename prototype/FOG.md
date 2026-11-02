# Fog of war, bots under fog, hero courage — build-150

Every skirmish now has fog of war, close to the original. Bots play by it: they only
know what their side sees and remembers, send scouts, search for an enemy they have
not found, and hold an attack against an army they saw is stronger. Bot heroes fight
more, by the bot's Aggression.

## What the original does (evidence)

- Local captures `local-023-scout-terrain-fog` and `local-027-retreat-fog-memory`
  (Bilbo's Bluffs, 1.01.0011): outside a side's sight the world is **not** blacked
  out. Terrain, trees and buildings sink into a dim blue-grey haze with soft edges
  (the options' "fog smoothing"). Enemy units vanish under fog, while enemy buildings
  once found stay. The minimap is grey everywhere except the side's sight, which is in
  colour. It shows the side's own buildings, the enemy buildings it has found (red
  squares in 027) and the resources.
- Source tables: `Units.SightRange` (17.5 m; Ranger, Rohan Archer and Wraith 20 m; wild
  animals 30 m), `Units.SightRangeForestMult` (0.75), `Buildings.SightRange` (20 m; the
  War Post 5 m), `Objects.RevealsFOW` + `SightRange` (Poison Trap and Satchel Charge,
  8 m), `Objects.BlocksLineOfSight` (trees, rock piles, Osgiliath ruins),
  `WorldVariables.UnitBlindSightRange` (0.5 m), `PlayerRevealTime` (20 s, Survival),
  `Effects.IsFadedByFogOfWar`, `GlobalSpells.CanCastInFOW`, spell action
  `START_REVEAL_FOW` (Dark Tracking, the Aviary).
- Unit reach never exceeds sight by much (archers 13 m, Lightbearer 17 m, Stonehurler
  18 m, Giant Ent 20 m; towers 19 m), so the source expects shooting within your own
  sight.

## Rules (`Simulation/Vision.cs`)

- Sight is a 2 m grid per side, refreshed every 5 ticks (a quarter second) at the end of
  `Step`. A cell is in sight when its centre is within the sight radius of a source:
  - a living unit outside a building: its `SightRangeMm` (source sight, scaled by
    research such as Eagle Eye and Greater Perception and by places of power);
  - a standing building or foundation: source `Buildings.SightRange` from its centre,
    at least its footprint radius plus one cell (adapter, so a Citadel sees past its
    own walls). The pack carries it as `construction.buildings.<name>.sight_mm`;
    caches without it use 20 m;
  - the side's Poison Traps and Satchel Charges: 8 m;
  - an Aviary's area while its power lasts.
- `CanSeeUnit(team, unit)` adds the fog test: an enemy outside the side's sight is
  hidden, unless an Aviary area or Dark Tracking reveals it, as before. This one rule
  serves combat (attack orders on hidden units are refused, targets lost to fog are
  dropped, towers fire only at units in sight), picking, the minimap and the bots.
  Observers (team -1) see everything.
- Each side remembers the enemy buildings it has seen (`TeamVision.Sites`: id, side,
  building, footprint, position, complete, health, tick). A remembered building is
  updated while in sight. It is forgotten when its spot is in sight and it no longer
  stands there as remembered. One razed or captured out of sight is a ghost: still
  believed to stand. `KnowsSite`, `RememberedSite`, `IsGhost`, `SeenTick(team, x, z)`
  and `InSight` expose this. The memory is part of the state digest.
- Off unless `EnableFogOfWar()` is called: simulation tests and the Sandbox see
  everything, exactly as before. Map knowledge stays public: terrain, start positions,
  resources and places of power.
- Not simulated (adapters): line-of-sight blocking (trees, cliffs, height), the forest
  multiplier, blindness, the Survival reveal, the Lookout Tower technique, spells cast
  in fog. Effects and missiles: a missile in fog is not drawn; other effects are.

## On screen (`Main.Fog.cs`, `Rendering/FogOverlay.cs`)

- One full-screen pass reads each pixel's depth, finds its ground point and looks it
  up in the side's sight. Fog mixes 55% of a dim blue-grey haze over terrain,
  buildings and trees alike. The sight texture blurs the 2 m cells over 3 × 3 and fades
  each cell in or out over a third of a second. Colour and strength are matched by
  eye to the captures (lit grass about (31,39,8), fogged about (42,50,27)): an adapter.
- Enemy units out of sight are not drawn, picked, heard or listed. An enemy building
  is hidden until found, then shown as last seen and frozen out of sight (no health
  bar, fire or new stage). A fallen one stays until its spot is seen. The console shows
  a fogged building's remembered health.
- Minimap: grey outside your sight, colour inside; your buildings and the enemy
  buildings you remember; enemy unit dots only in sight.
- Observers: V cycles through each side's sight and back to everyone's; a label at the
  top says whose sight is shown. Debug menu (F4): Reveal map shows a player everything,
  without changing what the simulation knows.
- Lobby: Game settings › Fog of war, on by default (`SkirmishSetup.FogOfWar`). Launches
  take `--fog` / `--no-fog` (`run.ps1 -Fog` / `-NoFog`); the smokes written before
  build-150 run without fog unless given `-Fog`.

## Bots under fog (`Simulation/AiScouting.cs`)

- **What they know.** Enemy buildings: only those their side knows
  (`AiKnownSites`), for targets, raids, places of power, tower ordering, worker
  safety, guard posts, placement and War Post direction. Enemy units: only those in
  sight (it was already `CanSeeUnit`; raids now check too). Where the enemy is, before a
  building is found: the nearest other start position. Enemy soldiers and heroes seen
  (`AiPlayer.Contacts`: side, tick, place, power) are kept until seen dead or unseen
  for 4 minutes.
- **Scouts.** The shipped `.rai` Scouting knob (10-100) sets the cadence (adapter; the
  original's use of it is unknown). The first scout leaves at 0:30 + (100 − Scouting) ×
  1.5 s, the next every 1:00 + (100 − Scouting) × 2.4 s (Easy: half as often again). A
  trip has 2 + Scouting / 34 stops. The scout is the fastest idle home soldier, the
  cheapest at equal speed; or, a minute after the gate with none, a worker (Normal and
  Hard, only while no enemy building is known, start positions only). It walks with a
  Move (it fights nothing) to the spot most worth a look: unfound enemy start
  positions first, then resources and places of power, the least recently seen first
  (capped at 10 minutes), less 1 s for every 2 m away. It skips spots in sight, under
  known enemy towers or unreachable, and skips a stop it has not got closer to for
  20 s. It runs home when below half health under attack.
- **Search.** A party that knows no enemy building (while an opponent is left) sets
  out anyway. It attack-moves on the same best spot, moving on once within 20 m or
  having seen it, until it finds a building. Then it attacks as the same attack.
  A target razed out of the bot's sight is still marched on until its spot is seen.
- **Holding back.** A new assault waits while the army it has seen of the side it would
  attack (the owner of the nearest known building; before one is found, the strongest
  side seen in the last 3 minutes) outweighs its own. Power is √(health × DPS) per
  unit, summed (Lanchester's square law). It goes once its power × (60 + Aggression)% ≥
  the enemy's: 5:3 for Aggression 0, about even at 40, 5:8 at 100. It also goes with 24
  soldiers, at the population cap, or when spent.
- **Journal** (observer, Scout kind): scouts sent, home or lost; enemy buildings found
  by side; searches and finds; attacks held, with both powers. The bot status line
  shows what it knows, its scout and a hold.

## Hero courage (`Simulation/AiHeroes.cs`)

The owner found bot heroes kept so safe they never levelled. Courage now follows
Aggression A (0-100), with or without fog:

| | A 0 | A 50 | A 100 |
| --- | ---: | ---: | ---: |
| runs home below (40 − 0.3 A)% health | 40% | 25% | 10% |
| back on duty from (80 − 0.35 A)% | 80% | 63% | 45% |
| hunts enemies it sees within (20 + 0.3 A) m | 20 m | 35 m | 50 m |
| takes a fight at odds no worse than (50 + 2 A)% | 2:1 for | 2:3 against | 2:5 against |

Odds are the power (as above) within 15 m of the enemy: the bot's units there, plus
the hero if it is still farther away, against the enemies it sees. Enemy heroes come
first, then soldiers before workers, the nearest first. A hero breaks off a fight whose
odds turned worse than its courage, toward the units it goes with or home, and walks
on for up to 10 s. With no assault out, a hero of A ≥ 40 goes with a place-of-power
squad or a raid instead of waiting at home.

## Verification

- `tests/simulation`: `dotnet run -c Release -- --fog-checks` (41 checks: sight
  radii, building sight from the source, seen ticks, memory, ghosts, digest, refused
  attacks, scouting gate and trip, worker scouts, Easy cadence, no scouting without
  fog, search and find, holding by Aggression, hero thresholds and courage). Also part
  of the default run.
- `./prototype/run.ps1 -FogSmoke [-CapturePath x.png -Resolution 1600x900]` on Bilbo's
  Bluffs, you against Default (18 checks): the enemy base hidden at the start (world,
  units, minimap, haze, grey minimap), a worker finds it, walks home, the units vanish
  while the Citadel stays as last seen, Reveal map, and the bot's scout finds your base
  (Default: scout at 2:18, found at 3:11). Writes `x-found.png` and `x-memory.png`.
- `./prototype/run.ps1 -ObserverSmoke -Fog` plays the observer match with fog.

Open: line-of-sight blocking and the forest multiplier, the Lookout Tower technique,
`CanCastInFOW` for global powers, effects fading in fog, a Scout trait for the lobby,
and composition choices informed by the enemy army seen.
