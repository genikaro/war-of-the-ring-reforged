# Presentation asset decoding

Phase 1 now has sequential decoders and JSON export for the five remaining
asset families. Every file in these families is consumed to EOF. This covers
the serialized structure and known resource links; unnamed parameters remain
indexed raw words. Full particle/cloth simulation and visual parity with the
original game are not established by parsing.

## Coverage and commands

Run from `tools/`, using Python + Pillow:

```bash
python presentation_assets.py export --out-dir ../converted/presentation
python presentation_assets.py inspect "../extracted/Projectiles/UrukHaiArrow.lpo"
python lms2gltf.py projectiles --out-dir ../converted/projectiles
python lms2gltf.py units --out-dir ../converted/units
python lms2gltf.py buildings --out-dir ../converted/buildings
python verify_phase1.py
```

| Family | Files | Result |
|---|---:|---|
| Effects | 405 | Six track/emitter arrays, keys, embedded UVAs, model/texture references |
| Projectiles | 37 | Versions 1, 2, 5, 6; control/resource/motion keys, behavior, actions/events |
| UVAs | 1027 | 28 v1, 140 v2, 856 v3, 3 legacy filename descriptors |
| TextureAnimations | 2 | Two 64-frame texture-ID sequences, frame intervals 75/50 ms |
| Cloths | 36 | 8 v100, 20 v102, 8 v103; particles, springs, pins, UVA stages/material |

JSON documents contain `format_version: 1`, `kind`, `source`, `asset` and
`references`. Known references retain table, sparse ID, field path, symbolic
name, resolved file and existence status. Fixed records expose indexed
`words` after their version word: byte offset `4 + 4*i`. Undefined heap
padding stays integer data, never NaN JSON numbers. The exporter checks
counts, versions, strings, truncation and EOF. Output inside source assets,
the database or original installation is rejected. Output is deterministic.

The index records all files, parse failures and missing references. The
presentation exporter exits nonzero on parse errors **or missing references**.
This install reports zero parse errors and two stale UI texture references
(below). The Phase 1 audit reports source gaps separately from conversion
errors and never claims that screenshot review occurred.

## Executable evidence

Layouts were recovered from the files and shipped `Rings.exe`, SHA-256
`7d9e30a76f08614de4c6c9cdbcd10769c73ecc87af04c4376c1709fa080b435a`.
Disassembly packages are research tools, not importer dependencies. Neither
the executable nor converted proprietary assets are committed.

| Address | Reader/behavior |
|---|---|
| `0x655e71` | LIQDUVAD v1/v2/v3 fixed records |
| `0x64a33e`, `0x64aab1` | UVA timing, playback and frame UV calculation |
| `0x656cba` | LIQDTANM header and texture-ID array |
| `0x656459` | LIQDEFTD header and six arrays |
| `0x656f47`, `0x6570d4`, `0x6571c1`, `0x65734b` | Four effect track headers |
| `0x65786b`, `0x657dfa`, `0x65816b` | Common effect track, transform track and keys |
| `0x657475`, `0x6574d2`, `0x657b2c` | Particle/mesh emitters and keys |
| `0x6578fc`, `0x6579ce`, `0x657a11`, `0x657a77` | Visual, trail, beam and auxiliary keys |
| `0x65d1cc` | Projectile version dispatch |
| `0x65d2ec`, `0x65d659`, `0x65e0ac`, `0x65e41d` | Shipped projectile versions 1, 2, 5, 6 |
| `0x66245e`, `0x66253c`, `0x6627bc` | Control, resource and motion keys |
| `0x5b315c` | Cloth version dispatch |
| `0x5b33b6`, `0x5b364c`, `0x5b3887` | Cloth counts and new/legacy particles |
| `0x5b3401`, `0x5b353c`, `0x5b3ae6` | Springs, configuration and pins |
| `0x5eb75f`, `0x5ed830` | LMD cloth collision shapes and groups |

## Layouts

Effects start with `LIQDEFTD`, f32 version 1, then ten words: duration,
start, padded loop byte, two emitter counts, four track counts, parameter.
The four track arrays precede particle and mesh emitters. The corpus has
1321 visual tracks, 37 trails, 37 beams, 2 auxiliary tracks, 1109 particle
emitters and 7 mesh emitters. Each track includes a version-dependent header,
a 148-byte common record (fixed UTF-16 name, duration/delay, flags, sixteen
bindings), and a transform track. Transform headers are 56 bytes; keys are
144 bytes in v1 or 152 in v1.01. The four other key sizes are 52, 28, 64 and
80 bytes. Visual kind 0 embeds a complete UVA; kind 1 selects a **Models**
record (debris, ice, globs, spell heads), not a texture-animation ID. Beam
keys also embed UVAs. All known resource links in effects resolve. Emitter
headers/keys are 96/140 bytes; particles add 40-byte keys and mesh emitters
add another transform track. `track_flags` preserves version-specific bytes
separately from common flags.

