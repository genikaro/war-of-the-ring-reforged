# Unit collision sizes — October 20, 2026

The owner reported visible clumping and asked whether original units have
different collision sizes. The source does contain different per-unit geometry.
The prototype now imports Main collision geometry for its three demo types and
uses it consistently for body contact, movement, formation and combat spacing,
passage clearance and click picking. Selection rings show the collision radius,
as requested by the owner; the original display-ring radius is retained as data.

## Where sizes live

`Units.lqr.Model` links to a `Models.lqr` resource and its `.lmd` file.
`BBoxes.lqr` names its box layers: Main collision (0), Selection (1), Visual (2),
Placement (3), Sub collision (4), Alt main (5), Oriented collision (6).
`lmd.Model.bounding_boxes` already decodes layer, joint, flag and 12 vector
components. Model ScaleX/Y/Z must be included; Uruk-hai uses 1.2 on all axes.

The existing static-obstacle adapter interprets these vectors as half-extents,
center and quaternion. Applying that convention to unit Main collision boxes
gives the horizontal half-widths below. All sampled Main boxes have equal XY
values and an identity quaternion. Some attach to skeleton joints, so these are
scaled local box measurements, not a recovered original runtime cylinder or
proof of how pathfinding, body contact or attack reach consume the geometry.

| Unit | Scaled Main horizontal half-width (m) |
|---|---:|
| Frodo | 0.460 |
| Aragorn | 0.561 |
| Gondor Swordsman | 0.570 |
| Uruk-hai | 0.693 |
| Elven Knight | 0.927 |
| Rider of Rohan | 0.882 |
| Warg Rider | 0.763 |
| Troll Bonecleaver | 1.181 |
| Huorn | 1.215 |
| Giant Ent | 2.244 |
| Balrog | 2.559 |

Local audit: `converted/prototype/unit-collision-audit.json` retains source IDs,
model paths/hashes, raw box values, full scale and derived half-extents. Original
and extracted inputs stay read-only. The larger roster measurements are an audit;
only the three demo types have runtime definitions and clearance profiles.

## Separate concepts

`Units.SelectionRingRadius` is explicitly described as the selected unit's
display ring radius in meters. It is 0.8 for all three current demo types and
many larger units, including the troll. It cannot replace the Main collision
geometry. SpotShadow dimensions and Visual boxes are also separate.

`PathingCategory` defines terrain slope/water/region rules, while
`MeshPathingCategory` defines allowed walkable-mesh flags. The sampled units,
including large ones, share Walker categories; neither table has a body-radius
column. Cloth-collision volumes are for clothing, not a unit pathing footprint.

## Implemented adapter

| Demo type | Radius (mm) | Height (mm) |
|---|---:|---:|
| Aragorn | 561 | 2095 |
| Elven Knight | 927 | 2534 |
| Uruk-hai | 693 | 2197 |

Rules format 3 retains the raw Main box, attached joint, full model scale and
input hashes. Sizes round outward to integer millimeters. The cylinder encloses
scaled XY half-extents plus offsets and the source Z height above the foot root.
Only unflagged, identity-rotation boxes attached to no joint or the locomotion
root `J_Position` are supported; animated/rotated alternatives fail import.

`prototype_clearance.py` derives per-body masks over the existing base graph:
terrain boundaries, swept-square mesh support, deck headroom and each removable
object's inflated node/edge masks. `--rules-only` refreshes these profiles and
their manifest hash without reconverting world geometry. Obstacle toggles update
all cached body graphs. Unsupported larger mesh bodies cannot fall back to the
base 350 mm clearance. Fixed graph samples remain conservative and can exclude
otherwise possible routes between samples.

Both radii determine pair contact, swept reservations and interpolated movement
safety. Formations use slots that fit their largest body. Imported bodies use
physical pair separation for combat slots and permission to strike; added comfort
margins trapped packed formations. [Engagement notes](../prototype/ENGAGEMENT.md)
cover the subsequent target distribution and performance corrections. Effective center reach is the greater
of the legacy 2 m and both radii plus the raw weapon range as a surface gap.
That reach interpretation and cylinder response remain prototype policies.

An 18-unit regression reproduced the owner's jitter/stalling report: avoidance
fell back to cell-center alignment that no longer fitted between larger bodies,
and early arrivals sealed inner formation slots. Wide-ground large units now
retain continuous avoidance and exchange blocked/crossing slots within the same
Move or Attack-move order. Marching units retain a passing side briefly and avoid
backward steering oscillations; presentation filters tiny heading corrections.
Attack-move has bounded encounters and regrouping rather than unlimited flank
pursuit. See [crowd movement policy](../prototype/MOVEMENT.md#crowded-arrival-and-attack-move-cohesion--october-20-2026).
Narrow routes retain constrained reservations. Checks cover combined
and redirected 18-unit moves, stable arrival, excess melee attackers, unequal
body sweeps, real-map arrivals, narrow doors, deck support and headroom.

Clicking a live enemy inspects its portrait, HP, type, Attack, Armor and Victories
with a red ring. Drag selection and commands remain friendly-only; inspection
cannot overwrite a friendly control group. Ray picking uses the nearest live
body and terrain/deck occlusion, including tall bodies away from their foot marker.
