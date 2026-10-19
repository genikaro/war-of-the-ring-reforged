# Godot C# skirmish sandbox

First Phase 3 milestone. Loads the local Helm's Deep GLB, Aragorn, seventeen Elf
Knights and eighteen Uruk-hai in three six-unit squads per side. Select friendly units, issue orders through the command panel and
fight a simple opponent. The engine choice was approved on Oct 19, 2026.

Squads now traverse layered walkable geometry with terrain entrances and
congestion replanning. Static building/object footprints now block movement.
Walkway edges now require support for the full 350 mm unit radius.
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

The user has accepted movement and combat spacing for this prototype. Further
polish is deferred. The original console artwork, scalable HUD, control groups,
queued waypoints and explicit orders are implemented. The bottom action row is
Attack, Hold, Guard, Patrol. Attack/A resolves by target: enemy pursuit,
neutral/friendly force attack, or ground attack-move. Move and Stop work without
visible buttons. These three types now use imported health, armor, melee DPS and
animation-timed hit events, with weapon visibility and impact sounds. See
[combat rules and approximations](COMBAT.md) and [command behavior](COMMANDS.md).

Combat reserves separate reachable attack positions around each target, keeps
allied spacing, and parks excess attackers outside the melee ring. Settled
fighters hold position and face their target. Attack orders continue onto nearby
enemies when their target dies. These are prototype rules, not recovered original
combat formulas.

The map starts with all six squads in view. The Menu has Squad, Fortress and Map
camera buttons; their F1/F2/F3 shortcuts are removed. Runtime textures have mipmaps and
anisotropic filtering, painted lake colours/opacity render correctly, and the
spawned teams use the original blue/red colours. Selected units have hollow
gold rings. The [reference review](../references/original-game/PROTOTYPE-REVIEW.md)
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

To update just the squads and camera in an existing cache:

```powershell
python tools/prepare_prototype.py --demo-only
```

To refresh imported combat rules, equipment bindings and impact sounds in an
existing cache, run `python tools/prepare_prototype.py --rules-only`.

For another owner's install, provide its folder (containing `Database/`
and `Archives/`). Extraction verifies CRCs and paths; the install is read only:

```powershell
python tools/prepare_prototype.py --install "C:/Games/War of the Ring"
```

All generated game data stays in ignored `converted/prototype/`. The source
project contains no game assets. At launch the manifest's content hashes are
checked before loading. Never distribute the local cache with the project.

## Controls

| Input | Action |
|---|---|
| Left-click / drag | Select friendly units |
| Shift + selection | Add to selection |
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
| S | Stop movement and pursuit; return blows within reach (no visible button) |
| H | Hold Ground; engage within reach without chasing |
| G / T, then left-click | Guard / Patrol |
| Shift + click / right-click | Queue orders and formation waypoints |
| Alt + right-click a placed obstacle | Remove it; click the same space again to restore it |
| Arrow keys / middle-drag | Pan the camera |
| Menu: Rotate left / Rotate right | Rotate the camera |
| Mouse wheel | Zoom |
| Space | Pause/resume simulation and unit animation |
| Esc | Cancel targeting; otherwise clear selection |
| Right-click while targeting | Cancel targeting and retain the current order |
| Restart sandbox | Reset the encounter |
| Menu: All squads / Fortress / Whole map / Focus selection | Frame a comparison view or the current selection |
| F4 / console Menu button | Open/close debug tools |
| F10 | Show/hide the debug UI |

Planned controls (user decision): **Q/W/E/R for abilities 1–4** and **F2 for
walk/run**, retaining S Stop. These are intentional differences from the
original game's bindings. Q/E rotation and F1/F2/F3 camera shortcuts have been
removed; camera buttons and launch-time `-View` captures remain available. The
planned abilities and walk/run actions are not implemented yet. See
[command plans](COMMANDS.md#planned-corrections-from-the-local-original-game-session).

The debug menu has live pause/tick and selected-order information, Pause/Resume,
Step one tick (50 ms while paused), Restart encounter, camera view/rotation
buttons, Focus selection and the saved 75–150% interface size. The current
shortcut guide is collapsible; the last action is labeled separately from live
state. The menu scrolls to fit above the console. Opening it does not pause play.
Use `./prototype/run.ps1 -DebugMenu` to start with it open, including when saving
a normal viewport capture with `-CapturePath`.

## Verification

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --cache converted/prototype
./prototype/run.ps1 -Smoke
./prototype/run.ps1 -MotionSmoke
./prototype/run.ps1 -CombatSmoke
./prototype/run.ps1 -MeleeSmoke
./prototype/run.ps1 -CommandSmoke
./prototype/run.ps1 -OrdersSmoke
./prototype/run.ps1 -SurfaceSmoke
./prototype/run.ps1 -ObstacleSmoke
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --movement-benchmark converted/prototype/movement-after.json
```

The simulation runner has no Godot or test-package dependency. Its 243 checks cover
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
The optional cache check adds 31 checks, including four repeatable source-unit
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
three blue squads. Its 23 checks cover the four bottom-row actions, target-dependent
Attack/A, hidden Move/Stop controls, targeted and immediate orders, disabled
commands, cancellation, mixed unit types, combat interruption, pause/resume,
drag release over the panel and removal of dead selected units. Its report is
`converted/prototype/command-smoke-report.json`.
The orders smoke test adds 20 actual input checks for control groups, centering,
new commands, queues, HUD adjustment and debug pause/step/open/close controls,
writing `converted/prototype/orders-smoke-report.json`.
Unit scenes share imported resources while each instance keeps its own skeleton
and animation player.
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
envelope with a 350 mm radius and 1.8 m body. Vertical bounds preserve separate
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
toggles existing placed objects; building health, construction and destruction
gameplay remain Phase 4 work. New or moving footprints still require an adapter.
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
them, so a parked unit never blocks another link. Mouse orders intersect source terrain and walkable mesh
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
open. The larger 6-to-14-unit stress suite has not been revalidated for this pass.

The three demo types use imported stats and animation-timed melee. Armor math,
event-scale normalization and effective range remain documented approximations;
see [combat notes](COMBAT.md). Movement remains 3 m/s.
Enemies acquire nearby reachable targets; attacked units retaliate. Attack orders
retain nearby combat intent after a target dies; move orders clear it. Melee
requires a supported route between attacker and target, so walls and disconnected
floors cannot become attack shortcuts. Settled allies keep 1.1 m spacing, and
approaching allies keep 1 m where possible, allowing already crowded units to
separate. The hard body diameter stays 700 mm. This is not the
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
