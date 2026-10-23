# Godot C# skirmish sandbox

First Phase 3 milestone. Loads the local Helm's Deep GLB, Aragorn, eleven Elf
Knights, six Elven Archers and eighteen Uruk-hai in three six-unit squads per side. Select friendly units, issue orders through the command panel and
fight a simple opponent. The engine choice was approved on Oct 19, 2026.

**build-16:** construction clicks work again; buttons are no longer hidden and
re-shown between a press and its release. Workers get a Build button (B) that
swaps the unit commands for the seven Tier 1 buildings and a Back button. Esc
cancels placement, then closes the page. Cancel a foundation by selecting it.
No cache refresh is needed. See [skirmish notes](SKIRMISH.md#build-16-build-menu-and-working-construction-clicks).

**build-15:** a completed Dwarf Hall trains the Dwarf Axethrower (60 food,
10 ore, population 2), which throws original axe projectiles with their own
launch sounds and trail. Ranged presentation is now per unit; the Elven Archer
is unchanged. Refresh the skirmish cache with
`python tools/prepare_prototype.py --skirmish --units-only` and Helm's Deep with
`python tools/prepare_prototype.py --rules-only`, then restart. Both local caches
are already updated. See [skirmish notes](SKIRMISH.md#build-15-dwarf-hall-trains-the-dwarf-axethrower).

**build-14:** a completed Barracks trains the Gondor Swordsman (50 food,
population 2, 14 s), which shows the original 155 HP / Attack 10 / Armor 0.
Refresh an existing skirmish cache with
`python tools/prepare_prototype.py --skirmish --units-only`, then restart.
The local cache is already updated. See [skirmish notes](SKIRMISH.md#build-14-barracks-trains-the-gondor-swordsman).

**build-13:** workers build every Tier 1 building. Citadel, Watchtower, Camp,
Barracks and Dwarf Hall go on open ground that the map marks as buildable, near
your buildings, with a green/red ghost and Shift-queued placement. A completed
Camp adds 11 population; a new Citadel accepts deliveries and trains workers.
Upgrade an existing skirmish cache with
`python tools/skirmish_construction.py --cache converted/skirmish`, then restart.
The local cache is already updated. See [skirmish notes](SKIRMISH.md#build-13-tier-1-building-placement).

**build-12:** the Citadel trains Human Workers. Select it and click the worker
portrait; repeated clicks queue up to five. Cost and population are paid when
queued. Cancel from the selection-panel portraits or with Backspace for a full
refund. Right-click the battlefield with the Citadel selected to set a rally
point (original banner). Add the training data to an existing skirmish cache with
`python tools/skirmish_production.py --cache converted/skirmish`, then restart.
The local cache is already updated. See [skirmish notes](SKIRMISH.md#build-12-citadel-worker-training).

**build-11:** brighter ambient lighting and softer baked terrain shadows make
the dark patches readable. Runtime terrain shading lifts shadow intensity while
preserving its color ratios, alpha and fully lit values. Cast building/tree
shadows remain. The original cache stays intact; restart without rebuilding it.
Preview settings and the terrain adjustment are recorded in capture metadata.

**build-10:** drag selection includes buildings and neutral interactables when
the box contains no friendly units. Units take priority, including those retained
by Shift-drag. Selection stays separate: a structure-only box opens one structure
inspector, preferring the visible structure nearest the box center. Partial mesh
overlap works, including a roof with its ground anchor outside the box. Restart;
the build-9 cache works unchanged.

**build-9:** food wells, ore piles and the Place of Power are selectable with
their original portraits and neutral outlines. Resource inspection shows live
stock; objects without health have no HP/armor display. Structure palettes show
only structure actions, and enemy/neutral unit inspection leaves the palette
empty. HP numbers and population/food/ore counters stay vertically centered.
Refresh an older skirmish cache with
`python tools/skirmish_construction.py --cache converted/skirmish`, then restart.
The local cache is already updated.

**build-8:** workers can cross the gap between the Citadel and Foundry. Skirmish
collision uses the original footprint polygons with one body-radius clearance;
the extra grid padding and repeated inflation are removed. Click a Citadel,
Mill or Foundry to select it separately from units and inspect health, armor,
construction progress or remaining resources. Shift-click toggles it; drag
selection and control groups still select units. A selected owned foundation
can be cancelled directly. Upgrade an older skirmish cache with
`python tools/skirmish_construction.py --cache converted/skirmish`, then restart.
The local cache is already refreshed. See [skirmish notes](SKIRMISH.md#build-8-building-clearance-and-selection).

**build-7:** workers recover from movement stalls at building/resource corners.
Gathering, return trips and Move orders reconnect from the actual body position
when the cell-center graph cannot find a route. Collision checks still apply.
Restart the sandbox to load the fix; the existing asset cache works unchanged.

**build-6:** workers can build a Mill on a food well or a Foundry on an ore pile.
Select workers, choose the building icon, then click its resource site. A green/red
preview shows placement validity. Workers approach and build together; Stop pauses
their work, right-click resumes it, and Cancel refunds 75% of the cost. Completed
buildings unlock food/improved ore gathering. See [skirmish notes](SKIRMISH.md)
for source values, cache refresh and remaining Tier 1 work.

**build-5:** Bilbo's Bluffs workers gather raw ore and deliver it to their own
Citadel. The resource strip reads the live simulation bank. Right-click ore to
gather, Shift-right-click to queue; ordinary orders interrupt and preserve cargo.
Build-6 adds Mill construction for food. See [skirmish notes](SKIRMISH.md) for
cache refresh, source values and adapter limits.

**build-3:** Kingsfoil now has its original healing sprite layers and a targeting
cursor, with invalid feedback for enemies/full-health units. Passive labels are
removed from skill icons. Active skill hotkeys sit at the bottom-right, separate
from the cooldown countdown. Effects follow the target and freeze when paused.

**build-2: Aragorn abilities.** Select Aragorn or a group containing him, press
**Q** (or the Kingsfoil icon), then click an injured ally or Aragorn. Greater
Kingsfoil heals up to **250 HP** within **10 m**, with a **17-second cooldown**.
He approaches distant targets and uses the source heal animation/sound. The
trial equips Andúril's Fury and Immunity as passives; W/E do not cast them and
R remains unused. Fury raises his displayed Attack to 30. Status-inflicting
weapons, poison cleansing and other original spell effects remain future work.
See [abilities, controls and source trace](ABILITIES.md).

**build-1:** the top-right corner shows the build number, current local date and
live FPS. Each implemented change set increments `BuildInfo.Number` once.
Double-click or Ctrl-click a unit (or portrait) to select every living unit of
that type on its team across the map. Shift-click adds/removes a unit;
Shift with type selection adds that type, or removes it when fully selected.
Shift-drag adds friendly units. Menu and Walk/Run use their native artwork sizes
inside the console's matching slots.

Squads now traverse layered walkable geometry with terrain entrances and
congestion replanning. Static building/object footprints now block movement.
Walkway edges now require support for each type's full collision radius.
Placed obstacles can be removed and restored while units replan their routes.
Painted walker bits match the original walker region graphs on all 54 maps.
Narrow walkways keep off-center nodes, and squads clear longer gridlocks.
New/moving footprints and the corner-diagonal policy remain open.
Open-ground orders now follow full-route any-angle shortcuts, react to new
orders from their current position and steer around nearby units. Squads match
units to formation slots by travel cost. The six-unit benchmark finishes in
10.55 seconds versus 13.70 seconds before, with no waiting samples. See the
[movement notes](MOVEMENT.md) for the fixture, measurements and remaining limits.
The remaining Phase 3 work and acceptance criteria are in
[the local roadmap](../tools/Roadmap.md).

The accepted movement baseline now includes per-type body sizes and a correction
for large formations getting stuck behind early arrivals. The original console artwork, scalable HUD, control groups,
queued waypoints and explicit orders are implemented. The bottom action row is
Attack, Hold, Guard, Patrol. Attack/A resolves by target: enemy pursuit,
neutral/friendly force attack, or ground attack-move. Move and Stop work without
visible buttons. These four types now use imported health, armor, weapon DPS and
animation-timed hit events, with weapon visibility and impact sounds. See
[combat rules and approximations](COMBAT.md) and [command behavior](COMMANDS.md).

Weapon ratings now follow cutting > piercing > blunt > cutting: 125% in the
favorable direction, 75% in reverse, and 100% for Hero attacks. Equal types
and Hero targets are neutral. Single selection shows the original type icon,
Attack, Armor, Victories and live health; hover the icon for its matchups.
Group selection retains the portrait roster. Enemy kills update Victories.

**Squad 3 is six Elven Archers.** Press **3** to select them. They use the
original model, portrait and shooting animation, with 180 HP, Attack 14, Armor 0
and Piercing damage. Their source bow range is 1–13 m. They approach until they
can fire, release visible original arrows on the animation's source event, and
apply damage on arrival. In melee combat they switch to their original melee
weapon and attack animations, returning to the bow after contact ends.
Their animation pivot is centered for hit-and-run turns, and new attack
windups wait until they face the target. Turning uses fixed simulation ticks;
the original per-turn angle field is recorded, while turn speed remains a
[documented prototype value](COMBAT.md#ranged-turning-and-animation-pivot).
Arrow releases play the original Elven launch sound and leave a narrow gray
trail that fades after impact. Refresh older sound/effect caches with
`python tools/prepare_prototype.py --rules-only`.
Attack/A, attack-move, Hold, Guard, Patrol, queued
orders and Walk/Run use the existing controls. Arrow speed and target following
are prototype policies; [ranged details and limits](COMBAT.md#elven-archer).

Click an enemy to inspect the same stats with a red ring and health bar.
Unit commands disable while inspecting it; the global Walk/Run switch remains
available. Drag selection remains friendly-only.
A unit or building below full health also carries the original's world health
bar, a black strip with a green fill at its foot. A foundation's health climbs
with the work done, so the same bar is its construction progress bar.
Selection rings show the full body envelope: Aragorn **0.800 m**, Elven Knight
**0.927 m**, Uruk-hai **0.693 m** radius. Aragorn's demo model is enlarged 12%,
with a wider hitbox; raw source bounds remain recorded. Friendly personal space
can compress to half the combined envelope so units can squeeze between allies.
Solid cores still prevent stacking; enemies and terrain retain full clearance.
Resting formations keep full spacing. Large formations retain continuous movement and exchange
blocked arrival slots. See [collision details](../tools/NOTES-unit-collision.md).
Refresh an existing cache with `python tools/prepare_prototype.py --rules-only`;
this also rebuilds body-specific navigation profiles for rules format 3.

Attack-move reacts immediately to reachable adjacent enemies, including while
regrouping and after arrival. Rear units may flank a nearby battle involving
allies, while pursuit of fleeing enemies remains bounded. Combat steering keeps
a passing side, and approach routes obey the same limits as movement. See the
[crowd follow-up](ENGAGEMENT.md#crowd-and-attack-move-follow-up) and run
`./prototype/run.ps1 -AttackMoveEngagementSmoke` for the full-army input check.

Idle player units automatically watch a 10 m radius and help nearby allies fight.
They pursue within a 12 m limit and return near their resting position afterward.
Completing Move starts guarding the new position. Stop cancels the current action
and resumes default guarding on the next tick; Hold Position stays stationary.

**F2** or the original run icon beside the minimap switches your entire army
between walking and running, including units spawned afterward. It works with
an empty selection or while inspecting enemies. Each player's setting is
independent. Orders, queued waypoints and attack timing continue.
Running retains the accepted 3 m/s speed; walking uses the source animation
ratio for each unit. See [movement details](MOVEMENT.md#walkrun-control--october-20-2026).

Combat reserves separate reachable attack positions around each target, keeps
allied spacing, and parks excess attackers outside the melee ring. Settled
fighters hold position and face their target. Attack orders continue onto nearby
enemies when their target dies. These are prototype rules, not recovered original
combat formulas.

The map starts with all six squads in view. The Menu has Squad, Fortress and Map
camera buttons; their F1/F2/F3 shortcuts are removed. Runtime textures have mipmaps and
anisotropic filtering, painted lake colours/opacity render correctly, and the
spawned teams use the original blue/red colours. Selected units have hollow
gold rings for friendlies and red rings for inspected enemies, sized to the
collision body. The [reference review](../references/original-game/PROTOTYPE-REVIEW.md)
records before/after captures, fixes and remaining fidelity gaps. Lighting and
selection art remain preview approximations.

## Run

From the repository root, with the current local cache and portable tools:

```powershell
./prototype/run.ps1
```

The launcher builds C# first. It uses portable tools in ignored
`converted/tooling`, or accepts `-GodotPath` and `-DotnetPath` explicitly.
Pinned development versions: Godot **4.7.2 .NET**, .NET SDK **10.0.402**.
The Windows Godot executable's Authenticode signature was verified; the SDK
ZIP matched Microsoft's published SHA-512. No global PATH changes are needed.

Rebuild the cache from the already extracted local install:

```powershell
python tools/prepare_prototype.py
```

To add the archer to an existing cache, refresh models, portraits, rules,
body navigation and demo squads together:

```powershell
python tools/prepare_prototype.py --units-only
```

To update just the squads and camera in an existing cache:

```powershell
python tools/prepare_prototype.py --demo-only
```

To refresh imported combat rules, equipment bindings and impact sounds in an
existing cache, run `python tools/prepare_prototype.py --rules-only`.
Existing caches also need `python tools/prepare_prototype.py --ui-only` for the
new type icons. Rules format 3 requires refreshing older combat/clearance caches.

For another owner's install, provide its folder (containing `Database/`
and `Archives/`). Extraction verifies CRCs and paths; the install is read only:

```powershell
python tools/prepare_prototype.py --install "C:/Games/War of the Ring"
```

Bilbo's Bluffs skirmish (build-5; see [SKIRMISH.md](SKIRMISH.md)) uses its own
cache, so the Helm's Deep sandbox above stays untouched:

```powershell
python tools/prepare_prototype.py --skirmish
./prototype/run.ps1 -Skirmish
```

`-Skirmish` on its own opens the skirmish setup screen (build-39; `-Setup` always
does). It is a lobby in the manner of Warcraft III and StarCraft II: every
multiplayer map with its player count and a preview of its start positions, and one
row per start position set to You, a bot personality (with its difficulty) or
Closed. With no slot set to You, you watch. It also sets the start speed. Restart on
the result panel replays the same setup; its Setup button, or the debug menu's
Skirmish setup button, goes back to the screen. Any flag that chooses the match
(`-Map`, `-Observe`, `-Bots`, `-Speed`, `-NoAi`, `-AiPersonality`, the smokes) skips the screen.

The skirmish plays against the enemy bot (build-27). Bots expand with a new Citadel
between 10 and 15 minutes, go all in when their army can no longer grow, and
surrender when they can never field one again (build-38). Each expansion keeps half an
assault party as its guard, home armies stop chasing enemies that have left, and idle
soldiers reinforce a party already out (build-40). Parties muster on their route and
regroup on open ground every 25 m on the way (build-41). `-AiPersonality Gimli` picks a
shipped `.rai` personality and `-NoAi` keeps the enemy passive. To watch two bots
fight instead (build-28), observe: nobody plays and any unit can be inspected but not
commanded. The spectator HUD (build-36) follows other RTS observer modes: a foldable
overview at the top right lists every player under an Economy tab (food, ore,
income per minute, population, workers, army, buildings) or a Units tab (a portrait
per unit type with its count), and the console's action grid, which has nothing to
command, shows the map, the game clock and the speed controls.

```powershell
./prototype/run.ps1 -Observe -Bots Gimli,Aragorn -Speed 4
```

Any of the 32 multiplayer maps imports into its own cache (build-35), with a Good
Citadel and four workers on every start point and one colour per player slot. A
bot runs every side but yours; `-Bots` names their personalities by slot (your own
slot's entry is ignored while you play), and observing a big map is a free-for-all:

```powershell
python tools/prepare_prototype.py --skirmish --map MP04_Wasteland
./prototype/run.ps1 -Map MP04_Wasteland -Observe -Bots Gimli,Aragorn,Legolas,Saruman -Speed 8
python tools/prepare_prototype.py --all-maps
```

Game speed works in every mode: `-` and `+` step through 1x, 2x, 4x, 8x and 16x,
and Space pauses. While observing, the match panel's pause button and notched
slider (click, drag or scroll it) do the same. When the simulation cannot keep up
with the chosen speed (big fights on large maps), the game runs slower instead of
the frame rate, and the match panel shows the speed actually reached.

`-Profile` (any mode) prints, every two game minutes, the unit count and where
simulation and frame time went, for chasing slow matches.

All generated game data stays in ignored `converted/prototype/`, `converted/skirmish/` and `converted/skirmish-maps/`. The source
project contains no game assets. At launch the manifest's content hashes are
checked before loading. Never distribute the local cache with the project.

## Controls

| Input | Action |
|---|---|
| Left-click | Select a unit, skirmish building or neutral interactable; inspect enemies |
| Left-drag | Select friendly units first; otherwise inspect a structure in the box |
| Shift + selection | Add to selection |
| Shift + structure click | Select/toggle that structure separately from units |
| Structure portrait | Center the camera on the selected structure |
| Hero portrait (right edge) | Select the hero; Shift-click adds or removes him; double-click also centers the camera |
| Q with Frodo selected | Put the One Ring on or take it off |
| Workers selected: Build [B], then a building | Place a Tier 1 or Tier 2 building (Shift queues); Mill/Foundry choose a resource site; Back or Esc returns; a locked building names what it needs |
| Workers selected: right-click a foundation | Resume or assist its construction |
| Workers selected: right-click a damaged own building | Repair it with the source hammer clip |
| Armed units selected: right-click an enemy building | Attack it until it falls |
| Attack [A], then click any building | Force-attack it, including your own |
| Right-click the minimap | Order the selection there; Ctrl marches, Shift queues |
| Citadel selected: worker portrait | Train a Human Worker; click again to queue (five maximum) |
| Citadel selected: training portrait / Backspace | Cancel that unit / the last queued unit, with a full refund |
| Citadel selected: right-click terrain | Set the rally point for trained units |
| Citadel or Camp selected: upgrade [Z] | Upgrade in place to Citadel 2 / War Camp; the upgrade heads the queue and its portrait (or Backspace once the queue is empty) cancels it for a 75% refund |
| Forge or Wilderness Outpost selected: Q / W | Research the blades / armor upgrade; its portrait in the queue (or Backspace) cancels it for a full refund |
| 1 / 2 / 3 | Select the corresponding blue squad |
| Ctrl + 1–9 / 0 | Assign the selection to a control group (1–3 start with the demo squads) |
| Shift + 1–9 / 0 | Add the selection to that stored group |
| 1–9 / 0, double-tap | Recall a group; double-tap centers its lead unit |
| Right-click terrain | Move the selection in a small formation |
| Ctrl + right-click | Attack-move to the clicked location |
| Ctrl + Alt + right-click | Order movement on terrain beneath bridges and walkways |
| Right-click an enemy | Attack, approaching when outside melee range |
| A, then left-click | Attack an enemy, force attack a neutral/friendly target, or attack-move to ground |
| M, then left-click | Move (no visible button) |
| S | Cancel the current action and queued orders, then resume default idle guarding (no visible button) |
| H | Hold Position; engage within reach without chasing |
| F2 | Toggle Walk / Run for selected units without replacing orders |
| G / T, then left-click | Guard / Patrol |
| Shift + click / right-click | Queue orders and formation waypoints |
| Alt + right-click a placed obstacle | Remove it; click the same space again to restore it |
| Arrow keys / screen edges / middle-drag | Pan the camera; rest the pointer on any window edge or corner |
| Left-click or drag on the minimap | Move the camera there; the white outline shows the visible ground |
| Menu: Rotate left / Rotate right | Rotate the camera |
| Mouse wheel | Zoom |
| Space | Pause/resume simulation and unit animation |
| - / + | Game speed down / up (1x, 2x, 4x, 8x, 16x) |
| Observing: E / U | Open the overview's Economy / Units tab; the open tab's key folds the overview |
| Observing: Tab or the overview's chevron | Fold or unfold the overview |
| Observing: a player's name in the overview | Center the camera on that player's base |
| Observing: speed slider / pause button | Choose the game speed / pause and resume |
| Esc | Cancel targeting; then close the build page; otherwise clear selection |
| Right-click while targeting | Cancel targeting and retain the current order |
| Restart sandbox | Reset the encounter |
| Menu: All squads / Fortress / Whole map / Focus selection | Frame a comparison view or the current selection |
| F4 / console Menu button | Open/close debug tools |
| F10 | Show/hide the debug UI |

Control policy (user decision): **Q/W/E/R for abilities 1–4** and **F2 for
walk/run**, retaining S Stop. These are intentional differences from the
original game's bindings. Q/E rotation and F1/F2/F3 camera shortcuts have been
removed; camera buttons and launch-time `-View` captures remain available. The
Aragorn's Q heal, W/E passives and global Walk/Run are implemented. See
[command plans](COMMANDS.md#planned-corrections-from-the-local-original-game-session).

The debug menu has live pause/tick and selected-order information, Pause/Resume,
Step one tick (50 ms while paused), Restart encounter, camera view/rotation
buttons, Focus selection and the saved 75–150% interface size. In a skirmish its
Cheats section adds 1000 food and ore or makes your building and training
instant. The current shortcut guide is collapsible; the last action is labeled
separately from live state. The menu scrolls to fit above the console. Opening
it does not pause play.
Use `./prototype/run.ps1 -DebugMenu` to start with it open, including when saving
a normal viewport capture with `-CapturePath`.

## Verification

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --cache converted/prototype
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --worker-cache converted/skirmish
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --crossing-probe converted/skirmish-maps/MP04_Wasteland
./prototype/run.ps1 -Smoke
./prototype/run.ps1 -MotionSmoke
./prototype/run.ps1 -CombatSmoke
./prototype/run.ps1 -MeleeSmoke
./prototype/run.ps1 -CommandSmoke
./prototype/run.ps1 -OrdersSmoke
./prototype/run.ps1 -SkirmishSmoke
./prototype/run.ps1 -ConstructionSmoke
./prototype/run.ps1 -StructureSmoke
./prototype/run.ps1 -ProductionSmoke
./prototype/run.ps1 -PlacementSmoke
./prototype/run.ps1 -DwarfHallSmoke
./prototype/run.ps1 -MinimapSmoke
./prototype/run.ps1 -FrodoSmoke
./prototype/run.ps1 -TechSmoke
./prototype/run.ps1 -Tier2Smoke
./prototype/run.ps1 -ResearchSmoke
./prototype/run.ps1 -SiegeSmoke
./prototype/run.ps1 -DefenseSmoke
./prototype/run.ps1 -VictorySmoke
./prototype/run.ps1 -AiSmoke
./prototype/run.ps1 -ObserverSmoke
./prototype/run.ps1 -SetupSmoke
./prototype/run.ps1 -SurfaceSmoke
./prototype/run.ps1 -ObstacleSmoke
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --movement-benchmark converted/prototype/movement-after.json
```

The simulation runner has no Godot or test-package dependency. Build-8 passes
633 synthetic checks and 700 checks with the Helm's Deep cache. The separate
worker-cache suite passes 30 checks on Bilbo's Bluffs. These cover
blocked routes, unreachable areas, adjacent-cell paths, deterministic ties,
terrain interpolation, repeatable command playback, movement, combat,
ownership and non-overlapping units, stationary-blocker detours, a six-unit
narrow gate, waiting-route replacement and resuming after a corridor clears.
They also cover opposing units using a siding, repeatable yielding, opposing
three-unit groups in open space and replacing an order during yielding.
Layered checks cover ramp heights, explicit bridge entrances, separated floors,
surface picking, formation spacing and slot exchanges after an early arrival.
Obstacle checks cover overlapping footprints, separate floors, blocked surface
edges, route invalidation and recovery, deterministic obstacle commands, and
safe restoration around standing units and units in transit. Replacement orders
retain the occupied physical segment on constrained routes; open-ground orders
turn or stop at their current position.
Input geometry checks cover stacked decks, ground below overpasses, slopes,
map boundaries, ray range and shared triangle edges far from the origin.
Formation regressions cover disconnected patches near the destination,
connectivity changes after obstacle updates, and stopping safely after an
unreachable or invalid replacement order.
Directional checks cover weighted diagonal routes, blocked corners and flank
edges, equal movement speed, mid-diagonal cancellation, crossing diagonals and
repeatable fractional progress.
Smoothing checks cover full-body ground shortcuts, blocked-corner detours,
constant speed, cancellation, construction and spawn reservations, and periodic
combat target tracking. Presentation checks follow recorded movement around
corners and verify that rendering does not change simulation state.
Traffic checks cover two opposing three-unit squads using wide bays and
single-entry pockets, reversed command order, cancellation during yielding,
recorded command playback, movement endpoint reservations and bottleneck cache
invalidation. Gridlock checks swap opposing three- and six-unit squads through
a 12-cell single-file corridor, add a third squad behind the first, cross four
squads at a single-file junction, clear four idle units out of a corridor and
run 60 seeded random order sequences for 6 to 10 units on both maps. They check
constrained endpoints and physical overlap every tick. Responsiveness checks
cover twelve distinct shallow headings, next-tick redirects, repeated squad
redirects, full-body ground clearance, interpolation-time separation and
deterministic playback. Off-center walkway nodes
are covered as well. Combat regressions cover six attackers holding distinct
positions, fourteen attackers with a stable waiting ring, moving targets,
obstacles invalidating attack positions, walls and separate floors, target death,
move-order cancellation, separation between ticks and deterministic playback.
Order checks cover cancellation during combat, force-move immunity to retaliation,
Hold Ground versus pursuit, Attack-move resumption after successive targets,
replacement orders, dead/foreign selections, invalid attacks, stopping on decks,
targeting cancellation, equivalent contextual/panel commands, serialized replay
and sequence ordering after playback.
Extended checks cover ten editable groups, additive assignments, death pruning,
queued formation routes, patrol resumption, guard following/leashes and ward
death, friendly force attacks, and proportional HUD layouts through 4K and
ultrawide sizes.
Melee checks cover timed windup/hits/recovery, cancellation, retargeting,
multi-hit damage, armor/pair policies, fractional health and attacker limits.
The optional cache check adds 56 checks, including four repeatable source-unit
duels and imported definitions. Navigation checks retain runtime
layers reproduce the static ground snapshot, all placed building centers are
blocked, and all six friendly units visit each of Helm's Deep's four walkable
surfaces without overlap and reach every order. Opposing six-unit squads also
swap between the clearing and each of the four walkways.
The full demo's 36 spawns are checked for overlap and initial separation from
opponents, so the squads do not start fighting before player input.
The headless Godot smoke test loads
the actual map and 36 animated units, dispatches mouse/key events through the
viewport, and checks selection, move/attack picking, zoom, pause/resume,
animation and damage over 420 fixed ticks. Its report is
`converted/prototype/smoke-report.json`.
It also checks map-view keys, selected-ring visibility, mipmaps, vertex-colour
bindings and team-mask adaptation on the imported scene.
The motion smoke test renders at 80 FPS while simulation stays at 20 Hz. It
requires interpolated positions and gradual turns as well as the normal input
and combat checks, writing `converted/prototype/motion-smoke-report.json`.
The combat smoke test selects squads with number keys, sends all three friendly
squads into combat through actual mouse input, and requires all three to fight
without physical overlaps. Its report is `converted/prototype/combat-smoke-report.json`.
The melee smoke test adds source stats, exact hit timing, animation seeks,
equipment changes and impact playback. It writes `melee-smoke-report.json` and
`melee-hit-trace.json`; source-unit duel evidence is in `melee-duels-report.json`.
The command smoke test exercises actual panel buttons and hotkeys across all
three blue squads. Its 34 checks cover enemy inspection and command protection,
the four bottom-row actions, target-dependent
Attack/A, hidden Move/Stop controls, targeted and immediate orders, disabled
commands, cancellation, mixed unit types, combat interruption, pause/resume,
drag release over the panel and removal of dead selected units. Its report is
`converted/prototype/command-smoke-report.json`.
The orders smoke test adds 27 actual input checks for control groups, centering,
new commands, queues, HUD adjustment and debug pause/step/open/close controls,
writing `converted/prototype/orders-smoke-report.json`. It also checks F2 and the
run-icon button, original walk/run clip selection, unchanged camera/targeting,
paused toggles and empty selection.
Unit scenes share imported resources while each instance keeps its own skeleton
and animation player.
The minimap smoke test (build-18, skirmish cache) checks the original ore, well and
place-of-power markers against the source objects, the view outline, minimap
press-and-drag, release, edge and corner scrolling, and that scrolling stops when
the pointer leaves the window, and (build-42) that every start Citadel is drawn at
its footprint's size in its team's colour. It writes `converted/skirmish/minimap-smoke-report.json`.
The surface smoke test places six units on an elevated deck, dispatches a
right-click order, then checks Ctrl + Alt + right-click terrain picking with the same
screen position. The obstacle smoke test removes and restores an obstacle
through actual Alt + right-click input and verifies its visibility each time.

For a rendered capture (opens the engine, saves a frame, then exits):

```powershell
./prototype/run.ps1 -CapturePath converted/prototype/preview.png
./prototype/run.ps1 -View fortress -HideHud -CapturePath converted/prototype/reference-fortress-after.png
./prototype/run.ps1 -Smoke -CapturePath converted/prototype/reference-selected-after.png
./prototype/run.ps1 -CommandSmoke -CapturePath converted/prototype/command-panel.png
./prototype/run.ps1 -OrdersSmoke -Resolution 3840x2160 -CapturePath converted/prototype/hud-4k.png
```

Each capture writes a companion JSON with its camera, viewport, map hash,
preview lighting and import checks. A smoke check with `-CapturePath` uses the
real renderer so that its selected-unit frame can be saved.
With `-Resolution` and `-CapturePath`, an offscreen target produces the requested
pixel dimensions even when the desktop constrains window size. The HUD scales
with height, retains source proportions and stays centered on ultrawide screens.
Original bitmap art is enlarged with filtering; the font rendering stays sharp.

## Boundaries of this milestone

The simulation runs at 20 Hz, with integer millimeter positions, integer HP,
tick-based cooldowns and stable unit/cell ordering. A* uses eight directions,
with integer 1000/1414 edge costs and an octile heuristic. Ground diagonals
require both neighboring flank cells and their connecting edges to be clear.
Walkway diagonal links pass the full swept-body coverage check. Motion follows
the line between endpoints at 3 m/s using integer micrometer progress, so
diagonals have the same speed as straight moves. Constrained crossing segments
reserve their swept body corridor, with integer segment-distance checks and separate
height ranges. Units can pass beside a stationary flank unit when their bodies
have enough space, while blocked terrain corners remain closed. Endpoint
reservations on constrained routes preserve retreat after an order change;
nearby units block overlapping movement. Godot camera, animation and rendering
do not feed timing or floating-point values back into simulation stepping.
UI hit positions are quantized into cell commands before the next fixed tick.

Orders entirely on wide ground use full-route line-of-sight smoothing, including
long straight and shallow headings. Movement holds only the current tick's body
sweep rather than the whole distant route. Relative-angle local steering handles
nearby blockers, while bounded avoidance falls back to coordinated yielding.
Formation matching minimizes total squared travel, reducing crossed paths.
New orders start at the current physical position; cancellation stops there.
Routes containing single-file cells or elevated decks retain the
constrained solver and its endpoint/corridor reservations. Their ground shortcuts
still span at most six graph edges. Combat plans individual attack positions,
uses free-ground following where its approach supports it, and replans after
six ticks when a target moves, an approach blocks or a waiting slot opens.
Rendering interpolates each tick's recorded movement along its actual corners
and rotates units gradually. It follows terrain height between samples, holds
on pause, and uses no floating-point feedback into the simulation. Native
physics interpolation is disabled for these manually interpolated unit roots.

Movement interprets the saved painted walker bits as allowed cells. On all 54
maps the original walker region graph covers exactly these bits; the two other
bitsets are editor inputs (`tools/NOTES-world-scenes.md`). Cache generation
fails if they disagree. Ground diagonals still need both flank cells, while the
original region graph also links corner-only diagonals: 66 painted Helm's Deep
cells are outside the prototype's main area only for that reason.
Static footprints use the source's oriented-collision layer for Buildings and
objects with `IsPathingObstacle`: 108 Helm's Deep placements block 1,741 painted
ground cells. The prototype interprets the records as half-extents, center and
quaternion, transforms their corners, and applies a conservative swept-cell
envelope. The base graph uses a 350 mm radius and 1.8 m body; per-type profiles
rebuild each object's masks with the imported radius/height. Vertical bounds preserve separate
floors. Original executable collision formulas remain unverified; rotated
boxes may be over-conservative. Unsupported or missing collision boxes fail
cache generation. The obstacle-free graph and independent per-object node/edge
masks allow removal and restoration at fixed simulation ticks. Overlapping
objects retain their own blocks. Adding an obstacle is rejected if its mask
covers a live unit's full body or a constrained segment the unit is crossing.
Construction ahead of an open-ground route invalidates that route and replans.
Constrained replacement orders bring moving units back to their last cell center;
sealed orders
retain their destination and resume after reopening. This sandbox control
toggles existing placed objects. The separate skirmish implements resource-building
construction and selection; damage, repair and destruction remain open.
New or moving footprints still require an adapter.
The raw painted mask and initial blocked snapshot remain available for auditing.
Walkable helpers append separate nodes to the ground grid; the
current cache has 1,342 surface nodes and 48 height-matched terrain entrances.
Links require continuous triangle coverage with at most 150 mm deviation from
the interpolated height, no more than one cell of rise, and terrain joins within
250 mm. Source model scale and placement transforms match the rendered scene.
The original MPL route codes are unused, including the two cyclic bridge tables.
Mesh nodes and edges require continuous support for a swept 350 mm square
around the unit center, conservatively enclosing its circular body. Triangle
subtraction checks the whole footprint; overlapping triangles cannot conceal
gaps. Support must match the local slope and interpolated height within 150 mm,
so lower floors cannot fill holes in a deck. Where a cell center cannot hold
the body, a node takes the nearest supported point within 400 mm (135 on Helm's
Deep). Off-center nodes keep 700 mm from other nodes and from links between
them in the base graph. Per-type clearance profiles reject unsupported larger
bodies, insufficient headroom and narrow links, then body reservations protect
moving/parked units. This conservatively subsets the base graph rather than
resampling it for every body. Mouse orders intersect source terrain and walkable mesh
triangles. Ctrl + Alt + right-click bypasses decks to target terrain beneath them.
Grid traversal limits terrain queries to cells crossed by the ray; precise
triangle calculations avoid cracks along shared edges. The input adapter uses
floating-point geometry and quantizes its result into an integer node command;
this math does not run inside simulation stepping. Mesh-edge clicks snap to a
nearby node on the clicked source surface. These are navigation queries;
rendered props and water do not occlude movement picking.
Units retain their surface node and integer elevation; collision and melee use
vertical separation. On constrained routes, units wait before entering occupied waypoints
and reserve both movement endpoints and their body corridor until reaching the
next cell center, preserving a safe retreat point if an order changes.
Detours and refuge searches
respect these reservations. Congested units retry routes around occupied cells
after ten blocked ticks (half a second); free-ground retries use three ticks.
Temporarily sealed routes retain their destinations and retry; statically
unreachable orders end without a route. This deterministic grid policy passes
the six-unit gate test. Opposing friendly move orders can request a reachable
side pocket: one unit holds there until the other finishes, then resumes its
original order. Refuge choices must leave a route around the other's final
destination. Stable unit/cell ordering makes this repeatable; changing the
priority order releases its yielding partner. Combat units do not request
yielding. Idle friendly blockers can also yield and return; if a convoy parks in
their old spot, they settle in nearby free space. Explicit move goals remain
intact. Early arrivals in
the same squad order exchange formation slots when they block unfinished slots.
Formation slots stay in the destination's connected region, so nearby islands
cannot consume a slot and strand part of a squad. Region queries are cached and
invalidated when obstacle masks change. Unreachable or invalid replacement
orders stop a free-ground unit immediately and return a constrained unit to its
reserved center. One formation slot honors the clicked destination; wide-ground
orders assign its unit by travel cost, while constrained orders keep ID ordering.
Extra slots prefer nearby cells
that do not cut the navigation graph, within one additional cell of the closest
available slot; otherwise they use the closest slot. Obstacle updates invalidate
the cached cut-point classifications.
Refuges avoid other units' reserved destinations and fill deeper off-route space
first, searching up to eight graph edges beyond the pocket entrance. Assigned
refuges remain stable during travel. A settled yielder can relocate if it blocks
its priority unit's changed route, and dependent holds transfer when their own
priority unit must yield. A refuge search can pass standing friendly units
that could also give way; the one nearest the refuge moves first, so a squad
backs out of a corridor one unit at a time. Units also yield on head-on routes
at junctions where their goals point the same way. Yielding units can ask for
room on their own way to a refuge, and an inherited or since occupied refuge is
replaced. A held refuge is released once the priority unit's remaining route no
longer crosses the way back. Idle units settle where they stand when blocked,
or in the refuge when they cannot return without crossing a destination.
Corridors without passing space wait safely. Dense random orders that park
units inside single-file passages others must cross can still wait. Continuous
movement through mixed wide/narrow routes and free-angle elevated motion remain
open. Source-sized 18-unit moves and stationary-target melee have additional
arrival, settled-waiting and interpolated-separation checks.

The three demo types use imported stats and animation-timed melee. Armor math,
event-scale normalization and effective range remain documented approximations;
see [combat notes](COMBAT.md). Running remains 3 m/s; walking uses the imported
walk/run animation ratio. Original world speeds and stamina remain unverified.
Enemies acquire nearby reachable targets; attacked units retaliate. Attack orders
retain nearby combat intent after a target dies; move orders clear it. Melee
requires a supported route between attacker and target, so walls and disconnected
floors cannot become attack shortcuts. Imported units use the sum of both radii
for physical contact, approach slots and permission to strike. Overflow attackers
can choose other reachable enemies; nearby allies share active threats. Stalled
steering triggers a new approach rather than circling indefinitely. Bounded goal-set
searches, staggered planning and loading-time region warmup reduce combat spikes.
See [engagement checks and measurements](ENGAGEMENT.md).
Attack-move uses a bounded encounter corridor and regroups after flank pursuits.
Large marching bodies retain a passing side briefly and exchange arrival slots;
visual facing filters small corrections and lateral changes. See the
[crowd movement regression and real-map smoke](MOVEMENT.md#crowded-arrival-and-attack-move-cohesion--october-20-2026).
The collision cylinders and surface-gap reach are adapter interpretations.
This is not the
original combat/economy or skirmish AI. Unit states choose original idle/run/
ready/attack/death clips; supported equipment visibility and impact sounds
now have adapters. Complete texture/effect events, voices, particles, water animation, cloth simulation,
in-game install selection and production importer packaging remain open.
The command log and integer core make repeatability testable; cross-platform
lockstep/networking is not claimed to be implemented.

Cached run/walk clips are adapted to play in place: `J_Position` horizontal
travel is subtracted from every independent skeleton root, preserving pelvis
bobbing and child motion. The original Phase 1 model exports are untouched.

Runtime import uses Godot's [GLTFDocument API](https://docs.godotengine.org/en/stable/classes/class_gltfdocument.html).
The toolchain follows the official [C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html)
and [Windows .NET release](https://godotengine.org/download/windows/).
