# Godot C# skirmish sandbox

First Phase 3 milestone. Loads the local Helm's Deep GLB, Aragorn, five Elf
Knights and six Uruk-hai. Select friendly units, issue move/attack orders and
fight a simple opponent. The engine choice was approved on Oct 19, 2026.

Squads now traverse layered walkable geometry with terrain entrances and
congestion replanning. Static building/object footprints now block movement.
Walkway edges now require support for the full 350 mm unit radius.
Placed obstacles can be removed and restored while units replan their routes.
Original mask semantics and broader traffic coordination remain open.
The remaining Phase 3 work and acceptance criteria are in
[the local roadmap](../tools/Roadmap.md).

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
| Right-click terrain | Move the selection in a small formation |
| Ctrl + right-click | Order movement on terrain beneath bridges and walkways |
| Right-click an enemy | Attack, approaching when outside melee range |
| Alt + right-click a placed obstacle | Remove it; click the same space again to restore it |
| WASD / middle-drag | Pan the camera |
| Q / E | Rotate |
| Mouse wheel | Zoom |
| Space | Pause/resume simulation and unit animation |
| Esc | Clear selection |
| Restart sandbox | Reset the encounter |

## Verification

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --cache converted/prototype
./prototype/run.ps1 -Smoke
./prototype/run.ps1 -SurfaceSmoke
./prototype/run.ps1 -ObstacleSmoke
```

The simulation runner has no Godot or test-package dependency. Its 68 checks cover
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
retain the occupied physical segment until the unit reaches a cell center.
Input geometry checks cover stacked decks, ground below overpasses, slopes,
map boundaries, ray range and shared triangle edges far from the origin.
Formation regressions cover disconnected patches near the destination,
connectivity changes after obstacle updates, and stopping at a cell center
after an unreachable or invalid replacement order.
The optional cache check adds fourteen checks: runtime layers reproduce the
static ground snapshot, all placed building centers are
blocked, and all six friendly units visit each of Helm's Deep's four walkable
surfaces without overlap and reach every order.
The headless Godot smoke test loads
the actual map and twelve models, dispatches mouse/key events through the
viewport, and checks selection, move/attack picking, zoom, pause/resume,
animation and damage over 420 fixed ticks. Its report is
`converted/prototype/smoke-report.json`.
The surface smoke test places six units on an elevated deck, dispatches a
right-click order, then checks Ctrl + right-click terrain picking with the same
screen position. The obstacle smoke test removes and restores an obstacle
through actual Alt + right-click input and verifies its visibility each time.

For a rendered capture (opens the engine, saves a frame, then exits):

```powershell
./prototype/run.ps1 -CapturePath converted/prototype/preview.png
```

## Boundaries of this milestone

The simulation runs at 20 Hz, with integer millimeter positions, integer HP,
tick-based cooldowns and stable unit/cell ordering. A* uses four neighbors;
nearby units block overlapping movement. Godot camera, animation and rendering
do not feed timing or floating-point values back into simulation stepping.
UI hit positions are quantized into cell commands before the next fixed tick.

Movement currently interprets the saved painted walker bits as allowed cells.
This is a prototype policy, not a claim of original-game passability fidelity.
Auxiliary ground mask semantics remain open.
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
covers a live unit or a segment the unit is crossing. Route changes bring moving
units back to their last cell center before taking a new path; sealed orders
retain their destination and resume after reopening. This sandbox control
toggles existing placed objects; building health, construction and destruction
gameplay remain Phase 4 work. New or moving footprints still require an adapter.
The raw painted mask and initial blocked snapshot remain available for auditing.
Walkable helpers append separate nodes to the ground grid; the
current cache has 1,207 surface nodes and 39 height-matched terrain entrances.
Links require continuous triangle coverage with at most 150 mm deviation from
the interpolated height, no more than one cell of rise, and terrain joins within
250 mm. Source model scale and placement transforms match the rendered scene.
The original MPL route codes are unused, including the two cyclic bridge tables.
Mesh nodes and edges require continuous support for a swept 350 mm square
around the unit center, conservatively enclosing its circular body. Triangle
subtraction checks the whole footprint; overlapping triangles cannot conceal
gaps. Support must match the local slope and interpolated height within 150 mm,
so lower floors cannot fill holes in a deck. This conservative one-meter raster
can omit narrow routes. Mouse orders intersect source terrain and walkable mesh
triangles. Ctrl + right-click bypasses decks to target terrain beneath them.
Grid traversal limits terrain queries to cells crossed by the ray; precise
triangle calculations avoid cracks along shared edges. The input adapter uses
floating-point geometry and quantizes its result into an integer node command;
this math does not run inside simulation stepping. Mesh-edge clicks snap to a
nearby node on the clicked source surface. These are navigation queries;
rendered props and water do not occlude movement picking.
Units retain their surface node and integer elevation; collision and melee use
vertical separation. Congested units wait before entering occupied waypoints
and retry routes around occupied cells after ten blocked ticks (half a second).
Temporarily sealed routes retain their destinations and retry; statically
unreachable orders end without a route. This deterministic grid policy passes
the six-unit gate test. Opposing friendly move orders can request a reachable
side pocket: one unit holds there until the other finishes, then resumes its
original order. Refuge choices must leave a route around the other's final
destination. Stable unit/cell ordering makes this repeatable; changing the
priority order releases its yielding partner. Combat units do not request
yielding. Idle friendly blockers can also yield and return. Early arrivals in
the same squad order exchange formation slots when they block unfinished slots.
Formation slots stay in the destination's connected region, so nearby islands
cannot consume a slot and strand part of a squad. Region queries are cached and
invalidated when obstacle masks change. Unreachable or invalid replacement
orders return a moving unit to its last cell center before stopping.
Refuges avoid other units' reserved destinations. Corridors without passing space wait safely, and complex multi-unit
gridlocks still need broader coordination. Full local avoidance remains open.

Rules are placeholders: 100 HP, 12 melee damage, one-second cooldown, 3 m/s.
Enemies acquire nearby targets; attacked units retaliate. This is not the
original combat/economy or skirmish AI. Unit states choose original idle/run/
attack/death clips; Phase 1's complete attachment/texture event timelines still
need an engine adapter. Audio, particles, water animation, cloth simulation,
in-game install selection and production importer packaging remain open.
The command log and integer core make repeatability testable; cross-platform
lockstep/networking is not claimed to be implemented.

Cached run/walk clips are adapted to play in place: `J_Position` horizontal
travel is subtracted from every independent skeleton root, preserving pelvis
bobbing and child motion. The original Phase 1 model exports are untouched.

Runtime import uses Godot's [GLTFDocument API](https://docs.godotengine.org/en/stable/classes/class_gltfdocument.html).
The toolchain follows the official [C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html)
and [Windows .NET release](https://godotengine.org/download/windows/).
