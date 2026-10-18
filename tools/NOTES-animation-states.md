# Model animation states

The `.lmd` state block is decoded in `lmd_states.py`, consumed by `lmd.py`,
and annotated with database state names and skeleton clip names by
`resolve_model.py`. It is distinct from the resource slots' configuration
bitmasks; both kinds of data feed the exporter/viewer appearance controller
described in `NOTES-model-appearance.md`.

## Evidence and executable readers

The binary layouts were checked against all extracted models and the shipped
32-bit `Rings.exe`. Executable SHA-256:
`7d9e30a76f08614de4c6c9cdbcd10769c73ecc87af04c4376c1709fa080b435a`.
Addresses below belong to that build:

| Address | Reader |
|---|---|
| `0x5ed556` | State header and weighted choice list |
| `0x5e749b` | Choice versions 1 and 2 |
| `0x5e7af3`, `0x5e7b3e` | Choice array sizes, skeleton clip index and arrays |
| `0x5e7bc6` | Three visibility bitsets; a fourth in choice v2 |
| `0x5e7c0a` to `0x5e8437` | Optional property tracks and additional event keys |
| `0x5f8d57`, `0x5f8dfb`, `0x5f8e80` | Model, effect and mesh/cloth property keys |
| `0x5ecd08` | Dense state-ID lookup following the state records |

The Python tools need only their usual dependencies. The disassembler used
for research is not an importer dependency, and no executable is distributed.

## State and choice layouts

All fields are little endian. Each of header count `[0]` states contains:

```text
u32 0x64, u32 state_id, u32 total_weight, u32 choice_count, u32 flags
choice_count choices
```

Each choice begins:

```text
u32 version (1 or 2), u32 weight
u32 mesh_count, u32 event_index_count, u32 secondary_event_index_count
i32 skeleton_clip_index (-1 for no clip in newer static models)
i32[mesh_count] per-mesh selectors
u32[event_index_count] event indices
u32[secondary_event_index_count] secondary event indices
visibility bitsets for models, effects, cloths
choice v2 only: fourth bitset, then optional property tracks and event keys
```

The primary skeleton's clip array supplies the clip names. A mesh selector
of `-1` disables that mesh. Other values range beyond the model's material
count, so they are retained as `mesh_selections` rather than material indices.
Their exact role remains open.

A visibility bitset stores `u32 population, u32 size`, then `ceil(size/32)`
u32 words. Set bits identify enabled attachments within that kind's ordered
list. These masks are independent of the attachment's default `shown` flag
and its configuration mask. The decoder checks population and unused bits.

After all state records comes `u32 lookup_size, i32[lookup_size]`: the array
index is a state ID, its value is the state record index, and `-1` is absent.
This explains the 404-byte block seen after unit state lists: 100 entries
plus the count. The parser validates this map and reads attachments directly
from the next byte.

## Choice v2 property tracks

Each optional track group starts with a u8 presence flag. When present, each
target has `u32 key_count`, then keys with an f32 time and a typed payload.
Groups occur in this order:

1. Mesh properties, one target per mesh.
2. Fourth-bitset properties, retained as auxiliary tracks.
3. Model attachment properties, one target per model bitset element.
4. Effect attachment properties, one target per effect bitset element.
5. Cloth properties, one target per cloth bitset element.
6. Two additional event-key groups, each with its own count. The second
   retains raw flag/type/value arrays; its gameplay meaning remains open.

The key records contain nested versioned booleans and property structures.
The decoder exposes `shown`, raw flags, secondary flags and raw parameter
values. Model keys consume 56 bytes after the time; effect keys 38 bytes;
mesh/cloth keys 50 bytes. Unknown property flags are preserved. Event index
lists are decoded, but their trailing model-level payloads and gameplay
callbacks remain a separate investigation.

## Concrete checks

- All 2108 model files decode: 5576 states, 5998 choices, 411 joint sets and
  4788 joint bounds. Every state's choice weights sum to its declared total.
- Every mesh selector list matches the mesh count. Model/effect/cloth bitset
  sizes match header counts. Every referenced primary skeleton clip is valid.
- Aragorn idle selects attached models `[1, 2, 4]` (head, scabbard, sword in
  scabbard); attack-ready selects `[0, 1, 2, 3]` (blood overlay, head, scabbard,
  drawn sword). Configuration flags still gate the blood overlay.
- Uruk-hai missile attack enables `[3, 4, 6]` (head, quiver, missile bow).
  Arrow keys toggle hidden/shown/hidden/hidden at `0`, `0.285`, `0.683`, `1`.
- Beast Lair construction 00 enables mesh 0; healthy, damaged and badly
  damaged enable meshes 3, 4 and 5 respectively.
- At the parser milestone, all mesh/material/texture/attachment records matched
  the previous parser: 83 animated units, no warnings and byte-identical GLBs.
  The following appearance milestone applies these states and exports 84 units,
  including Shelob; see `NOTES-model-appearance.md`.
- Synthetic regressions cover both choice versions, sparse IDs, weighted
  choices, all property groups, corrupt/truncated data and a meshless skeleton
  whose joint indices contain misleading state-marker byte patterns.

Inspect from `tools/`:

```bash
python resolve_model.py UNIT_G_HERO_ARAGORN --json --no-recurse
python resolve_model.py MODEL_E_BEAST_LAIR --json --no-recurse
python -m unittest discover -v
```
