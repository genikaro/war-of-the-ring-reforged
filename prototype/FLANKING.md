# Build-68: local melee flanking and building approaches

Rear melee ranks now try a reachable side approach when their normal attack route
has settled into a queue. They consider free attack positions around their current
target and up to eight other military units in the same nearby enemy group. One
bounded search chooses both the route and the target. The target changes only when
that search finds an actual attack position, not another waiting point.

Attack-move may temporarily take an extra 8 m around its original encounter area
for that chosen route, for at most 12 seconds. The route is kept while it makes
progress, rather than replaced on every local steering correction. The allowance
ends on contact, a replacement command or combat release. A unit makes one committed
attempt before waiting again if it cannot connect; attacking, a target dying or a new
command permits another attempt. Failed searches can retry as the crowd opens.
Hold/guard limits, forced targets, queued orders and the existing siege/worker threat
policy remain authoritative. Explicit attacks retain their existing nearby-opponent
behavior; the extra attack-move pursuit allowance is not applied to them.

For buildings, melee attack spots are selected around the whole footprint using a
route that accounts for the bodies between the unit and the spot. Planned spots
reserve room for a body. Route smoothing respects those blockers, and the ordinary
movement replan cannot replace the chosen detour with a straight route through the
front rank. If the approaches are temporarily sealed, units wait and retry.

Unit and building approach searches share the existing fair budget of six searches
per tick. Ordinary searches close at most 1,024 nodes; a flank may close 2,048.
The new route state is included in deterministic digests. Physical body clearance,
terrain collision, interpolation and source movement/attack speeds are unchanged.

## Validation

`FlankingChecks` uses a held friendly line in front of an enemy formation three
ranks deep. With flank attempts disabled, one of 36 rear units connects; with them
enabled, 21 connect. With both sides sealed by terrain, none cross the blockers.
The front rank keeps its Hold Position anchors. Against a large rectangular
building, rear contributors increased from 26 to 34 of 36.

The 64-vs-64 queue scenario has 81 combat participants versus 48 before. Because
productive flanks now walk, the traffic checks distinguish total movement and
turns from movement that fails to advance 400 mm over a 20-tick window. They still
reject repeated unproductive walking: this run has zero walking loops, two stalled
reversals and 71 stalled moving samples. Collision and interpolated clearance are
checked throughout the new scenarios, together with replay, command interruption,
destroyed buildings and the shared search cap.

Final validation passed all 1,064 simulation checks, all 18 seeded extended combat
runs (up to 400 units per side), and the Godot siege, attack-move engagement and AI
smoke tests. Extended results are in `converted/build68-soak.json`.

```powershell
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release -- --flanking-checks
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release -- --flanking-benchmark converted/flanking.json
```

Flanking is local and capacity limited. A sealed passage or a completely occupied
enemy perimeter still produces a waiting rank; the algorithm does not guarantee
that every unit can attack simultaneously or coordinate an army-wide encirclement.

## Build-69: building attacks from a distance

The local 1,024-node flood introduced a regression: a melee unit could keep retrying
the same search without moving when the building was farther away than that flood
could reach (reproduced at 24 m). Long approaches and terrain detours now use a
directed A* search to the available attack perimeter, with the existing bounded
32,768-node long-route budget. Nearby clear approaches still use the local crowd
search, and both share the six-searches-per-tick allowance. The directed heuristic
is a lower bound to the rectangle containing the destination nodes; preferred/
fallback combat searches retain their original expansion order.

`SiegeDistanceChecks` covers 6, 14, 24, 48 and 100 m starts, a far terrain detour,
a nearby building requiring a long detour, deterministic replay, and a 24-unit
forced attack on an owned building. The sandbox Godot smoke also issues the actual
Attack command from over 24 m and checks acceptance, prompt movement and damage.
