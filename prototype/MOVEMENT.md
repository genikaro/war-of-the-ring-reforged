# Movement responsiveness pass — October 19, 2026

Open-ground move orders now use the complete visible route rather than looking
ahead six grid edges only after a bend. Integer interpolation follows that
heading at 3 m/s by default. A replacement order starts from the current physical position,
and cancellation stops there instead of walking back to an old waypoint.

Each tick checks the unit's swept body against terrain, other bodies,
motion already recorded during that tick and constrained movement reservations.
Distant waypoints do not own the empty space between them. Local avoidance tries
directions relative to the requested heading, with 450 mm lookahead. After twenty
avoidance steps it hands congestion back to the existing coordinated solver;
blocked free-ground routes retry after three ticks. All choices use integer
arithmetic and stable ordering.

The twenty-step handoff applies to move orders. Combat uses the positioning
policy below and replans its own approaches rather than requesting move-order
refuges.

Squads on wide ground use a deterministic minimum-cost assignment of units to
formation slots. Minimizing total squared travel avoids much of the crisscross
caused by assigning slots by unit ID. Slot spacing and connectivity checks remain.

## Reproducible comparison

The comparison below uses the unchanged default running mode.

The baseline is the simulation from commit `0d77058`. Both versions run the same
`ResponsivenessChecks.Measure` fixture without Godot or rendering feedback:
40 × 24 open one-meter cells, six units at `(2,2)`, `(4,2)`, `(6,2)`, `(2,4)`,
`(4,4)`, `(6,4)`, ordered toward cell `(30,14)`. No wall-clock timings are used.

| Measurement | Baseline | Updated |
|---|---:|---:|
| Six-unit formation finishes | 274 ticks / 13.70 s | 211 ticks / 10.55 s |
| Pending-order samples spent waiting | 168 | 0 |
| Units moving on first tick | 6 | 6 |
| Max distance from direct heading during the first 2 s of a shallow move | 2,844.599 mm | 0.542 mm |
| First X step after redirect toward the right | −150 mm | +150 mm |

The formation completes 23% sooner in this fixture. This is a simulation result,
not an original-game fidelity claim or a guarantee for arbitrary terrain.

