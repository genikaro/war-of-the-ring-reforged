# Main menu, setup screens, loading screen and the game executable (build-147)

The game now starts like a game: a main menu over the original's 3D menu scene, a
setup screen for Skirmish and another for the Sandbox, a loading screen while a
map loads, and a Windows executable that opens on the menu.

## Run

```powershell
python tools/frontend_assets.py      # once: menu art, font and menu scene into converted/frontend
./prototype/export.ps1               # builds build/WarOfTheRingReforged.exe
./build/WarOfTheRingReforged.exe     # or double-click it
```

`./prototype/run.ps1` with no scene-choosing parameters (or `-Menu`) also opens the
main menu. `-View`, `-CapturePath` and the other parameters that chose the Helm's
Deep scene before still do; `-Setup`, `-Skirmish` and `-Sandbox` work as before.

## Main menu

Laid out from the original `Screens/Menu_Main.lsr` (a `LIQDSCRN` of control records
with normalized rectangles on a 1024 × 768 screen; control ids from the `Controls`
table, art from `UVAs`, text ids into `Text/Menu.ltx`):

| Control | Rectangle | Art |
|---|---|---|
| `CONTROL_MAIN_MENU_WOTR_LOGO` | 0.504, −0.026, 0.557 × 0.386 | `lotr_wotr_logo` |
| five option buttons | x 0.649, 0.285 × 0.0925, from y 0.358 every 0.0925 | `parchment_button_04, 01, 02, 03, 02` |
| `..._OPTION_BULLET_LEFT/RIGHT` | x 0.588 / 0.939, 0.0505 × 0.026, 0.031 below each button's top | `menu_bullet_left/right` |

The original's items were Single Player (Menu text 1), Multiplayer (2), Load (20),
Settings (3) and Exit (8006). Here they are **Campaign** (disabled: the campaigns are
not built), **Skirmish**, **Sandbox**, **Settings** and **Exit**, in dark ink on the
parchment, dark red under the pointer with the gold bullets either side. The menu
keeps the 1024 × 768 layout at the window's height against its right edge, so a wide
window shows more of the scene. Settings opens the in-game Settings page on its own
(OK, Cancel or Esc returns). Exit asks "Confirm exit from the game." (Menu text 3100)
in the original `console_dialog_quit_acknowledgement_g_box`; Esc opens and closes it.

**Build-222:** the labels are the install's own where the original has them, Skirmish (Menu text 12), Options (3),
Quit (8006), the exit question (3100), OK (9015) and Cancel (9000), and catalog rows (`mainmenu.*`) for Campaign,
Sandbox and the tooltips, so a Russian install shows Сражение, Настройка and Выход. The **Sandbox** is a testing
field: it shows only in a debug session (`-Debug`, `-DebugMenu`, smokes and captures, `DebugSession.Enabled`), and
otherwise the items below it move up a row. The loading screen's steps are catalog rows too (`loading.*`).
Build-223 does the same for the setup screens and the in-game menu with its Settings page (`setup.*`, `menu.*`,
`settings.*`); see the build log for the original Menu text each label takes.

**Lettering.** `Fonts/Immortal.lfn` is a `LIQDFONT`: float 1.0, glyph count (160),
size count (4), then per size a header (size, 0, space width, letter spacing, page
width, page height, layers), BGRA pixels for every layer, and 160 glyphs of (code,
advance, u0, v0, u1, v1). A code above 0xFFFF names its layer in the high word
(the 12 px size spills onto a second layer). Sizes are 12, 16, 20 and 24 px of white
glyphs on alpha, Latin and Cyrillic, no space glyph (the header gives its width).
`FrontEndArt.Immortal(px)` builds a Godot bitmap `FontFile` from the size nearest
above and scales it; Godot's default font fills in characters it lacks (·, ×, …).

**Background.** `Levels/MenuScene.wor` ("ModifiedMenu2") is the original menu world:
Barad-dûr (`MODEL_BARAD_DUR_MAINMENU`, with the Eye) behind an army of 44 goblin
spearmen, 7 orc slashers, 6 Uruk-Hai and 8 bonecleaver trolls among 18 flag spears
and six volcanoes on a 128 m Mordor map, with one camera waypoint (`CamWayPt 6`,
source 50.06, 1.80 at 10.4 m). It exports like any map (`wor2gltf.export`). The export
keeps rest poses, so `MenuScene` swaps each soldier for the content pack's animated
model of the same name (Godot numbers repeated node names: `MODEL_E_GOBLIN_SPEARMAN7`)
idling from a random point in the Evil red. The sky is a panorama made from
`SKYBOX_MORDOR_01` (the four identical sides and the top, sampled bilinearly).

Adapters, not decoded from the source: the waypoint's two angles (58.4°, 90.8°) are
not understood, so the camera stands 8 m behind and 3.5 m above it and looks north
over the army to the tower with a 46° field of view, turned so the tower sits at 36%
of the window's width, swaying slowly; the 128 m map ends in a bare edge that this
low view keeps behind the volcanoes. Lighting, haze and the flicker of the fire
lights on the tower's fire pits, torches and Eye and the volcanoes' lava (bone
attachments on the skinned models) are the prototype's own. The flag spears keep
their rest pose.

