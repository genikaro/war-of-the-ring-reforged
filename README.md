# War of the Ring Reforged

A real-time strategy engine, written from scratch, that plays **The Lord of the Rings: War of
the Ring** (Liquid Entertainment, 2003) from your own installed copy of the game: a
deterministic integer simulation core and a Godot 4 C# front end, plus the Python tools that
read the original archives, database, models, maps, audio and text.

Unofficial, non-commercial fan project. It ships no game content and needs a legally obtained
installation of the original game — see [NOTICE.md](NOTICE.md).

**Status (build-214):** a playable skirmish and sandbox prototype — original maps, economy,
construction, research, heroes, abilities, places of power, bots, fog of war, a main menu,
setup screens and an exported Windows executable with a launcher. Not the original campaign,
and not a finished game. [docs/README.md](docs/README.md) is the build log and the detailed
setup and controls.

## Requirements

- Windows, for the PowerShell launch and export scripts.
- Your own installation of the original game.
- [Godot 4.7.2 .NET](https://godotengine.org/download/windows/) and the .NET 10 SDK
  (the project targets `net10.0`).
- Python 3.13 with Pillow and NumPy, only for the developer tools and their format checks (not to play).

## Getting started

The launcher is how Reforged is played: it finds your copy of the game, checks it and starts the main menu. Reforged
reads the original game in place as it plays; nothing is imported or copied, and the install is only read.

1. Build the game once. The first time, `-DownloadTemplates` fetches Godot's official export templates (about
   1.2 GB) and checks their SHA-512:

   ```powershell
   ./prototype/export.ps1 -DownloadTemplates
   ```

2. Start `build/WarOfTheRingReforged.exe`. The launcher opens with your War of the Ring folder already filled in if
   the game is in one of the usual install folders. If it isn't, click **Browse** and pick the folder that holds
   `Database` and `Archives`. The launcher then checks that the install reads correctly (a second or two).
3. Click **Start Reforged**. Next time the launcher remembers the folder, so Start is ready at once.

### Development

`run.ps1` runs the project from source. With no parameters it opens the main menu; `-Launcher` opens the
launcher. The smokes and captures are listed in [docs/README.md](docs/README.md):

```powershell
./prototype/run.ps1
./prototype/run.ps1 -Launcher
./prototype/run.ps1 -Install "C:/Games/War of the Ring"
```

The game reads everything straight from the install (builds 192-214): text, rules, models, audio, pictures, the maps
with their navigation and scenes, and the menu art. The install is the one `-Install` names, else the folder the
launcher kept. `-Data <folder>` runs on another data folder; it may be empty. Without an install the game falls back
to the developer tools' caches in `converted/`, where they exist.

Checks:

```powershell
dotnet run --project tests/simulation -c Release -- --cache converted/prototype
dotnet run --project tests/formats -c Release -- --install "C:/Games/War of the Ring"
python -m pytest tools
```

## Repository layout

| Path | Contents |
| --- | --- |
| `prototype/` | Godot 4 C# project: simulation core, rendering, HUD, launcher, readers for the original formats (`Formats/`), `run.ps1`, `export.ps1` |
| `tools/` | Python and JavaScript importers and analysis for the original game's formats |
| `tests/` | C# simulation, movement, combat and repeatability checks, and checks for the C# format readers |
| `docs/` | All project documentation, starting with [docs/README.md](docs/README.md) |
| `LICENSE`, `NOTICE.md` | GPL-3.0-or-later, and what it does and does not cover |

Local only, never committed: the original installation, `extracted/`, `converted/`,
`prototype/Assets/`, `build/`, `artifacts/`, and `references/` (original-game research notes
and captures).

## Documentation

Everything is in [`docs/`](docs/): the [build log, setup and
controls](docs/README.md), the [roadmap](docs/ROADMAP.md), the [asset
tools](docs/TOOLS.md) with their format notes (`docs/NOTES-*.md`), and one document per
system — [skirmish](docs/SKIRMISH.md), [combat](docs/COMBAT.md),
[commands](docs/COMMANDS.md), [movement](docs/MOVEMENT.md),
[abilities](docs/ABILITIES.md), [menus](docs/MENUS.md),
[localization](docs/LOCALIZATION.md), [performance](docs/PERFORMANCE.md) and the rest.
Documents still link to `references/`, the local research library, which is not part of the
repository.

## License

GNU General Public License v3.0 or later, for the code and documentation written for this
project. See [LICENSE](LICENSE) and, for the original game's content and marks,
[NOTICE.md](NOTICE.md).

## Before this repository is made public

- Rewrite the history. `references/` is untracked from build-189 on, but earlier commits
  still carry the research library, including data dumped from the original game. Run
  `git filter-repo --invert-paths --path references` and force-push before anyone else
  clones it.
- Review what `docs/` quotes from the original game: descriptions, label text and stat
  tables taken from the install are documentation of someone else's content.
- Keep it non-commercial — no sales, donations or ads — and keep the engine useless without
  the buyer's own copy of the game.
- Decide on the name. Using the game's title is honest about what the project does, but a
  distinct title with "for The Lord of the Rings: War of the Ring (2003)" underneath carries
  less trademark risk.
- Add contribution terms if pull requests are accepted, so later relicensing stays possible.
- Push `main` only. This checkout also holds `refs/codex/*` checkpoints and old `claude/*`
  working branches.
