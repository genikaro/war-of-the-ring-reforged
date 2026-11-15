# Notices

## What this is

War of the Ring Reforged is an unofficial, non-commercial fan reimplementation of the
real-time strategy game **The Lord of the Rings: War of the Ring** (Liquid Entertainment /
Sierra Entertainment, 2003). It contains no game content of its own: it reads a copy of the
original game that you own and installed yourself.

## Not affiliated

This project is not affiliated with, authorized by, endorsed or sponsored by Liquid
Entertainment, Sierra Entertainment, Vivendi Games, Activision Blizzard, Electronic Arts,
Middle-earth Enterprises, Embracer Group, the Tolkien Estate, or any of their subsidiaries
or successors.

*The Lord of the Rings*, *War of the Ring*, Middle-earth and all related names, characters
and marks belong to their respective owners. They appear here only to say which game this
software works with, not as a claim to any right in them.

## No game content in this repository

- Nothing from the original game is distributed here: no art, text, sound, models, maps,
  campaign or database files.
- You need your own legally obtained installation of the original game. The launcher asks
  for its folder, and the game reads it in place on your machine; nothing is copied out of it
  or written into it.
- Release builds hold only the engine. Everything the developer tools produce stays local and
  is excluded from version control: the install itself, `extracted/`, `converted/`,
  `prototype/Assets/`, `build/` and `reports/`.
- `references/` holds the owner's own research: captures, observations, data dumps and
  third-party reference material about the original game. It is local-only and excluded
  from version control as well.

## What the license covers

[`LICENSE`](LICENSE) (GNU General Public License v3.0 or later) applies to the original
work in this repository: the Godot C# project in `prototype/`, the import and analysis tools
in `tools/`, the checks in `tests/`, the build scripts, and the documentation in `docs/`.

It does not, and cannot, apply to:

- the original game's data, text, art, sound, or anything the engine or the tools derive from it;
- field names, descriptions, numbers and other facts quoted from the original game where
  they appear in `docs/` for documentation and interoperability — those stay with their
  owners;
- third-party reference material (the game's manual, guides, screenshots), which this
  repository does not redistribute.

## Interoperability

The engine and the tools read the original game's own file formats so that data you already
own can be played by this engine. Those formats were worked out from files on the owner's own
installation, for interoperability and preservation. No original executable code is copied,
modified or redistributed here.

## Takedown

If you hold rights in the original game or in Middle-earth and consider anything here to go
too far, open an issue on this repository and it will be removed.