## Setup screens

Both use the original game-creation screen `Screens/MENU_SKIRMISH.lsr`: the stone
`Game_Creation_BG` frame with the title at the top left (0.025, 0.177), the player
table across the middle (header at y 0.256, rows from 0.293 one every 0.0349 between
thin rules, x 0.067–0.935), the map list at the bottom left (0.03, 0.6225, 0.306 ×
0.329), the minimap beside it (0.344, 0.6225, 0.151 × 0.191) with the map's name and
size under it, the game settings at the bottom right under the gold rule
(`control_golden_panel_thin`, 0.548, 0.665), and the gold-edged `control_yellowbox`
Back and Start boxes (0.653 and 0.809, 0.908). Drop-downs are the black
`control_yellowbox_long` with the gold `control_arrowbutton`; check boxes are
`control_checkbox_*`. The frame keeps its 4:3 shape with dark stone beside it.

- **Skirmish** lists the 32 multiplayer maps (the Sandbox is no longer among them).
  Each row: player and colour, controller (You, Closed, a bot), side (Good / Evil / Random,
  build-161: drawn from the match seed at load, kept by Restart),
  traits, and the bot in a few words (difficulty and style). The bot card and the
  start speed sit under Game settings. Behaviour is the build-39/49/51/133 lobby's.
- **Sandbox** shows the Sandbox field, both start positions yours with a side each,
  the start speed, and two choices: *Instant building and training* (the build-56
  cheat, still on by default; F4 still toggles it) and *Training dummies* (the
  build-59 dummies by the blue base). They travel in `SkirmishSetup.SandboxInstantBuild`
  / `SandboxTrainingDummies`.

Back returns to the main menu (Quit when the screen was opened on its own with
`--setup`); Esc does the same from the menu. A match's Setup button returns to the
screen it came from; Exit to Main Menu in the in-game menu returns to the main menu
from any match (it was disabled outside setup matches before).

## Loading screen

After the original `Menu_Loading_Screen.lsr`: the parchment with the logo
(`menu_loading_screen_background`) and the One Ring as the progress bar
(`CONTROL_MENU_LOADING_BAR`, 0.3335, 0.6989, 0.333 × 0.2109): the plain ring
(`menu_loading_screen_bar_background`) turns into the glowing inscribed ring
(`menu_loading_screen_bar_fill`) from left to right. The map's name sits between the
logo and the ring and the step under way under it (the original shows neither). It
covers every match load and the menu scene's.

Loading used to run in `_Ready` in one go, freezing the window. `LoadSandboxAsync`
now runs the same steps in the same order but yields a frame between them, and runs
the pure parts off the main thread (manifest, cache check, rules, navigation grid,
path-region warm-up). Nothing simulates, draws the HUD or takes input until it ends;
play starts on a physics frame, so the first physics step still precedes the first
drawn frame, as before. Each load prints `LOADED <map> in <ms>: <step> <ms>, ...`.

The freeze was mostly the cache check: `CacheManifest.Verify` hashed the pack's 3,900
files (428 MB) one by one, 19 s per load. It now hashes in parallel and remembers
each file found matching with its size and write time
(`%LOCALAPPDATA%\WarOfTheRingReforged\verified-files.json`), so an unchanged file is
not hashed again: Sandbox now loads in about 3 s, the menu scene in 0.3 s.

## Executable

`prototype/export.ps1` exports the Godot project with the official Godot 4.7.2 .NET
export templates (`-DownloadTemplates` fetches them once from
github.com/godotengine/godot-builds, about 1.2 GB, and checks the release's SHA-512;
they live in ignored `converted/tooling/appdata/Godot/export_templates`, and the
script points `APPDATA` there for the export only). It writes
`build/WarOfTheRingReforged.exe` (release, project packed in) and
`build/data_Reforged_windows_x86_64` (the game's assemblies and the .NET runtime).
`prototype/Reforged.sln` is the solution Godot's .NET export requires;
`prototype/export_presets.cfg` holds the Windows preset.

The owner's game data is never packed. `GamePaths` finds `converted/` beside the
executable or in a folder above it, so the build in the repository's `build/` uses
the repository's data; `--data=<folder>` names another. Settings stay in the same
`user://` folder as development runs. Re-run `export.ps1` after code changes.

## Verification

`./prototype/run.ps1 -MenuSmoke` (17 checks; add `-CapturePath <png> -Resolution
1600x900` for the menu, both setup screens and the loading screen): the menu over the
loaded scene with its five items, bullets on hover, the Exit question, Settings and
back, both setup screens and Back, a Sandbox start (Player 2 Evil, no dummies, no
instant building) loading behind the ring over 20 steps with the game still until it
ends, the choices reaching the match, and Exit to Main Menu. The same smoke passes on
the exported executable (`build/WarOfTheRingReforged.exe --fixed-fps 20 -- --menu-smoke`).
`-SetupSmoke` (27 checks, the Sandbox check moved to the menu smoke), `-SandboxSmoke`,
`-SkirmishSmoke` and `-CommandSmoke` pass; `python -m unittest test_frontend_assets`
covers the font decoder and the sky panorama.
