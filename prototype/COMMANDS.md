# Original console, control groups and explicit orders

Implemented Oct 19, 2026. This slice adds player orders to the accepted movement
and combat-spacing baseline. Health, damage, range and hit cadence still use
the existing prototype values; importing combat definitions is subsequent work.

## Player controls

Select units with a click, drag, or control group. The console now uses the
original Good faction atlas, portraits, command icons and targeting cursors,
decoded from source UVA rectangles. It preserves the 1024x212 artwork proportions
and includes a clickable terrain overview, portrait/health grid, control-group
strip, current orders and queue hints. Commands support mixed unit
selections. Active orders and targeting are highlighted; commands disable without
living friendly units. Dead selections and group members are pruned automatically.

The console scales uniformly with screen height, clamps to narrow windows and
stays centered on ultrawide screens. **Menu/F4 → HUD size** adjusts it from 75% to
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
| Stop | S | Clear movement, target, combat position and suspended destination. Return blows if attacked within reach; never chase. |
| Hold Ground | H | Stop at the current position and proactively attack within melee reach. Drop an out-of-range target; never chase or yield. |
| Guard | G, then friendly unit or terrain | Follow and defend a friendly unit, or defend a fixed location; return after an engagement. |
| Patrol | T, then terrain | Repeatedly travel between the starting point and destination. Engage enemies and resume the route. |

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

Arrow keys and middle-drag pan the camera; Q/E rotate and the wheel zooms.
F4 opens controls and HUD sizing. Reserving A/S/H for orders replaces the old WASD camera and H
help bindings. The camera shifts the scene upward while the HUD is visible so
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

Idle blue units may retaliate when struck; idle red units acquire nearby
enemies. Move suppresses retaliation while travelling. Stop only retaliates
within reach when struck, while Hold Ground acquires enemies within reach
automatically. Attack and Attack-move acquire connected enemies within the
existing 10,000 mm prototype awareness radius, with distance then unit ID as
deterministic tie breakers. Melee reach remains the existing 2,000 mm prototype
rule with terrain/layer checks. These values do not establish original rules.
Imported stats and animation-timed melee are described in [combat notes](COMBAT.md).
Replacing an order cancels pending hits while preserving remaining recovery;
queuing an order preserves the current swing. Health bars use each type's source
maximum; portrait tooltips show imported armor, DPS and damage classification.

Stop and Hold Ground preserve the exact current position. On a constrained
deck segment the physical reservation stays held while stationary. A later
movement order resumes through the existing safe segment coordination. Stopped
and holding units do not yield for passing traffic, so they can block a passage.

## Original references and deliberate differences

The local [reference library](../references/original-game/README.md) contains
the original manual and addendum. The following were checked before selecting
labels and shortcuts:

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
layout are additions. Hold Ground follows the manual's label; the addendum calls
it Hold Position. Guard uses prototype 6 m threat and 8 m pursuit leashes and
replans following twice per second; original guard ranges are unverified.
The minimap is a simplified navigation/height overview without fog of war;
economy/resource bars, abilities, original fonts, animated Palantir/alerts and
full original binding/layout fidelity remain subsequent work.

## Validation

- 243 synthetic checks: original regressions, timed melee, contextual Attack/neutral target
  checks, order/group checks and responsive
  layout checks through 4K, ultrawide and narrow windows.
- 31 additional real-cache checks retain map navigation, traffic and spawn safety,
  and cover imported definitions and four repeatable duels.
- `-CommandSmoke`: 23 actual Godot panel/input checks, including the four-button
  bottom-row layout and working hidden Move/Stop controls; report in
  `converted/prototype/command-smoke-report.json`.
- `-OrdersSmoke`: 17 additional actual input checks for groups 1–0, additive
  assignment, double-tap centering, death pruning, HUD adjustment, queued squad
  waypoints, Guard, Patrol, Force Attack and contextual Attack-move.
- Existing combat and 80 FPS motion smoke checks retain real input, damage,
  all three friendly squads fighting, spacing and interpolation checks.
- `-CommandSmoke -CapturePath converted/prototype/command-panel.png` saves a
  rendered preview and companion camera/import metadata. It uses long enemy
  cooldowns to isolate input checks; normal launches retain the existing AI.
- `-OrdersSmoke -Resolution 3840x2160 -CapturePath converted/prototype/hud-4k.png`
  renders an exact offscreen 4K preview, even if the desktop limits window size.

Reports, reference renders and the preview remain in ignored `converted/`.
Original-game behavior fidelity has not been established by these regressions.
