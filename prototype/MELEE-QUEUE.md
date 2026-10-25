# Build-65: stable rear ranks in crowded melee

25 October 2026. The reported symptom was melee soldiers stuck behind allies,
continuously walking and occasionally turning around. Build-64's safety and completion
checks passed, but did not reject prolonged unproductive motion.

## Reproductions and causes

The new `--melee-queue-benchmark` keeps melee fronts alive for 1,600 ticks and counts
post-contact walking, sharp reversals and one-second walking loops.

Two causes were reproduced:

* Attack-move tested prospective targets from the unit's physical position, then stored
  the containing cell center as the encounter anchor. A boundary target could pass one
  test and fail the next. The nearby-allied-fight exception also ran before the ignored
  target check, allowing an abandoned enemy to be reacquired immediately.
* Local steering accepted movement that avoided collisions but made no sustained
  progress. A blocked soldier could keep circling or shuffling at full animation speed.
  Tiny cell-center corrections also counted as locomotion in presentation.

Acquisition now uses the same encounter anchor throughout. The ignored-target interval
also applies to shared allied fights; an enemy already in attack reach still takes
immediate priority for self-defense.

## Traffic patience and animation

The simulation watches physical displacement over 20 ticks. A soldier that walks at
least six strides while staying within three strides enters a traffic queue. It rests
for six ticks, then permits forward local avoidance while retaining normal terrain
route planning. It also skips the redundant starting-cell-center correction on that
approach. Once it makes sustained progress or starts fighting, the restriction clears.

This applies to crowded combat approaches. Ordinary movement, workers' economic orders,
building attacks and constrained deck links retain their existing policies. Player
orders reset the queue immediately. Attack reach is checked before waiting, so a new
adjacent threat or an opening in the front can be used without waiting out a timer.
All timers and progress samples use fixed ticks and are included in the replay hash.

Presentation uses the same one-fifth-stride movement threshold as heading playback
before selecting a combat locomotion clip. Tiny corrections and a movement trace that
returns to its starting position use the ready stance. A stationary queued melee unit
faces its target. Actual full walking and running strides still animate immediately.

## Measured behavior

The following synthetic, durable-front fixtures count events during ticks 400–1,599.
Counts describe simulation trajectories, not FPS. The 64-per-side fixture uses melee
bodies and two opposing attack-move orders; the smaller fixture reproduces the original
18-per-side engagement. A reversal requires meaningful movement in opposite directions
on successive non-attacking ticks. A walking loop travels more than one metre in a
20-tick window but ends within 0.4 m of its starting position.

| Fixture | Build-64 | Build-65 |
|---|---:|---:|
| 18 vs 18 attack-move: reversals | 906 | 0 |
| 18 vs 18 attack-move: walking loops | 17 | 0 |
| 18 vs 18 attack-move: unique hitters, both teams | 34 | 34 |
| 64 vs 64: reversals | 9,794 | 109 |
| 64 vs 64: walking loops | 389 | 3 |
| 64 vs 64: rear-rank moving samples | 13,703 | 309 |
| 64 vs 64: unique hitters, both teams | 48 | 48 |

The 64-per-side checks require broad combat participation as well as low oscillation.
They then remove the friendly front rank and require previously queued soldiers to
advance and land hits. A replacement Move must clear traffic waiting immediately.
Additional checks cover source-sized bodies, fractional render separation, deterministic
replay, and locomotion thresholds. Existing engagement, guard, leash, ranged timing,
force-attack and queued-order checks remain enabled.

Final validation: all 1,024 simulation checks pass, as do all 18 seeded long-run
scenarios (96,000 primary ticks plus replays, up to 400 units per side). The real-map
Godot attack-move engagement and formation smokes pass, and the 80 FPS motion run
records interpolated movement and turns. Generated reports are
`converted/melee-queue-build64.json`, `converted/melee-queue-build65.json`,
`converted/build65-soak.json`, and the `converted/build65-*-smoke.log` files.

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation -c Release -- --combat-checks
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation -c Release -- --melee-queue-benchmark converted/melee-queue-build65.json
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation -c Release -- --army-soak converted/skirmish converted/build65-soak.json
./prototype/run.ps1 -AttackMoveEngagementSmoke
./prototype/run.ps1 -FormationSmoke
```

Restart the game to load build-65. No content-cache rebuild is needed. This improves
how blocked approaches wait and resume; the full continuous-contact/predictive-steering
architecture in [RTS-RESEARCH.md](RTS-RESEARCH.md) remains future work.

Build-68 adds [bounded local flanking](FLANKING.md). Traffic checks now distinguish
productive movement around a front from movement with less than 400 mm of net progress
over a second. Total turns and moving samples remain reported, while limits apply
to stalled movement, stalled reversals and walking loops. Extra movement must also
produce broader participation: the 64-vs-64 scenario increases from 48 to 81 hitters
while retaining zero unproductive walking loops.
