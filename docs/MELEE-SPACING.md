# Build-71: room to fight

Allies fighting other units previously used the same half-envelope spacing as allies passing
one another. Rear ranks could fill the gaps between fighters, merging a large
battle into overlapping bodies. Imported units now leave at least 85% of their
combined body radii between their centres during combat. A unit whose physical
clearance is larger keeps that clearance. This is a prototype presentation and
movement policy, not a recovered original-game rule.

Attack spots, waiting positions, local approach steps, route searches and route
smoothing agree on that distance. Otherwise a route or shortcut would keep aiming
through gaps the local follower refuses. Already compressed groups can move apart;
the existing rule allows steps that increase their separation. Normal travel still
uses compressed allied cores. Enemy collision, terrain clearance, weapon reach,
damage timing, Hold Position and the six-searches-per-tick budget are unchanged.

The same durable scenarios were sampled every ten ticks from ticks 400 to 999.
The values below are the nearest ally's distance divided by the two body radii
added together. A value of 1 means the model envelopes just touch.

| Scenario | Build-70 median | Build-71 median | Build-71 10th percentile |
|---|---:|---:|---:|
| 18 vs 18, mixed body sizes | 0.57 | 0.98 | 0.87 |
| 64 vs 64 melee | 0.73 | 0.88 | 0.86 |
| 18 attackers around one held target | 0.72 | 1.10 | 1.02 |

All 36 soldiers in the smaller battle land hits. The durable 64-vs-64 fixture has
58 contributors rather than 81 with compressed spacing: there are fewer attack
slots along an immortal front. Rear ranks still advance and hit after the front
is removed. The normal-health mixed-army check includes all 72 soldiers and
finishes the battle. Against the held enemy block, flanking adds 13 contributors
(2 without flanking, 15 with it); a sealed corridor admits none. Static-front
regression expectations now reflect the lower slot density while retaining
progress, oscillation, casualty replacement and flank-gain requirements.

`MeleeSpacingChecks` covers these three scenarios plus three soldiers starting
inside the new comfort distance. It checks visible separation, participation,
the search cap, a fixed Hold anchor and deterministic replay. The existing
collision/interpolation, squeeze-through, commands, ranged and army checks remain.

```powershell
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release -- --melee-spacing-benchmark converted/melee-spacing-build71.json
.\prototype\run.ps1 -AttackMoveEngagementSmoke
```

The real-map attack-move smoke passes with all 36 soldiers landing hits and no
body collisions. All 1,103 simulation checks pass with the Helm's Deep cache,
including terrain traversal and opposing squads passing on its walkways. The Godot
formation smoke also passes movement, engagement, arrival and collision checks.
All 18 seeded stress scenarios pass (96,000 primary ticks plus deterministic
replays, up to 400 units per side), including narrow passages, replacement
commands, changing obstacles and queued orders. Results are in
`converted/build71-soak.json`.
Captures are `converted/melee-spacing-build70.png` and
`converted/melee-spacing-build71.png`. Restart to load build-71; no cache rebuild.

## Build-72: ranged groups

The same combat spacing applies to missile units. A new packed-group check exposed
an approach problem: only 7 of 18 tightly packed archers fired, with 61% of the
later samples still crowded. All adjacent route nodes could be occupied, leaving
the interior unable to reach a free firing position.

Ranged units already within reach now make local steps that reduce the group's
crowding before planning a route. They remain in firing range and keep normal
swept body checks, terrain clearance and attack-move limits. A 20 mm settling
margin prevents integer rounding from leaving a unit just short of enough room to
shoot. Steps use the unit's own movement budget and deterministic integer math.
Hold Position stays fixed. Moving to make room uses normal windup cancellation
and preserves recovery and released projectiles.

Missile approaches can use compressed allied passing space, allowing shorter-range
throwers to pass archers. Firing endpoints and the permission to shoot still use
the wider combat spacing. Ranged units engaged in melee use the existing melee
approach and spacing rules.

