# Original console, control groups and explicit orders

Implemented Oct 19, 2026. This slice adds player orders to the accepted movement
and combat-spacing baseline. The three demo types now use imported definitions
and simulation-timed melee events; armor, range and event-scale interpretations
remain approximations documented in [combat notes](COMBAT.md).

## Player controls

Select units with a click, drag, or control group. The console now uses the
original Good faction atlas, portraits, command icons and targeting cursors,
decoded from source UVA rectangles. It preserves the 1024x212 artwork proportions
and includes a clickable terrain overview, portrait/health grid, control-group
strip, current orders and queue hints. Commands support mixed unit
selections. Active orders and targeting are highlighted; unit commands hide without
living friendly units. Dead selections and group members are pruned automatically.

Double-click or Ctrl-click a unit or portrait to select all living units of that
type on the same team, across the map. Shift-click adds/removes a unit. With
Shift and type selection, add the whole type; if all matching units are already
selected, remove that type. Shift-drag adds friendly units. Selection never mixes
opposing teams. The top-right overlay shows the build number, local date and
live FPS; build-1 introduces this numbering.

Click a live enemy to inspect its portrait, health, type, Attack, Armor and
Victories. Its ring and health bar are red; the action palette is empty while the global
Walk/Run switch stays available. Enemy inspection replaces a friendly selection.
Drag selection prioritizes friendly units; when none are selected by the box,
it can inspect a building or neutral structure. Picking intersects the nearest live body up to the terrain/deck
hit, rather than a fixed screen-space foot marker. Selection rings match each
unit's collision radius. Ctrl assignment while inspecting an enemy preserves
stored friendly groups; recalling a friendly group restores command controls.

