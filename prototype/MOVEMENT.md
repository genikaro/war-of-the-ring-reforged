# Movement responsiveness pass — October 19, 2026

Open-ground move orders now use the complete visible route rather than looking
ahead six grid edges only after a bend. Integer interpolation follows that
heading at 3 m/s. A replacement order starts from the current physical position,
and cancellation stops there instead of walking back to an old waypoint.

Each tick checks the unit's swept 350 mm body against terrain, other bodies,
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

Attackers reserve reachable navigation nodes around a target within the existing
2 m melee range. They prefer the nearest available attack position and retain it
while it stays useful. When the ring is occupied, additional attackers reserve
holding positions up to 4.5 m away and wait for a slot. Slot reservations account
for other allies' planned positions as well as actual bodies, including allies
attacking a different nearby target.

Ground approaches use the free-ground follower where supported. Approaching
allies keep 1 m apart where possible; an already crowded unit can move away to
increase separation. Settled fighters require 1.1 m of allied space, stop their
route and face their opponent. The hard 700 mm body separation remains in force.
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
The 350 mm clearance, conservative corner policy and original movement/combat
constants have not been reverse-engineered further in this pass.