Modern UVAs contain `LIQDUVAD`, f32 version, then an 80-byte body (v1) or
84-byte body (v2/v3): texture ID, frame count, frames per row, frame interval,
parameter, `(u, v, width, height)`, eleven parameter words and newer flags.
File offsets 64/72/80/88 control start, playback, tiling and primitive modes.
Eight frames with four per row means eight frames, not a 32-frame atlas.
The three legacy descriptors have a 52-byte record, LQR UTF-16 filename and
trailing word. LTAs contain `LIQDTANM`, f32 version 1, frame count, frame
interval in milliseconds, mode and signed texture IDs. `sequence_texture`
provides deterministic looping lookup in seconds. Arbitrary UVA playback
modes and scrolling are retained for later engine work; cloth previews use
the initial rectangle.

Projectiles contain `LIQDPROJ`, version, duration, three timed key arrays,
version-specific behavior, actions and events. Resource kind 0 refers to
Models; kind 1 refers to Effects. Unknown motion/action enums and parameters
stay raw. `lms2gltf.py projectiles` writes 37 JSON definitions and 33 GLBs
with `extras.wotrProjectile`. Four definitions have no mesh preview: Ember
is an effect, Lightbringer bolt and dispel are a skeleton with an effect,
and Haradrim poison dart declares no visual resource. Those cases are
explicit data-only entries. GLBs preview the first model; they do not
simulate trajectory or impact.

The bounded Elven Archer runtime adapter now interprets launch event 7 with
eight-byte payload `(46, SoundEvents ID)` as a launch sound. It preserves unknown
payloads and opcodes. Elven arrows select sound event 172; ordinary arrows 43,
fire arrows 125, and ballista arrows 151 use the same layout. Resource bindings
on Elven/ordinary arrows reference Effects 12 (`Test_Arrow_Trail.lfx`), while
fire and ballista bindings reference their matching effects 91 and 382. The
Elven adapter imports that trail's 250 ms track, width and packed color keys;
full runtime effect support remains open. See docs/COMBAT.md for the
explicit ribbon, blending and sound scheduling policies.

Cloth begins with an integer version. V100 stores counts, 83-byte particles,
20-byte springs, a 112-byte configuration plus pins, and two legacy texture
words. V102/v103 move configuration/pins first, use 79-byte particles, and
end in eight **UVA** IDs plus a material ID. Particles retain position,
velocity, force, normal, mass, flags, packed color and legacy neighbors.
Grid dimensions are u16s at configuration offsets 96/98. Particle counts
and spring endpoints are checked. Initial grid GLBs have separate diffuse
and mask atlas UVs, double-sided materials, attachment transforms, team
color and `node.extras.wotrCloth` physics records. Decoded visibility masks
and timed keys work in Python/JavaScript, including seeks and parent gates.
There is no wind/collision solver.

The LMD blocks previously called cloth simulation records are named cloth
collision volumes (sphere, box, cylinder, capsule), then indexed groups.
All 2108 LMDs now reach their event tables: 109 cloth attachments, 114
collision volumes, 38 groups and 1938 events. The resolver exposes these;
GLBs retain collisions, effect visibility and clip event metadata. The final
unrelated LMD block remains outside the decoder.

## Verification and remaining gate

```bash
python -m unittest discover -v
node --test test-model-appearance.mjs
```

Synthetic tests cover five asset families, shipped version variants, six
effect arrays, embedded UVAs, LMD collisions followed by events, cloth
visibility/parent gates, truncation, extra bytes, safe output and deterministic
export. Zero normals in Qamara's knife and a few building meshes are repaired
from adjacent faces, with an up-axis fallback for isolated degenerate vertices.

Optional independent validation from the repository root:

```bash
npm install --prefix converted/validation gltf-validator --no-save
node tools/validate-glb.mjs converted/validation/node_modules/gltf-validator
python tools/verify_phase1.py
```

All 142 unique generated GLBs (69 units, 40 buildings, 33 projectiles) pass
Khronos validation: zero errors and warnings. The validator saves content
hashes; the audit rejects stale validation results. Viewer spot checks cover
Orc Mound healthy/ruins with blue team color and cloth on its animated pole.
Captures in `converted/` are viewer evidence, not original reference images.

Source gaps remain explicit:

- `UNIT_WILD_BUTTERFLY` has no Models record; 84/85 units export.
- `console_spellsportrait_summonbalrog.uva` selects texture 803, whose shipped
  `Elven_Worker.dds` file is absent.
- `mission_objectives_frame.uva` selects deleted texture record 940.
- Dark Foundry has two ore parts whose named attach joint is absent from
  its skeleton; the existing exporter warning remains visible.

The roadmap requires original in-game screenshot comparisons for every unit
and building. On Oct 19 the project owner deferred that review until after
Phase 2, when they will capture reference screenshots. The implementation is
complete; the audit records conversion checks as passed and visual review
as deferred, without claiming that the visual gate has passed.
Particle rendering, cloth simulation and full unnamed-property semantics
are not claimed complete.
