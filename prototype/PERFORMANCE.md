# Large-match performance

The October 29 performance pass (build-119; concurrent music work advances the
shared checkout to build-120) addresses retained corpse assets and avoidable
simulation allocations.

Expired corpses previously kept their hidden models, skeletons, animations,
materials and health bars. After the existing 60-second lifetime and two-second
sink, those children and their managed references are now released. Each historical
unit keeps its simulation id and two empty, disabled lookup nodes. One shared
model template per model/team stays available for recruitment and transformations.
Dead units immediately leave the position, attack reservation, transit and target
indices; scripted revival adds them back. Empty target buckets are removed.

Blocked wide-ground movement no longer builds an occupancy map that its retry
does not use. Read-only proximity queries traverse spatial buckets without copying
them into temporary lists. Collision checks and frequently used ability getters
avoid captured delegates. Sandbox dummies now compare their effective unit type
when refreshing their model, avoiding a full model replacement every physics tick.

## Measurements

Release simulation benchmark, imported Sandbox combat rules, 800 ticks (40 game
seconds), durable mixed armies. Local comparison with a separate source snapshot
that restores the previous allocation paths:

| Units | Before, total CPU time | After | Before allocation | After |
| --- | ---: | ---: | ---: | ---: |
| 128 | 1,677 ms | 1,519 ms | 959 MiB | 629 MiB |
| 200 | 3,503 ms | 2,795 ms | 1,758 MiB | 1,098 MiB |

The 200-unit case uses about 20% less CPU time and allocates 38% fewer bytes.
All nine army benchmark digests match. Smaller and choke scenarios have mixed
timing results; these are diagnostic CPU measurements, not rendered FPS claims.
Generated evidence is in `converted/prototype/performance-119-*.json` and `.log`.

The recruitment/death smoke repeats three 64-unit waves. After burial, unit nodes
are 152 / 280 / 408 for 76 / 140 / 204 historical units, exactly two per unit.
Shared template nodes remain at 157 throughout; later recruits still animate.
The 512-unit durable soak passes 4,000 ticks with 41 replay checkpoints and
27.5 million collision/interpolation pair checks. Its measured p95 is 15.35 ms
per simulation tick, so very large battles still limit fast-forward speed.

## Build-121: keep tick work independent of casualty history

A late-match benchmark keeps 200 living units, geometry, orders and duration
identical while appending 0, 1,000 or 10,000 historical casualties. Before this
pass, many tick systems still visited all records and repeated death cleanup.

`UnitRoster` now maintains an active set in stable id order. Cached snapshots
remain safe across recruitment, damage and revival. Recent casualties complete
the existing cleanup phases and any avoidance-lock countdown before retiring.
Retired records remain available by id for projectiles, match statistics and
hero recruitment; reviving a record restores its position in the active order.
Fixtures that append or clear the public historical list rebuild the roster.

Movement, occupancy, effects, orders, regeneration, targeting, population checks
and bot decisions now traverse that active set. The wide-body occupancy path also
avoids an unnecessary historical scan when checking whether all bodies are small.
Place bonuses on ownership changes and hero recruitment history deliberately
continue to include historical records, preserving their existing state semantics.

| Historical casualties | Before CPU time | After | Before allocation | After |
| --- | ---: | ---: | ---: | ---: |
| 1,000 | 2,867 ms | 2,648 ms | 1,255 MiB | 1,090 MiB |
| 10,000 | 4,975 ms | 2,647 ms | 2,738 MiB | 1,090 MiB |

These runs cover 800 measured ticks after 100 warm-up ticks, using the same
imported rules as the earlier benchmark. With 10,000 casualties, measured CPU
time falls 47%, allocation falls 60%, and tick p95 falls from 11.66 to 4.11 ms.
All three before/after digests match. The 6,000-tick, 200-unit attrition soak also
matches the original final digest, with 61 replay checkpoints.

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -c Release -- --late-match-benchmark converted/skirmish-maps/Sandbox converted/prototype/late-match-profile.json
```

Evidence is in `converted/prototype/performance-121-*`. `-Profile` now reports
the active roster size alongside living and historical counts.

## Repeating the checks

```powershell
./prototype/run.ps1 -PerformanceSmoke
./prototype/run.ps1 -Map MP08_Rift -Observe -Bots Gimli,Aragorn,Legolas,Saruman -Speed 8 -Profile
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -c Release -- --retention-checks
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -c Release -- --army-benchmark converted/prototype/army-profile.json converted/skirmish-maps/Sandbox
```

Add `--allocation-profile` to the simulation benchmark to enable runtime allocation
sampling by type. It requires no external profiler installation. Sampled bytes are
approximate; `allocated_bytes` in each scenario is the exact thread allocation count.

`-Profile` prints every two game minutes. Compare living versus historical unit
counts, buried bodies, native nodes/resources, managed heap, private memory,
window allocation and Gen0/1/2 GC counts. `godot units`, `godot effects` and
`godot hud` separate presentation costs from the simulation. Heap and private
memory alone do not prove a leak: GC and native allocators can retain capacity.

## Remaining costs

Historical simulation records and the two lookup nodes still grow with casualties.
Simulation tick work now excludes settled corpses; presentation and some HUD
queries still traverse historical records and should be measured in rendered play.
Occupancy rasterization and combat planning still allocate, particularly during
congestion. Expensive movement retries and bot placement searches remain candidates
for further work. GPU skinning, shadows and off-screen animation need a rendered
large-map measurement; headless CPU timings do not cover them.

The standard observer smoke's Fate assertion fails in the original baseline and
the updated run. Both reach tick 22,621 with the same final digest; corpse-release
and the other observer checks pass.