Generate the updated metrics and recorded squad positions:

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --movement-benchmark converted/prototype/movement-after.json
```

The local `converted/prototype/movement-before.json` contains the matching
baseline run; generated reports are ignored. The regular test runner adds twelve
regressions for headings, immediate stopping/redirecting, repeated group orders,
formation arrival, deterministic replay, construction clipping a stopped body,
and retaliation while moving freely.
It checks separation between ticks as well as at tick boundaries. Existing
single-file, opposing-squad and layered-map checks remain enabled.

Godot's motion smoke test requires actual free-ground movement samples after
dispatching a six-unit order through mouse input. It also verifies animation,
gradual turning and rendering at 80 FPS over the 20 Hz simulation.

## Combat positioning

Attackers reserve reachable navigation nodes around a target within the effective
body-based melee range described in [combat notes](COMBAT.md). They prefer the nearest available attack position and retain it
while it stays useful. When the ring is occupied, additional attackers reserve
holding positions up to 4.5 m away and wait for a slot. Slot reservations account
for other allies' planned positions as well as actual bodies, including allies
attacking a different nearby target.

Ground approaches use the free-ground follower where supported. Imported bodies
use the sum of both radii for approach slots, contact and permission to strike.
Adding a comfort margin trapped units inside their starting formations and
prevented otherwise legal attacks. Settled fighters stop and face their opponent.
The original 350 mm probe fixtures retain
their historical 700 mm / 1 m / 1.1 m spacing.
Plans are reconsidered after six ticks if the target moves out of reach, a
position becomes blocked or a holding unit can take an attack position. Positions
are released when a move order replaces combat. Fighters can acquire a nearby
reachable opponent after their target dies. Attacks require ground clearance or
short validated mesh links, preventing melee through walls or disconnected decks.

The combat regression suite covers a stable six-unit surround, fourteen attackers
with settled waiting units, a moving target, newly placed obstacles, sealed walls,
separate floors, target death, cancellation and deterministic replay. It checks
separation during tick interpolation as well as at tick boundaries.

The demo contains three six-unit groups per side (36 units), spaced beyond the
enemy's initial acquisition range. It starts framing those squads; keys 1/2/3
select the blue groups. `-CombatSmoke` dispatches their orders through Godot
input, requires all three blue squads to fight and checks physical separation.
Local previews are `converted/prototype/demo-squads.png` and `demo-combat.png`.

## Remaining constraints

Wide ground is identified by an available diagonal with supported flanks. An
order uses free movement only when its anchor routes and destination slots stay
on that ground. Orders through single-file cells, bridges and other mesh surfaces
use the established constrained solver. Combat uses that solver for constrained
approaches and the free-ground follower for wide-ground approaches. Mixed move routes have not yet
been converted to continuous movement: doing so needs reliable transfer between
rolling body checks and reserved passage endpoints. Elevated nodes still use
their validated graph links; cutting across unverified deck geometry is unsafe.

Clicks still select integer navigation cells. The graph search remains eight-way;
the free-ground route follower and local steering produce the additional angles.
The conservative corner policy and original movement/combat response formulas
remain unverified.

## Per-type bodies and crowd correction — October 20, 2026

The three demo types now use scaled Main collision boxes: radius/height in mm
are Aragorn 561/2095, Elven Knight 927/2534 and Uruk-hai 693/2197. Full pair radii
and vertical intervals apply to spawn safety, swept contact and reservations.
Each type routes through its own cached terrain/object/mesh clearance and
headroom profile. Formations allocate slots using their largest body. The base
mesh graph still starts from 350 mm support; larger profiles only remove nodes
and links, so some routes remain conservatively excluded. See
[the source interpretation](../tools/NOTES-unit-collision.md).

An 18-unit formation reproduced jitter and stalled arrivals after the size
change. Large units on wide ground now keep continuous avoidance when crowded,
instead of retreating toward a cell center that no longer fits. Blocked early
arrivals can advance into unfinished slots, and crossing units exchange slots
when both benefit, within the same Move or Attack-move order. Narrow passages retain the
constrained solver. Tests require combined and redirected 18-unit formations to
arrive, stay still afterward, and never overlap during tick interpolation.
Another fixture requires excess melee attackers to settle around a stationary
target rather than shuffle indefinitely. Selection rings now show body radius.

The subsequent [engagement correction](ENGAGEMENT.md) covers both armies fighting,
crowded-target redistribution, stalled-approach replanning and performance. Three
large-body units may revise approaches per fixed tick; current routes and strikes
continue every tick. Initial body-region caches are built while loading.

## Crowded arrival and attack-move cohesion — October 20, 2026

Large bodies marching without a combat target retain their chosen passing side
for eight ticks (0.4 seconds). Local steering uses forward or lateral steps;
when those are blocked, it waits or replans instead of alternating backward
steps. Replans skip the starting cell center, avoiding a short reverse correction.
Attack-move shares Move's formation-slot exchanges while travelling. Active
melee keeps its separate approach policy so attackers can surround opponents.

Attack-move pursuit uses an 8 m encounter radius, a 3 m lateral corridor and a
2 m rear allowance. Wide-ground orders keep the original start-to-destination
direction across encounters; constrained routes use a local path direction.
Targets leaving this area are released for four seconds, unless attacking in
melee reach. Units regroup within 2.5 m of the encounter origin and resume the
destination. Completion clears retaliation-driven acquisition. Explicit Attack
still pursues its target. These bounds are prototype policy, not recovered AI.

Visual facing uses net movement across the entire fixed tick, filters lateral
changes, ignores corrections below 20% of a stride, and retains its heading
inside a 10-degree deadband. New orders reset that filter; strikes face the target.
This affects presentation only, retaining deterministic integer movement.

The 18-unit open-ground regression now finishes both Move and enemy-free
Attack-move in 218 ticks, with identical trajectories, no overlap and no motion
after settling. Abrupt net direction changes drop from 163 (Move) / 1,439
(Attack-move) to 28 each in the same 900-tick fixture. Previously only 13 of 18
Attack-move units had arrived. A second fixture sends enemies fleeing toward
both flanks and requires the whole group to resume its destination.

`./prototype/run.ps1 -FormationSmoke` runs at 80 rendering frames per simulated
second. It drag-selects all 18 allies, dispatches a common-point Move and a
subsequent contextual Attack-move near stationary enemies, and requires both
arrivals, bounded group width and body separation. It runs 2,400 simulation
ticks to allow source-stat combat to finish; its report includes turn changes,
participation and step timing. The inspected real-map run retained all 18 allies
and a maximum 10.617 m group width.

## Walk/run control — October 20, 2026

F2 and the original run icon beside the minimap toggle selected units' movement
mode. A mixed selection switches to Walk; an entirely walking selection switches
to Run. No selected units means no command. `ToggleWalkRun` is recorded against
the explicit living unit IDs and applied in sequence at the next fixed tick,
including after a pause. It changes no route, queue, target, windup or cooldown.
The mode participates in the state digest and survives later orders.

Running keeps the accepted **3 m/s** baseline. Walking scales that speed using
the first positive Walk/Run state choices' `J_Position` displacement divided by
clip duration, before prototype root-motion removal. LSK uses XY as its ground
plane and Z as up; the importer also normalizes the Uruk joint's trailing space.

| Unit | Source walk / run speed | Walk scale | Nominal prototype walk speed |
|---|---:|---:|---:|
| Aragorn | 2.117924 / 5.809129 | 364585 ppm | 1.093755 m/s |
| Elven Knight | 2.465559 / 5.256238 | 469073 ppm | 1.407219 m/s |
| Uruk-hai | 2.277097 / 5.260435 | 432872 ppm | 1.298616 m/s |

These are an adapter policy, not measured original world speeds. Per-tick
budgets truncate to whole micrometers. Aragorn's `AIApproximateRunVelocity`
equals his walk clip's speed in this install;
it cannot safely set runtime run speed. Uruk-hai has three Run choices; this slice
uses the first, matching the prototype's existing run animation. Stamina and
original default movement-mode behavior remain unverified.

Rules cache `Source.Locomotion` preserves clip names, state IDs, durations, source
speeds and joint names alongside the existing skeleton/model hashes. Refresh it
with `python tools/prepare_prototype.py --rules-only`. The original Walk and Run
clips already exist in the in-place model cache; no model re-export is needed.
Synthetic probe profiles use a 50% walk scale.

Motion samples share one normalized tick clock. Consumed distance is converted
from the unit's speed budget before interpolation, so a walking unit moves
throughout the tick. Early arrivals hold for the remaining fraction. Ground
steering and constrained deck segments retain the same body checks and passage
reservations. Tests cover mode changes in mid-route and mid-swing, queued orders,
mixed selections, paused double toggles, full-tick/early-arrival playback, deck
traversal, mixed-speed squad separation and serialized replay. The Orders smoke
also checks both UI paths and distinct source clip playback; a 4K capture is
saved at `converted/prototype/walk-run-4k.png`.
