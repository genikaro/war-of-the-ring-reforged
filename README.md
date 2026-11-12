# War of the Ring Reforged

A real-time strategy engine, written from scratch, that plays **The Lord of the Rings: War of
the Ring** (Liquid Entertainment, 2003) from your own installed copy of the game: a
deterministic integer simulation core and a Godot 4 C# front end, plus the Python tools that
read the original archives, database, models, maps, audio and text.

Unofficial, non-commercial fan project. It ships no game content and needs a legally obtained
installation of the original game — see [NOTICE.md](NOTICE.md).

**Status (build-189):** a playable skirmish and sandbox prototype — original maps, economy,
construction, research, heroes, abilities, places of power, bots, fog of war, a main menu,
setup screens and an exported Windows executable with a launcher. Not the original campaign,
and not a finished game. [docs/README.md](docs/README.md) is the build log and the detailed
setup and controls.

## Requirements

- Windows, for the PowerShell launch and export scripts.
- Your own installation of the original game.
- [Godot 4.7.2 .NET](https://godotengine.org/download/windows/) and the .NET 10 SDK
  (the project targets `net10.0`).
- Python 3.13 with Pillow and NumPy, for the import tools.

## Getting started

Import your installation (about 17 minutes for a full English install on 16 threads):

```powershell
python tools/import_install.py "C:/Games/War of the Ring"
```

Run the project from source, or open the launcher that the exported game opens on:

```powershell
./prototype/run.ps1
./prototype/run.ps1 -Launcher
```

Export a Windows executable to `build/WarOfTheRingReforged.exe` (once with
`-DownloadTemplates`, which fetches and verifies the official export templates):

```powershell
./prototype/export.ps1
```

Checks:

```powershell
dotnet run --project tests/simulation -c Release -- --cache converted/prototype
python -m pytest tools
```

## Repository layout

| Path | Contents |
| --- | --- |
| `prototype/` | Godot 4 C# project: simulation core, rendering, HUD, launcher, `run.ps1`, `export.ps1` |
| `tools/` | Python and JavaScript importers and analysis for the original game's formats |
| `tests/` | C# simulation, movement, combat and repeatability checks |
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
