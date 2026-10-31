# Places of Power — build-128

**Build-132 balance update:** Steed of Rohan, Radagast's Aviary and Enchanting
Willow are excluded from normal skirmishes. Random markers and explicit map
placements of these three resolve to seeded replacements from the other
**12** random-eligible landmarks. Map objective positions and counts are retained.
Their source models and powers remain available to the developer gallery.

**Monument of Narsil now grants heroes +25% attack damage**, for both melee and
projectile attacks, instead of granting the army armor. The attack HUD includes
this multiplier. This is the owner's balance change; the original +1 armor
record is preserved under `source_rule` in the export. New heroes receive the
buff, and ownership changes transfer it immediately.

Build-132 validation: five Python export tests, 148 combined Fate/tech/defense/place
checks, 18 fixed-hit damage checks and 45 hero-experience checks passed. The
14-check Godot Places smoke passed, as did map-policy smokes on Sorrow (explicit
Aviary) and Sandbar (explicit Willow), confirming their allowed replacements.

Random map markers become named landmarks with their original models. The match
seed and object ID choose a complete source object; the title, model, radius,
active power and all default bonuses follow it. The Orb of Hasty Summoning
carries its Fate, food **and** ore discounts. The old adapter chose one of 19
individual modifier rows, retained the Orc Mask placeholder and prefixed the
title with “Random Place of Power”. Restart keeps the seed and landmarks.
The new object-based pool is an adapter choice; the original executable's random
algorithm has not been verified. Two eligible alternate rows (Orc running speed
and Fountain cooldown) have no dedicated object and are not separate random
landmarks; their simulation support remains available to authored rules.

Capture requires one team alone within the control radius. A contested zone
retains its owner and says “contested” in the panel. Leaving keeps ownership;
enemy capture transfers army bonuses immediately. Later recruits receive them.

Implemented modifiers include armor, regeneration, maximum health, attack,
melee/ranged damage, siege damage, cooldown, Fate gain, hero XP, cavalry/Orc
running speed, sight, recruitment costs and attack against listed victim types
(Mallorn of Galadriel). Cost multipliers affect recruitment food and ore;
additive discounts follow multiplication and prices floor at zero. Fate has its
own additive discount. Source prices remain unchanged. Queues record actual
payments, so cancellation refunds that payment after any ownership changes.
Buttons show current prices; bots use the same discounts. Discount composition
and integer rounding are prototype policies, not recovered executable arithmetic.
Sight affects the existing detector rules; terrain fog is not implemented.

In the developer gallery, select an owned **Enchanting Willow** or **Radagast's Aviary**, click its power
button or press **Q**, then choose ground. Esc/right-click cancels targeting.
Original icons and cursors accompany the radius preview and recharge tooltip.
Both powers recharge for 90 seconds on the landmark; capture does not reset it.
Bots can use their owned powers.

| Place | Effect |
|---|---|
| Enchanting Willow | Enemies in 15 m move and attack at 50% speed for 15 seconds. Choose ground within 30 m of the Willow. |
| Radagast's Aviary | Scout a 25 m area for 15 seconds. The current visibility adapter reveals camouflage/cloak there; the One Ring stays hidden. |

Willow parameters come from its spell graph's child spell, time-scale action
and timer. Aviary's original action reveals fog of war; revealing concealed
units is an explicit prototype adapter while terrain fog is absent, not a claim
about original-game invisibility detection. It targets anywhere on the map.
Original effects 254 (Willow), 191 (Aviary) and sound event 376 are exported.

## Model audit

The 24 `OBJECT_PLACE_OF_POWER_*` rows reference **15 distinct original models**
through `Objects.ModelEvent → ModelEvents.AssociatedModelResource1`. They are
exported to the shared pack's `objects/places/` folder and loaded before picking
meshes are registered. Map anchors, rotations, scales and precomputed navigation
footprints are preserved. The footprint of a random marker remains its original
map footprint; navigation is not regenerated for each possible replacement mesh.

| Source sharing | Model |
|---|---|
| Orc Mask and editor Random marker | `MODEL_POP_ORC_MASK` (Random is replaced at runtime) |
| Three Balrog claws and Spires of Siege | `MODEL_POP_BALROG_GLYPH` |
| Energy/Reforming orbs, Hasty Summoning, Globe of Mastery | `MODEL_POP_DARK_ORB_OF_REFORMING_ENERGY` |
| Sentinel Tree and Mallorn of Galadriel | `MODEL_MALLORN_WARD_TREE` |
| Elven Temple and no-bonus scenario variant | `MODEL_POP_ELVEN_TEMPLE` |

Wretched Sentinel, Ancient Fountain, Dragon Skull, Silent Watcher, Radagast's
Aviary, Enchanting Willow, Steed of Rohan, Monument of Narsil, Statue of Eorl and
Radiant Monolith each have their own source model. Radagast's display spelling
corrects the source object's internal “Radaghast” label.

Source `NONE` rows without an object power are scenario landmarks: Sentinel Tree,
Energy/Reforming orbs and the no-bonus Temple. They show “Scenario landmark · no
army bonus”. The Catapult rule has no `OBJECT_PLACE_OF_POWER_*` object and stays
scenario-only; campaign scripting is outside current skirmish support. No
invented passive is assigned.

## Refresh and verification

Run `python tools/content_pack.py` and restart. Existing shared-pack map caches
receive the models and rules without reimporting terrain. The pack includes
source model assignments, spell/action provenance, icons, cursors and 15 GLBs.

`tools/test_places_of_power.py` checks source bonuses, model assignments and spell
parameters. `PlacesOfPowerChecks` covers capture, contesting, complete random
objects, speed/sight, costs and refunds across capture, targeted damage and both
timed active powers, replay digests and bot personality/visibility gates.
`./prototype/run.ps1 -PlacesSmoke` renders all 15 models and exercises both
powers' Q targeting, icons/cursors, previews, actual effects and cooldowns. It writes
`places-smoke-report.json` to its map cache; optional `--capture=<path>` saves the
gallery and targeting/cast images.

Verified October 29: 142 combined Fate/tech/defense/place checks, 34 Ranger,
41 Dwarf, 52 global-power/bot, 47 hero-AI, 32 revival, 18 fixed-hit damage and
12 projectile-presentation checks passed, plus eight Python exporter/interface
tests. The rendered Places smoke passed all eleven checks with no engine errors.
The full simulation suite still stops at `MeleeChecks`' “dead target cancels
pending hit”; the same failure reproduces in an isolated snapshot with this
change's combat paths removed. Dust and Blood's broader skirmish smoke loads
the new landmarks successfully but still fails its existing worker context-move
completion check. These broader failures are not marked as passes.
