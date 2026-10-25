# Crowded army engagement and performance — October 20, 2026

Build-65 corrects oscillating rear ranks and attack-move boundary reacquisition.
See [the 25 October reproduction and verification](MELEE-QUEUE.md).

Build-64 adds request-based combat scheduling, indexed movement and reservations,
shared terrain candidates, and 12–100-per-side mixed-army benchmarks. See the
[24 October research and implementation report](RTS-RESEARCH.md) for current
measurements and remaining crowd-positioning work. The sections below record
the earlier acceptance baseline.

The owner reported that only three or four units fought while the others waited,
on both teams, and that performance worsened after importing collision sizes.
The existing checks required every squad to fight; they did not require broad
participation within each squad. A new 18-versus-18 fixture reproduced the gap.

## Crowd and attack-move follow-up

Further owner feedback reopened crowded combat responsiveness. The source Main
boxes were being treated as rigid allied personal space, and attack-move pursuit
limits could exclude nearby fights. This pass changes the demo policy:

- Allies use a solid core at 50% of their combined full radii. This permits
  squeezing between friends without stacking or walking through one another.
  Terrain, static obstacles, enemy contact, hit picking and resting formations
  retain full envelopes. This is a feel adjustment, not recovered original physics.
- Reachable adjacent enemies take priority over distant approach routes. This
  also works during regrouping and for idle units after an order finishes.
  Explicit Move, Hold Position, Force Attack and active swings retain their intent.
- Nearby enemies fighting allies can be approached around the local battle,
  within the existing 8 m acquisition radius. Fleeing targets still obey the march
  corridor. Attack positions allow a melee-range approach margin, and the bounded
  route search applies that same area throughout the route. Previously it could
  propose a route that the movement limit would later refuse to follow.
- Combat steering now retains its passing side, and blocked attackers examine
  up to twelve alternative targets on their existing staggered planning turns.
- Aragorn's demo visual scales by 1.12; his full radius is 800 mm and height
  2,347 mm. The original measurements remain in `Source.Collision` in the cache.

The new focused regressions cover passage between held allies, solid enemy
contact, interpolated core separation, repeatable playback, immediate threats
during regrouping, idle acquisition and preserved Move behavior. Both the
explicit Attack fixture and the Attack-move fixture bring all 18 units per side
into melee. The actual Godot input checks likewise record 18/18 hitters and peak
fighters for both orders. The real-map formation check finishes both orders with
all 18 allies alive, no core overlaps and a maximum march width of 9.088 m.
These are implementation checks, not original-game equivalence claims.

```powershell
./prototype/run.ps1 -AttackMoveEngagementSmoke
./prototype/run.ps1 -EngagementSmoke
./prototype/run.ps1 -FormationSmoke
```

The sections below retain the measurements from the earlier rigid-body pass.

### Automatic idle guard

The owner confirmed the crowd changes feel much better, then identified idle
units standing beside nearby fights. Idle player units now detect enemies within
10 m and support fighting allies within 10 m, with the support target bounded to
12 m from their resting origin. They approach and fight without an explicit order,
drop pursuits beyond 12 m and return near their resting origin. An occupied origin
permits settling nearby. Move completion sets a fresh origin; explicit orders
replace this behavior, and Hold Position never pursues. The red demo opponent
keeps its existing aggressive AI. These radii are prototype policy.

Focused checks cover damage after automatic approach, support beyond immediate
melee reach, passing enemies, bounded pursuit/return, movement arrival, explicit
Move replacement, Hold Position, disconnected terrain and deterministic replay.
The owner increased awareness to 10 m and requested that Stop preserve default
guarding. Stop now cancels its current action/queue, returns to Idle, and permits
automatic acquisition starting the following tick. Hold Position remains a
stationary order. The 10 m boundary and Stop-to-guard transition have regressions.
Idle origin, return state and stop tick are included in the replay digest.

## Behavior correction

Imported units use their physical pair radii for approach positions and strikes.
The legacy probe's additional comfort margins could immobilize units in a packed
formation even when their collision bodies fitted. Target allocation estimates
available ring capacity, accounts for already assigned attackers, and prefers
nearby opponents with room. Waiting or repeatedly blocked attackers may choose
another reachable hostile target. Allies within 6 m share active threats so rear
units can join a nearby fight. These are prototype policies, not recovered AI.

Attack retains its original `OrderTarget` while `Target` can be an overflow
opponent. Queued orders advance when the primary target dies, including while
an overflow opponent is still alive. Force Attack keeps its explicit target;
Hold never pursues and Guard retains its leashes. Active swings are not retargeted.
Source attacker caps, timed damage and full body separation remain enforced.

Approachers that repeatedly steer around bodies replan their route instead of
circling forever. A full isolated target can still have waiting attackers; the
new army check requires nearly all units to participate when other opponents
are available, rather than requiring every unit to hit one victim simultaneously.

## Performance correction

Each approach uses one bounded search for a set of acceptable ring nodes instead
of separate full paths to many candidates. Each search expands at most 2,048
nodes. Three large-body units may revise plans per fixed tick; strikes and
existing movement continue at 20 Hz. Failed, settled waiting plans retry less
often. Ground sweeps inspect indexed obstacle-edge neighbors rather than all
pairs of crossed cells. Legacy 350 mm probe fixtures retain their earlier policies.

Body-region caches now warm during scene loading. The first real-map attack had
triggered an approximately 0.86-second full-map connectivity pass during play;
moving that work before interaction removes that particular command-time hitch.

Local Debug benchmark: 800 simulation ticks, 18 large infantry per side, high HP
to keep the front intact, one initial friendly focus-attack command, no renderer.
Both versions use the same fixture. Timings are one local measurement, not FPS.

| Measurement | Before | Corrected |
|---|---:|---:|
| Distinct friendly / enemy units landing hits | 2 / 15 | 18 / 17 |
| Average friendly / enemy fighters during ticks 400–799 | 1 / 11 | 16.70 / 16.73 |
| Total simulation CPU time | 3,572 ms | 1,729 ms |
| 95th-percentile step | 11.67 ms | 8.18 ms |
| Slowest step, including cold setup | 150.22 ms | 60.03 ms |

This fixture uses about 52% less simulation CPU time while many more units fight.
The rendered Helm's Deep reproduction records separate step timings after
loading-time warmup. Generated reports and captures stay in ignored `converted/`.

## Verification

The deterministic fixture requires at least 16 of 18 units on each side to land
hits, an average of at least 12 fighters per side during the second half,
non-overlapping bodies, and identical replay state. Force Attack, Hold and queued
primary-target death have focused checks. Existing narrow-route, traffic,
equipment, timing, order/input and 80 FPS interpolation checks remain enabled.

The actual Godot `-EngagementSmoke` drag-selects all 18 allies, right-clicks an
enemy, runs 800 ticks and checks at least 14 distinct hitters per side and body
separation. Casualties and physical access can keep every unit from landing a hit
in a particular source-stat encounter. The report includes peak fighters and
simulation step timings; it does not assert a machine-dependent timing threshold.
The inspected 4K run recorded 18 friendly and 17 enemy hitters, peaks of 17
fighters per side, 1.40 ms average / 6.51 ms 95th-percentile / 29.72 ms maximum
simulation steps, plus roughly 1 second of region-cache setup during loading.

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --cache converted/prototype
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --engagement-benchmark converted/prototype/engagement-benchmark-after.json
./prototype/run.ps1 -EngagementSmoke
./prototype/run.ps1 -EngagementSmoke -Resolution 3840x2160 -CapturePath converted/prototype/engagement-fixed-4k.png
```
