# WotR asset tools (proof of concept)

Python 3.13 + Pillow. Run from this folder.

| Script | Purpose |
|---|---|
| `h2o.py` | List/extract Liquid Entertainment `.H2O` archives (v6). Format notes in the module docstring. |
| `blast.py` | PKWARE DCL "implode" decompressor used by H2O. |
| `lms.py` | `.lms` mesh parser (v1.1 and v2.0). Format notes in the module docstring. |
| `lsk.py` | `.lsk` skeleton + animation clip parser (v2.0). Format and pose maths in the docstring. |
| `lwt.py` | `.lwt` skin weight parser. |
| `lqr.py`, `lmd.py`, `resolve_model.py` | Database tables, `.lmd` models, and name to file resolution. See `NOTES-database.md`. |
| `lms2gltf.py` | Export to `.glb`: one unit by name, all units, or hand-picked files. |
| `viewer.html` | three.js viewer with unit and clip pickers (`?clip=Name`, `?t=0.5` freezes a frame). |

Extract the archives once:

```bash
python h2o.py extract "../TLOTR - War of the Ring/Archives" ../extracted --verify
```

Export one unit (or `MODEL_...` name) with its textures, skeleton, all clips and attachments:

```bash
python lms2gltf.py model UNIT_G_HERO_ARAGORN --out ../converted/units/MODEL_G_ARAGORN.glb
```

Export every unit plus `index.json` for the viewer:

```bash
python lms2gltf.py units --out-dir ../converted/units
```

Add `--all-attachments` to either command to include the attachments the game hides
by default. The old manual mode is `lms2gltf.py files` (see its `--help`).

Viewer: serve the repo root (`.claude/launch.json` config `viewer`) and open
`http://localhost:8765/tools/viewer.html` (config `viewer-alt` serves on 8767 when 8765 is taken).

## Status

- H2O: all 23 archives extract, every CRC verified. Stored entries are plain files
  (`.dds` textures, Ogg Vorbis speech/music, WAV effects, Bink `.bik` movies).
- LMS: 2394 of 2395 meshes parse (one v1.0 tree is left).
- LSK: all 164 v2.0 skeletons parse to the exact byte. 83 v1.0 files (mostly static
  buildings/objects) are not handled, so Merry and Pippin export static.
- LWT: all 162 weight files parse. glTF keeps the top 4 influences per vertex.
- Database: all 2108 models resolve to existing files.
- Units: 83 of 85 export (69 unique models, 26 MB). Shelob's model has no mesh of
  its own (only hidden egg attachments); `UNIT_WILD_BUTTERFLY` has no model record.
- Spot-checked in the viewer: Aragorn, Legolas, Gandalf, Uruk-hai, Elf Knight,
  Troll Bonecleaver, Rohan Rider, Witch-king, Frodo, Balrog.

## Conventions worth knowing

- Model scales in `Models.lqr` are absolute: attachments get the ratio to their parent.
- Rigid attachments are authored in their joint's bind orientation (the `.lsk`
  third array), so each joint gets a "frame" node with that rotation.
- A mesh is skinned only if its own flags say so; some heads list weights but are rigid.
- Default attachment visibility is inferred: the `.lmd` "shown" flag, the most common
  state bit (drops blood overlays, keeps upgrade level 1), and `*_IN_SCABBARD` hidden
  when the drawn weapon is attached. The game's own per-clip show/hide rules are not
  decoded yet.
- Team colour (`Materials/Team Color.lmt`, D3D9 fixed-function stages) is additive and
  unlit: `texture * lighting + mask * teamColour`, and the diffuse is black under the
  mask. Exported as glTF emissive: `emissiveTexture` = `*_Mask.dds`, `emissiveFactor` =
  `TeamColors.lqr` red (48 of 69 unit models). Recolour by setting the material's
  emissive colour; the viewer has a picker (`?team=Blue`).
- `.lmt` materials (v2.0, 624 bytes; read by hand, no parser yet): `LIQDMATL`, f32 version,
  17 floats of `D3DMATERIAL9` at 0x0c, then 4 stage blocks of 0x7c bytes from 0x64:
  i32 editor texture id (-1 none), COLOROP, ALPHAOP, COLORARG1, COLORARG2, COLORARG0,
  ALPHAARG1, ALPHAARG2, ALPHAARG0 (D3D enum values), then address, filter and a 3x3
  texture transform. v1.0 (528 bytes) and v3.0 (2372 bytes) differ and are not decoded.

## Not yet handled

v1.0 skeletons,
per-clip attachment visibility, animation events, effects (`.lfx`), terrain/levels.
