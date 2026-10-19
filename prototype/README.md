# Godot C# skirmish sandbox

First Phase 3 milestone. Loads the local Helm's Deep GLB, Aragorn, five Elf
Knights and six Uruk-hai. Select friendly units, issue move/attack orders and
fight a simple opponent. The engine choice was approved on Oct 19, 2026.

The next milestone is reliable squad navigation through gates, bridges and
elevated walkways, including obstacle footprints and congestion replanning.
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
| Right-click an enemy | Attack, approaching when outside melee range |
| WASD / middle-drag | Pan the camera |
| Q / E | Rotate |
| Mouse wheel | Zoom |
| Space | Pause/resume simulation and unit animation |
| Esc | Clear selection |
| Restart sandbox | Reset the encounter |

## Verification

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj
./prototype/run.ps1 -Smoke
```

The simulation runner has no Godot or test-package dependency. It checks
blocked routes, unreachable areas, adjacent-cell paths, deterministic ties,
terrain interpolation, repeatable command playback, movement, combat,
ownership and non-overlapping units. The headless Godot smoke test loads
the actual map and twelve models, dispatches mouse/key events through the
viewport, and checks selection, move/attack picking, zoom, pause/resume,
animation and damage over 420 fixed ticks. Its report is
`converted/prototype/smoke-report.json`.

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
Auxiliary ground masks, obstacle footprint updates, portal/bridge entrances,
walkable mesh elevation and the two invalid source bridge matrices still need
navigation integration. Congested units can wait; full local avoidance and
replanning are future work.

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