Skirmish buildings and neutral interactables select separately from units.
Buildings show only their own available actions (currently Cancel for an owned
foundation), while neutral structures and inspected enemy/neutral units have
empty palettes. Source-selectable food wells, ore piles and the Place of Power
show original portraits; resources show remaining stock. See [skirmish selection](SKIRMISH.md#build-9-neutral-inspection-and-command-palettes).

A structure-only drag opens one inspector, choosing the visible structure whose
ground anchor is nearest the box center. The box can touch any visible part of
its mesh; the anchor itself need not be inside. Depth checks reject hidden mesh
surfaces. Shift-drag retains friendly units over structures and preserves a
structure selection when the added box is empty. Units and structures never mix.

The console scales uniformly with screen height, clamps to narrow windows and
stays centered on ultrawide screens. **Menu/F4 → Interface size** adjusts it from 75% to
150%; the setting persists in `user://interface.cfg`. Resize updates are live.
Text renders at the display resolution; original bitmap artwork remains at its
source resolution and is filtered when enlarged.

Control groups use **1–9 and 0**. Ctrl+number replaces that group with the current
selection. Shift+number (also Ctrl+Shift+number) adds the selection to the stored
group. An empty Ctrl assignment clears it. Number recalls surviving members;
a second tap within 350 ms centers the lead unit. Empty recalls leave selection
intact. Groups may overlap and only affect selection; orders still record unit
IDs. Groups 1–3 start with the demo squads and are fully editable. The strip
supports click recall. Portrait clicks select one unit; Shift-click removes it.

| Command | Shortcut | Behavior |
| --- | --- | --- |
| Move | M, then left-click | Move in formation. Ignore combat and damage en route. Return to Idle on arrival. |
| Attack | A, then left-click | Enemy: attack and pursue. Neutral/friendly: force attack. Ground: attack-move, engaging enemies and resuming the destination. |
| Stop | S | Cancel the current order, swing and queued route immediately, then resume default idle guarding from the next tick. |
| Hold Position | H | Stop at the current position and proactively attack within melee reach. Drop an out-of-range target; never chase or yield. |
| Guard | G, then friendly unit or terrain | Follow and defend a friendly unit, or defend a fixed location; return after an engagement. |
| Patrol | T, then terrain | Repeatedly travel between the starting point and destination. Engage enemies and resume the route. |
| Walk / Run | F2 or run icon beside minimap | Toggle global movement pace for your army at the next tick, including future units. Works with any selection; preserves orders and queues. |
| Greater Kingsfoil | Q or hero ability icon | With Aragorn selected, choose an injured ally or self. Heal up to 250 HP within 10 m; approach if needed. 17-second cooldown. |
| Hero slots 2–4 | W/E/R | Aragorn's W/E abilities are passive Fury/Immunity; R is empty. Other heroes are not connected. |

The visible action buttons occupy the **bottom row: Attack, Hold, Guard, Patrol**,
left to right. Move and Stop retain their controls without visible buttons.
Attack-move and force attack are target-dependent results of the one Attack/A
action; their separate buttons and F/B shortcuts have been removed. The Attack
button highlights for all three attack outcomes. Esc/right-click cancels targeting.

Shift-click or Shift+right-click queues orders behind unfinished actions. A
targeted Shift-click keeps targeting active for the next waypoint. Queued squad
destinations receive distinct formation slots. Stop, Hold Ground and unmodified
replacement orders clear the queue. A Shift move from Idle/Stop/Hold begins
immediately. Patrol and Guard are ongoing orders;
orders queued after them wait while that order is active. Replacement cancels
the waiting queue.

Right-click terrain and enemies retains contextual Move and Attack. During
targeting, Esc or right-click cancels targeting without
replacing the units' current orders. An invalid Attack click leaves targeting
active. Esc outside targeting clears selection. Selecting another squad also
cancels targeting. **Ctrl + right-click now issues Attack-move**, following the
addendum's default Force-Move behavior. **Ctrl + Alt + right-click** picks terrain
below bridges. Alt + right-click still toggles obstacles.

Arrow keys and middle-drag pan the camera; Menu buttons rotate and the wheel zooms.
Camera preset buttons remain in Menu; F1/F2/F3 and Q/E camera shortcuts are removed.
F4 opens/closes debug tools: live selection/orders, Pause/Resume, a paused
single-tick step, encounter restart, camera tools and interface sizing. The
controls guide is collapsed by default; last-action feedback is separate from
live state. Opening the menu does not pause the simulation. Reserving A/S/H for
orders replaces the old WASD camera and H help bindings. The camera shifts the scene upward while the HUD is visible so
the encounter stays above the command panel. F10 hides the entire HUD.

## Simulation contract

`SimCommand.Kind` and `Queued` record the order alongside tick, sequence, team,
sorted unique unit IDs, cell and target. Buttons, hotkeys and contextual clicks
use the same `OrderInput` targeting rules and `SimWorld.Enqueue` path. No UI
callback moves units or resolves combat. Paused commands remain queued for the
next fixed tick after resuming; multiple orders on that tick apply in sequence.
The unified Attack input resolves to Attack, ForceAttack or AttackMove in the
recorded log according to its clicked target. Allied and neutral targets share
force-attack dispatch; negative team IDs represent neutral units in picking.

`SimUnit.Order`, `OrderDestination` and `OrderTarget` represent player intent.
`OrderQueue`, patrol endpoints/direction and the guard origin retain additional
intent. `Target`, `Goal`, `Path`, and attack-position fields remain temporary execution
state. Attack-move preserves its per-unit destination while combat replaces the
approach route. Explicit replacement clears the suspended destination. The
state digest includes intent as well as the execution state.

Dead, invalid and foreign unit IDs are ignored. A dead, invalid or friendly
Attack target leaves the current order intact. Unreachable Move/Attack-move
orders end cleanly. An explicit Attack with no reachable approach waits until
the target or navigation changes or the player replaces the order.

Idle player units automatically guard their resting position: detect connected
enemies within 10 m, and support allies fighting within 10 m against a target up to
12 m from the guard origin. Pursuit stops beyond 12 m, then the unit returns near
its resting position (or settles nearby if that position is occupied). Move
completion establishes the new guard origin. This requires no Guard command;
the displayed order stays Idle. The red demo opponent retains its aggressive AI.
Move suppresses retaliation while travelling. Stop returns to Idle and restores
default guarding, while Hold Ground acquires enemies within reach
automatically. Attack and Attack-move acquire connected enemies within the
10,000 mm prototype awareness radius. Imported units also share the active target
of allies within 6 m. Selection favors available target capacity, then distance
with a load penalty and stable unit IDs. Crowded Attack targets may redirect
overflow attackers to other reachable enemies while preserving the primary
order target; queued orders advance when that primary target dies. Explicit
Force Attack targets and Hold/Guard limits remain intact. Melee reach is the greater of 2,000 mm and both body
radii plus the source weapon range, with terrain/layer checks. This gap policy
does not establish the original range formula.
Imported stats and animation-timed melee are described in [combat notes](COMBAT.md).
Large-body Attack-move adds the [movement policy](MOVEMENT.md#crowded-arrival-and-attack-move-cohesion--october-20-2026)
limits: an 8 m encounter, 3 m lateral corridor and 2 m rear allowance. Units
release fleeing targets, regroup and resume their destination. Wide-ground
encounters preserve the order's original direction instead of drifting toward
successive flank targets. Completion clears retaliation-driven roaming.
Replacing an order cancels pending hits while preserving remaining recovery;
queuing an order preserves the current swing. Health bars use each type's source
maximum. Single selection shows the original type icon, Attack, Armor and
Victories beside the portrait and health bar. The type icon's tooltip lists
the four damage percentages. Group portraits retain these stats in tooltips.

Stop cancels movement at the current position and resumes idle guarding; Hold
Ground preserves the exact position until replaced. On a constrained
deck segment the physical reservation stays held while stationary. A later
movement order resumes through the existing safe segment coordination. Stopped
and holding units do not yield for passing traffic, so they can block a passage.

## Original references and deliberate differences

The local [reference library](../references/original-game/README.md) contains
the original manual and addendum.

The [reference audit](../references/original-game/REFERENCE-AUDIT.md) excludes
prerelease/modified sources. Retained gameplay images show the four standard
commands along the bottom with abilities above, but do not establish executable
provenance. The manual's labeled interface diagram is documentation only and
does not establish exact final icon placement. Use release assets and controlled
captures for final UI validation.

The following were checked before selecting labels and shortcuts:

- Manual PDF page 21 (printed 38–39): A for Attack Move and H for Hold Ground.
- Manual PDF pages 5 and 21: Ctrl+1–0 group assignment, number recall, and
  double-tap centering on the lead unit. Shift assignment is an added shortcut.
- Addendum PDF pages 3–4: S is a Walk/Run modifier; Ctrl changes the default
  movement type between Force-Move and Attack-Move. These correct the manual.
- Addendum PDF page 6: Action Panel entries include Force Attack, Hold Position,
  Guard, Patrol and Harvest. The page layout was rendered and visually checked.
- Owner's exported `Hotkeys.lqr`: `HOTKEY_HOLD_GROUND` (91) links to DI_KEY_H;
  `HOTKEY_FORCEATTACKMOVE_MODIFIER` (106) links to DI_KEY_A;
  `HOTKEY_WALKRUN_MODIFIER` (105) links to DI_KEY_S.
- Exported `Controls.lqr` does not establish a matching unit-command layout.
  Source interface assets include Good/Evil `console_*_action_forceattack`,
  `standground`, `stop`, `guard` and `patrol` UVAs and AttackMove cursors.

The console now uses those original assets, with a new group strip and the four
bottom-row action buttons requested by the user. The source lookup, crop and hashes are recorded in
`converted/prototype/interface/index.json`; `prepare_prototype.py --ui-only`
refreshes an existing cache, and full preparation includes it automatically.
All images remain in ignored cache data.

M/S bindings remain prototype choices: the original uses S as a walk/run
modifier. A is a targeting shortcut here, where the original also supports a
modifier. Shift+number assignment, the new group strip and centered responsive
layout are additions. The player-facing label is now Hold Position, matching the
addendum and local tooltip; the internal enum remains HoldGround. Guard uses prototype 6 m threat and 8 m pursuit leashes and
replans following twice per second; original guard ranges are unverified.
The minimap is a simplified navigation/height overview without fog of war;
economy/resource bars, abilities, original fonts, animated Palantir/alerts and
full original binding/layout fidelity remain subsequent work.

## Planned corrections from the local original-game session

The [30-capture session](../references/original-game/LOCAL-SESSION-2026-10-19.md)
confirms the bottom command row and captures its tooltips. It does not validate
the current command algorithms. The [roadmap](../tools/Roadmap.md#next-milestone-original-game-command-and-combat-corrections)
puts a focused comparison/correction pass before ranged combat. The controls
above describe the implementation as it stands. The
[behavior matrix](../references/original-game/COMMAND-BEHAVIOR-MATRIX.md)
tracks implemented corrections and unresolved comparisons.

- **Implemented:** Hold Position is the player-facing label. Measure
  acquisition/pursuit rather than inferring them from the tooltip.
- Compare original **Force Attack** targeting with contextual attacks and A/Ctrl
  movement modifiers before revising unified Attack/A. Record modifier press
  and release, cancellation and the configured default movement mode.
- **User-selected bindings: Q/W/E/R for abilities 1–4, F2 for walk/run. F1–F4
  will not activate abilities.** Keep S Stop as a documented prototype
  convenience. Q/E rotation and F1/F2/F3 camera shortcuts are now removed;
  camera buttons, launch-time views and the F4 menu remain available.
  Reserve R for ability 4 rather than a future enter-building command. These
  intentional differences do not change the recorded original S/F1–F4 bindings.
  **F2 is implemented** as a persistent player-wide Walk/Run toggle and has a
  matching run-icon button. Source animation ratios determine walking speed;
  default running retains 3 m/s. No camera shortcut uses F2. Q/W/E/R are
  assigned by source ability slot. Build-2 connects Aragorn's Q Kingsfoil; his
  W/E slots show passive Fury/Immunity and R is empty. See [ability details](ABILITIES.md).
- Verify Guard for a friendly unit, building and terrain area, plus Patrol
  interruption/return and Hold-versus-idle behavior. Existing 6 m/8 m Guard
  leashes and automatic target policies remain unverified.
- Preserve the infantry/worker/building selection distinction: statistics for
  infantry, construction strip for workers, recruitment and purple queue progress
  for buildings. Evil and hero/ability panels still need collection.

Acceptance requires an original/prototype behavior matrix, evidenced corrections
and focused deterministic input/replay checks. Continuous recordings or traces
are required for timing and pursuit claims; JPEG stills establish visible UI
intent and sampled state only. The label and chosen F2 binding are implemented;
the original timing and behavior comparison gate remains open.

## Validation

- 341 synthetic checks: crowded arrivals, attack-move cohesion and facing, army engagement/distribution, force/hold/queue preservation,
  variable-body crowd movement, narrow passages, ray picking,
  original regressions, timed melee, type ratings, kills, walk/run, contextual Attack/neutral target
  checks, order/group checks and responsive
  layout checks through 4K, ultrawide and narrow windows.
- 56 additional real-cache checks retain map navigation, traffic and spawn safety,
  and cover imported definitions and four repeatable duels.
- `-CommandSmoke`: 49 actual Godot panel/input checks, including enemy/neutral inspection and empty palettes,
  ownership/control-group protection, single-unit
  stats, live victory refresh, return to the group roster and the four-button
  bottom-row layout and working hidden Move/Stop controls; report in
  `converted/prototype/command-smoke-report.json`.
- `-OrdersSmoke`: 27 additional actual input checks for groups 1–0, additive
  assignment, double-tap centering, death pruning, HUD adjustment, queued squad
  waypoints, Guard, Patrol, Force Attack and contextual Attack-move, plus debug
  menu open/close, pause/resume and exactly one paused tick with animations frozen.
  F2/run-icon checks cover mixed selection, active targeting, original walk/run
  clips, unchanged camera, paused double toggles and empty selection.
- Existing combat and 80 FPS motion smoke checks retain real input, damage,
  all three friendly squads fighting, spacing and interpolation checks.
- `-EngagementSmoke` drag-selects all 18 allies and attacks through actual input,
  then checks participation on both sides, body separation and step timings over
  800 ticks. See [the benchmark and acceptance criteria](ENGAGEMENT.md).
- `-CommandSmoke -CapturePath converted/prototype/command-panel.png` saves a
  rendered preview and companion camera/import metadata. It uses long enemy
  cooldowns to isolate input checks; normal launches retain the existing AI.
- `-OrdersSmoke -Resolution 3840x2160 -CapturePath converted/prototype/hud-4k.png`
  renders an exact offscreen 4K preview, even if the desktop limits window size.

Reports, reference renders and the preview remain in ignored `converted/`.
Original-game behavior fidelity has not been established by these regressions.
