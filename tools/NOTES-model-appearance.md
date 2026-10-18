# Applying model appearance

`lms2gltf.py` retains all resolved mesh parts and model attachments. It uses
`ModelClasses.DefaultAnimState` to select the initial state and the first
positive-weight choice for a deterministic preview. If the declared state is
absent, the first state with a positive-weight choice is the explicit fallback.
Models without choices keep their static attachment `shown` flags.

The animation choice's mesh selectors (`-1` means hidden) and model bitset
override static visibility. The first property-key flag enables a `shown`
change; other parameter-only keys do not change visibility. Visibility keys
use normalized clip time and STEP evaluation, including time-zero keys.
Evaluation starts from the choice mask on every frame, so seeking backwards,
switching states and loop wraparound cannot retain a previous visibility value.
Treebeard's mesh disappearance at 0.687 is handled by the same mechanism.

## Configuration tags and preview policy

Resource and attachment masks contain tags named by `ModelFlags.lqr`: the
record ID is the bit position. They are not animation-state IDs or a list of
requirements to AND together. For example Legolas's bow carries `WeaponPrimary`
and all four level tags; his flap has mask zero and is still visible in idle.

The preview chooses one gear level (1 by default) and disables weapon/critical
death blood tags unless requested. Resources with any level tag must include
the selected level; resources without level tags pass. Other tags are retained
without a guessed gate. This is an explicit inspection configuration, not an
implementation of every runtime flag operation (night/day, building upgrades,
random enable, battle gear, scripts, and so on). `--all-attachments` bypasses
model attachment visibility/configuration for debugging, while retaining state
selection for the model's own mesh slots.

The old most-common-bit, same-joint-alternate and `_IN_SCABBARD` filename rules
have been removed. Attachment `shown=False` no longer prevents export: Uruk-hai
arrows and missile bow, worker sacks and Shelob's body parts can be enabled by
their animation choices.

## GLB contract: `extras.wotrAppearance`, version 1

glTF 2.0 has no standard visibility animation. Hidden **mesh leaf nodes** are
disconnected from the default scene, while their mesh data, nodes, skeletons
and animations remain in the file. Generic glTF consumers therefore see the
default appearance. Zero-scale tricks are avoided because transforms on skinned
mesh nodes are not a reliable visibility mechanism.

Top-level appearance fields:

- `version`: currently 1.
- `flags`: `levels` (four masks, levels 1 through 4), `blood` (combined mask).
- `config`: `level`, `blood`, `allAttachments`.
- `models`: parent-before-child model instance records.
- `hiddenNodes`: `{node, parent}` bindings for disconnected mesh leaves;
  `parent: null` means the glTF scene root.

Each model instance contains:

- `model`, `declaredDefaultState` (database name), `defaultState` (effective ID).
- `parent`: parent instance index or null; `attachment`: its model-attachment
  slot in that parent, or null for the root.
- `parts`: glTF node indices in original mesh-slot order, null for missing parts.
- `partFlags`: original resource tags for each mesh slot.
- `attachments`: `{shown, flags}` in **model-only** attachment order; effect
  slots do not shift these indices.
- `states`: `{id, name, totalWeight, choices}`.
- Each choice: `clip` (exported animation name or null), `weight`, `meshes`
  (raw mesh selectors), `models` (enabled model-attachment indices),
  `meshTracks` and `modelTracks` (per-slot lists of `[normalizedTime, shown]`).

The viewer reattaches hidden mesh leaves through the GLTFLoader node bindings
and updates `visible` on every mesh, including skinned meshes stored at the
scene root. Parent attachment gates propagate through the model-instance
records, so detached skinned descendants cannot leak through hidden parents.
Skeleton nodes keep their original parents and transforms.

Static skeleton clips still export as constant animation channels. Tracks
ending before the clip's declared duration repeat their last value at the end.
This preserves the correct clock even for a clip with no bone motion or only
one pose key. Choice weights are shown but not sampled randomly in the viewer.

Nested models use their own default appearance at time zero. Their independent
animation playback, effects, cloth, property parameters other than visibility,
and damaged/corrupted texture swaps remain future tasks. An engine importer
must interpret this metadata to get dynamic gear; generic glTF clip playback
alone retains the default gear.

## Validation

- 84/85 units export, 69 unique models, zero warnings. The remaining missing
  model is `UNIT_WILD_BUTTERFLY`.
- Shelob exports 35 retained meshes, with 28 body/leg/head meshes visible in
  idle; egg and web attachments remain available for their other states.
- All unit GLB default scenes match the evaluated default appearance, contain
  visible geometry, and retain all referenced root-state skeleton clips.
- Browser checks: Aragorn sheathed idle vs drawn attack-ready; Uruk-hai arrow
  present at 0.5 and absent at 0.8; Shelob renders; Beast Lair healthy vs initial
  construction switches meshes. These are viewer checks; the Phase 1 gate's
  comparison against original in-game screenshots remains open.
- 42 Python tests and 6 Node tests cover parser/exporter regressions, default
  selection, time-zero keys, backwards seeks/loops, mesh disappearance, named
  configuration flags, hidden ancestors, static duration and GLB restoration.

```bash
python -B -m unittest discover -q -s tools
node --test tools/test-model-appearance.mjs
python -B tools/lms2gltf.py units --out-dir converted/units
```
