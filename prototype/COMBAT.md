# Imported melee combat

The existing 36-unit demo uses source definitions for Aragorn, Elven Knight and
Uruk-Hai. Original damage-formula fidelity has not been established. Movement
and combat spacing retain the accepted baseline.

## Definitions and provenance

| Unit / source ID | Health | Armor points | Melee DPS | Damage classification | Weapon ID |
|---|---:|---:|---:|---|---:|
| Aragorn / 16 | 485 | 3 | 20 | HERO (3) | 15 |
| Elven Knight / 3 | 350 | 3 | 10 | CUTTING (0) | 4 |
| Uruk-Hai / 34 | 240 | 1 | 14 | PIERCING (1) | 74 |

`Units.MeleeWeapon` resolves the `Weapons` record. The Elven Knight uses
`WEAPON_GONDOR_SWORDSMAN_MELEE`. Damage comes from `CuttingDamagePerSecond`,
not a per-hit field. All three source units specify eight attackers. Uruk-Hai's
missile weapon/preference is outside this melee slice.

`tools/combat_rules.py` reads unit, weapon, classification and pair-modifier
tables; model states/events; skeleton durations; impact effects and sounds.
`converted/prototype/combat/rules.json` retains source IDs, names, raw weapon
fields, event indices/fractions, clip durations and SHA-256 hashes of the input
tables, models, skeletons and selected sounds. The manifest hashes derived rules,
models and PCM sounds before launch. State digests include the rules hash,
precise health and attack state.

There are no explicit unit-to-unit modifiers between IDs 3, 16 and 34. Absent
pairs use 1.0. `DamageTypes` supplies labels, not a numeric generic matchup
matrix; no class bonus is invented. Explicit directional pair multipliers and
invincibility are supported and tested with synthetic profiles.

Refresh an existing cache using `python tools/prepare_prototype.py --rules-only`.
A full preparation also builds these resources. Original/extracted inputs stay
read-only; no game assets enter source control and no audio dependency is added.

## Attack lifecycle and numeric policies

The simulation runs at 20 Hz using integer positions, milli-health and rule
values. Import quantization uses decimal half-up rounding, with 1,000 units per
armor/DPS point and 1,000,000 units per multiplier/event scale.

Each swing selects a positive-weight `ATTACK_MELEE_01` choice using stable
per-unit/attack-sequence weighted selection. This is a prototype selection
policy, not the recovered original RNG. Windup precedes the hit events; recovery
lasts until the full cycle ends. Source times are normalized fractions:

```
cycleTicks = ceil(sourceClipSeconds * 20)
hitTick = min(cycleTicks - 1, ceil(sourceClipSeconds * sourceEventFraction * 20))
```

| Unit | Choices: cycle ticks / hit offsets |
|---|---|
| Aragorn | 34 / 21; 31 / 19; 37 / 13, 27 |
| Elven Knight | 31 / 10; 31 / 10; 34 / 16 |
| Uruk-Hai | 27 / 15 |

Only simulation hit events resolve damage, after checking that the victim is
alive, still targeted and within supported reach. Stop, movement, replacement
orders, lost reach and target death cancel pending hits. Cancellation preserves
remaining cooldown, preventing Stop/Attack from accelerating the cadence. Queued
orders preserve the current swing. Dead victims cannot finish pending swings.
The attacker cap admits nearby eligible units in stable ID order; waiting units
can take released slots.

The damage policy is explicitly approximate:

```
rateMilli = max(0, attackerDpsMilli * pairScale / 1_000_000 - victimArmorMilli)
hitDamageMilli = rateMilli * cycleTicks * eventScale / (20 * sumOfClipHitScales)
```

Integer division truncates; damage is capped at remaining health. Invincibility
produces a zero-damage hit. Dividing by total hit scales keeps Aragorn's
two-event choice at its cycle DPS instead of doubling it. Fractional damage
stays in milli-health. The HUD rounds HP upward and uses imported maximum health
for bars; portrait tooltips show armor, DPS and classification.

**Unverified interpretations:** subtracting armor from DPS and normalizing event
scales are implementation policies, not recovered formulas. Controlled
original-game duels or executable analysis remain required. Raw weapon ranges
are 1.0 for Aragorn and 0.5 for the others; body/range origin semantics are
unknown. Effective center reach therefore stays at the accepted 2,000 mm with
wall/layer checks. Movement stays at 3 m/s. Awareness, spacing and opponent AI
remain prototype policies.

## Presentation

Attack players seek from the simulation clock, including repeated selection of
the same clip, and pause with the simulation. Animation/audio never cause
damage. Original ready clips play between swings while a unit has a target.

The prototype importer reconnects hidden mesh leaves with stable node bindings.
`UnitAppearance` adapts `extras.wotrAppearance`: root clip mesh, attachment and
cloth visibility; normalized visibility keys; parent attachment gates;
level-one equipment and blood-disabled flags. Nested models use their declared
defaults. Skeletons/bones stay active. Aragorn switches between sheathed idle
equipment and drawn ready/attack equipment. This is visibility adaptation, not
cloth physics or the complete texture/effect/flag system.

Broadsword impact type 11 against flesh (composition 1) and orc flesh (16)
resolves to sound event 339. Three source variants (effects 877–879) are converted
from mono Microsoft ADPCM to 16-bit PCM in the derived cache. The decoder
validates RIFF lengths, coefficients, block headers and fact sample counts.
Predictor/adaptation behavior follows the format implemented by the primary
[FFmpeg ADPCM decoder](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/adpcm.c).
Positive hits trigger a deterministic variant through a four-player positional
pool at the event's minimum volume. Variant selection and spatial falloff are
presentation policies. Swing sounds, voices, particles and full sound-event
scheduling remain open.

## Verification and limits

```powershell
python -m unittest discover -s tools -p 'test_*.py'
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --cache converted/prototype
./prototype/run.ps1 -MeleeSmoke
./prototype/run.ps1 -MeleeSmoke -Resolution 3840x2160 -CapturePath converted/prototype/melee-4k.png
```

Synthetic checks cover windup, exact hits, recovery, Stop/reissue, retargeting,
death, moving victims, queues, multi-hit normalization, fractional health, armor,
pair modifiers/invincibility, attacker slots and replay. Import tests cover
event quantization, protected outputs, idempotent equipment restoration and
ADPCM known-sample/error cases.

Cache checks repeat Aragorn/Uruk-Hai and Elven Knight/Uruk-Hai duels in both
spawn/order directions. They require identical state/hit logs, one survivor and
exact hit offsets, writing `melee-duels-report.json` with full events and rules
hash. These reports measure this implementation, not original-game balance.

`-MeleeSmoke` requires all three squads to fight safely, correct imported health
and IDs, exact event ticks, simulation-driven clip seeks, equipment changes and
impact playback. Reports are `melee-smoke-report.json` and
`melee-hit-trace.json`. The inspected 4K capture retains the four-action row and
proportional HUD. Existing command/group, motion, surface and obstacle checks
remain regressions.

Unsupported rules: upgrades, levels above one, regeneration, height bonuses,
poison, knockback, ranged/projectile damage, critical deaths and the complete
animation/effect event system. This adapter covers three types, not all 85 units.