`RangedSpacingChecks` covers packed archers, packed axethrowers, mixed ranks,
a 64-unit group, distant approaches, replay, movement interruption and Hold
anchors. Every unit fires in all open-ground scenarios, with no crowded samples
after settling. Source Elven Archer and Dwarven Axethrower profiles also pass.
Checks include projectile release timing, interpolated body clearance and the
shared six-search cap.

```powershell
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release -- --ranged-spacing-checks --cache converted/skirmish
```

Validation passes all 1,141 simulation checks with the Helm's Deep cache and all
84 focused ranged checks with the skirmish content. Four seeded 100-vs-100 mixed
army runs (24,000 primary ticks plus replays) pass attrition, prolonged combat,
replacement orders and changing obstacles; reports are in
`converted/build72-soak-100.json`. The Godot combat smoke passes projectile timing,
weapon switches, facing and body collision. The attack-move engagement smoke also
passes with all 36 soldiers landing hits. The formation smoke keeps its arrival,
survivor, width and collision requirements, with up to ten extra seconds for a
final ranged engagement. It completes at tick 2,437 (1.85 seconds past the previous
deadline), with all 18 allies idle at their destination. The current capture is
`converted/ranged-spacing-build72.png`. Restart to load build-72; no cache rebuild.

## Build-73: building attacks

The owner confirmed that unit fights looked better, but melee sieges still
clumped. Building combat had separate reservations using compressed cores, local
steering skipped the spacing check when neither unit had a unit target, and a
fighter stopped and swung as soon as it reached the building.

Melee siege reservations, routes and shortcuts now use the same combat spacing.
Attackers need both footprint reach and room beside allies before swinging. The
local settling step also applies within a building's attack band, allowing an
already packed line to spread without leaving weapon reach. Ranged attackers
against buildings use the same settling and firing-space checks.

When the perimeter is full, reserves keep their order, wait and retry. A separate
terrain-only check still distinguishes an unreachable building. The selection
readout says "Waiting for room at Hall" (or the building's name). Opening a gap
admits reserves; replacement commands and destruction release the waiting state.
A small building consequently supports fewer simultaneous attackers, with the
rest in reserve. Weapon reach, damage and attack timing retain their normal rules.

The benchmark samples active siege orders every ten ticks from ticks 400–999.
"Crowded" means the nearest ally is within 80% of their combined body radii.

| Siege fixture | Build-72 crowded samples | Build-73 crowded samples | Nearest-ally distance, 10th percentile, before → after |
|---|---:|---:|---:|
| 48 melee, large footprint | 29.2% | 0% | 0.56 → 0.88 |
| 64 melee, small footprint | 81.5% | 0% | 0.51 → 0.86 |
| 6 melee already packed at the wall | 100% | 0% | 0.61 → 0.86 |
| 18 ranged already packed within reach | 100% | 0% | 0.65 → 0.85 |

The large-footprint fixture has 46 of 48 contributors; the small footprint has 14
fighters with all 64 orders retained. Previously only 27 of those 64 retained
their siege order after available spots filled. Additional checks cover mixed
body radii (570/927/1,200 mm), attack-move, replay, physical and interpolated
clearance, casualty replacement, Move, destruction, waiting text, sealed terrain
and the six-search cap. Long approaches at 6–100 m and terrain detours still pass.

```powershell
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release -- --siege-spacing-checks
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release -- --siege-spacing-benchmark converted/siege-spacing-build73.json
.\prototype\run.ps1 -SiegeSmoke
.\prototype\run.ps1 -SandboxSmoke
```

Validation: all 1,172 simulation checks pass with the Helm's Deep cache, and the
84 focused ranged checks pass with skirmish content. Native siege, Sandbox
long-distance building attack and AI assault/razing smokes pass. Benchmark reports
are `converted/siege-spacing-build72.json` and `converted/siege-spacing-build73.json`.
Restart to load build-73; no cache rebuild.
