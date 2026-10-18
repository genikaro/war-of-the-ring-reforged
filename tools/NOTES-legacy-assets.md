# Legacy skeletons and meshes

Decoded from the local install's extracted files. Byte layouts and pose maths
live in `lsk.py` and `lms.py`; fixtures live in `test_asset_formats.py`.

## Skeleton v1.0

The difference from v2.0 is the header and a flattened child-index block.
All values are little endian:

| Offset | Value |
|---|---|
| 0x00 | `LIQD0LSK` (8 bytes) |
| 0x08 | f32 version, 1.0 |
| 0x0c | u32 joint count J |
| 0x10 | u32 child-index count N |
| 0x14 | u32 clip count C |
| 0x18 | u32 unknown, zero in all 83 files |
| 0x1c | u32[N] child joint indices |
| 0x1c + 4N | The same three J-by-vec4 bind arrays as v2.0 |

Joint descriptors and animation tracks then have the same layout as v2.0.
The joint descriptor's child count and child-list offset select a range of
the index block. Every selected child's parent must be that joint. Empty
lists and multiple roots are valid. The unknown header word is not interpreted.

Evidence: all 83 v1.0 files parse to the exact byte, covering 1,491 joints and
585 clips. All child lists agree with the parent fields. For all 247 skeletons
of both versions, accumulated bind offsets agree with the independently stored
model-space positions within 0.0001. Merry, Pippin and the Warg model's skin
weight files independently confirm those bind positions and joint counts.

`resolve_model.joint_names` now uses the parser for both versions, replacing
the legacy string search. The glTF exporter uses the existing pose maths.

## Mesh v1.0

The only file is `Meshes/Objects/Trees/M_SW_Grasslands_Tree_02.lms`. It uses
the v1.1 layout, including four reserved words before the UV channel counts
and a reserved word after each count. Adding v1.0 to the accepted versions
lets the existing index, triangle and usage-count checks apply unchanged.
The mesh contains 71 positions, 229 render vertices and 98 triangles.

## Export validation

- All 247 skeletons and 2,395 meshes parse successfully.
- Regenerated units: 83 of 85 export with animation and no warnings. Three
  GLBs changed: Merry, Pippin and the shared Warg model. Merry and Pippin each
  gained 19 clips; seven wildlife entries using Warg gained 12 clips each.
- `MODEL_GRASSLANDS_TREE_02` exports to a GLB with 98 triangles and one clip.
- The four new GLBs pass buffer/accessor bounds, finite-value, triangle-index,
  skin-weight, joint-index, animation-time and quaternion-length checks.
- The two remaining unit failures are unchanged: Shelob has no visible mesh,
  and Butterfly has no model record.

Reproduce the exports from `tools/`:

```bash
python lms2gltf.py units --out-dir ../converted/units
python lms2gltf.py model MODEL_GRASSLANDS_TREE_02 --out ../converted/objects/MODEL_GRASSLANDS_TREE_02.glb
python -m unittest discover -v
```
