# Imported combat

The 36-unit demo uses source definitions for Aragorn, Elven Knight, Elven Archer
and Uruk-Hai. Original damage-formula fidelity has not been established. Movement
and combat spacing use source-derived per-type envelopes, with explicit
[demo sizing and friendly compression](ENGAGEMENT.md#crowd-and-attack-move-follow-up).

## Definitions and provenance

Build-95 tunes Human Workers to the owner's requested 5 base damage per hit at
an average speed (one hit every 30 ticks / 1.5 seconds). Loading combat rules
adapts existing content packs to the source `G_Human_Worker_Attack_Melee_01`
single-punch clip, with its hit at tick 18. The animation is sampled over that
same 30-tick cycle; gathering and construction keep their own clips. The former
two- and three-punch variants are no longer combat choices. `DamagePerHitMilli`
keeps the base hit exactly 5 HP despite the integer DPS readout of 3.333; armor,
matchups and bonuses still adjust damage normally. Raw source data stays intact.

| Unit / source ID | Health | Armor points | Weapon DPS | Damage classification | Weapon ID |
|---|---:|---:|---:|---|---:|
| Aragorn / 16 | 485 | 3 | 20 | HERO (3) | 15 |
| Elven Knight / 3 | 350 | 3 | 10 | CUTTING (0) | 4 |
| Uruk-Hai / 34 | 240 | 1 | 14 | PIERCING (1) | 74 |
| Elven Archer / 2 | 180 | 0 | 14 (bow) | PIERCING (1) | 13 |

`Units.MeleeWeapon` resolves the `Weapons` record. The Elven Knight uses
`WEAPON_GONDOR_SWORDSMAN_MELEE`. Damage comes from `CuttingDamagePerSecond`,
not a per-hit field. All three source units specify eight attackers. Since build-149, Uruk-Hai use their source
crossbow at range (13 m maximum) and retain weapon 74 and the source melee clip in close combat. The missile
releases at tick 28 of its 41-tick animation; the melee hit remains at tick 15 of its 27-tick animation.

`tools/combat_rules.py` reads unit, weapon, classification and pair-modifier
tables; model states/events; skeleton durations; impact effects and sounds.
`converted/prototype/combat/rules.json` retains source IDs, names, raw weapon
fields, event indices/fractions, clip durations and SHA-256 hashes of the input
tables, models, skeletons and selected sounds. The manifest hashes derived rules,
models and PCM sounds before launch. State digests include the rules hash,
precise health and attack state.

There are no explicit unit-to-unit modifiers between IDs 3, 16 and 34. Absent
pairs use 1.0. The shipped addendum (PDF page 7) and the owner's weapon-rating
photograph establish cutting > piercing > blunt > cutting. The owner specified
125% for the favorable matchup, 75% for its reverse and 100% for Hero attacks.
Equal types and attacks against Hero use a documented neutral 100% policy.

| Attacker / target | Cutting | Piercing | Blunt | Hero |
|---|---:|---:|---:|---:|
| Cutting | 100% | 125% | 75% | 100% |
| Piercing | 75% | 100% | 125% | 100% |
| Blunt | 125% | 75% | 100% | 100% |
| Hero | 100% | 100% | 100% | 100% |

Rules format 3 includes per-type bodies, all 16 integer `TypeMatchups` and their provenance in
the hashed cache. The parser rejects incomplete/duplicate tables; unclassified
probe types remain neutral. Pair invincibility overrides damage. The addendum
does not state the numeric percentages or the full damage formula.

**Build-61: a source pair replaces the type matchup.** The source `DamageTypes`
table has no multipliers. Its 620 `UnitToUnitModifiers` rows are the type cycle:
150% for the favourable matchup (Rohan Rider against Gondor Swordsman), 90% for
the reverse and 85% against heroes. Until build-61 the prototype applied both,
so a Rider hit a Swordsman for 125% × 150% = 187.5%. Now a pair with a source row
uses that row alone (`CombatRules.DamageScale`), and the table above applies only
to pairs the source leaves out, such as the Sandbox dummies. Armor follows.
See [the reference note](../references/original-game/COMBAT-RATINGS-2026-10-19.md).

Refresh an existing cache using `python tools/prepare_prototype.py --rules-only`
and `python tools/prepare_prototype.py --ui-only` for the type icons.
A full preparation also builds these resources. Original/extracted inputs stay
read-only; no game assets enter source control and no audio dependency is added.

## Attack lifecycle and numeric policies

The simulation runs at 20 Hz using integer positions, milli-health and rule
values. Import quantization uses decimal half-up rounding, with 1,000 units per
armor/DPS point and 1,000,000 units per multiplier/event scale.

Each swing selects a positive-weight `ATTACK_MELEE_01` choice using deterministic
shuffled weighted cycles (build-118). Common weight factors are removed first;
source weights of 10000 therefore behave like weights of 1. Each cycle includes
every equal-weight clip, while unequal weights retain their relative frequency.
Unit attacks and building attacks share this selector; ranged missile/contact
variants remain separate. This is a prototype selection
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

Imported-body armies now redistribute overflow attackers among reachable hostile
targets and replan after repeated avoidance. Contact clearance permits a legal
strike without the old probe's extra comfort margin. Three units may plan per tick;
current movement and timed hits continue every tick. Force Attack, stationary
orders and queued primary-target completion have separate regressions. See
[the engagement correction and performance measurements](ENGAGEMENT.md).

The damage policy is explicitly approximate. Since build-116, the displayed
base damage is authoritative for every strike. Animation variants keep their
source durations and hit-event timing; they do not change damage per strike.
Aragorn's unupgraded attacks all deal 26 base damage, including each strike in
his two-hit animation. Other units use the same rule; missile and contact
weapons have separate cadences.

```
scale = sourcePairScale if the pair has a source row, else typeScale
baseHitMilli = explicit per-hit value, or roundedHUD(weaponDpsMilli * weightedMeanSecondsBetweenHits)
typedDpsMilli = attackerDpsMilli * scale / 1_000_000
rateMilli = max(0, typedDpsMilli - victimArmorMilli)
hitDamageMilli = baseHitMilli * rateMilli / weaponDpsMilli
```

Integer division truncates; damage is capped at remaining health. Invincibility
produces a zero-damage hit. Upgrades, hero levels, target-specific bonuses and
armor still adjust the fixed hit. The tooltip reports effective DPS using the
actual fixed damage and mean attack cadence. Fractional damage
stays in milli-health. The HUD rounds HP upward and uses imported maximum health
for bars. A hostile lethal hit awards exactly one `KillCount` to its attacker;
later hits on the dead victim cannot award it again. Friendly force kills do
not award victories. This attribution is a prototype policy; the local capture
confirms a victory counter but does not isolate the original attribution rule.
Kill counts are part of the state digest.

Single selection now shows the portrait, name, live health bar, original damage
type icon and a framed Attack / Armor / Victories block, following `local-020`
and `local-025`. Attack displays the imported base weapon DPS number (the source
field), without claiming it is damage per hit. Hovering the type icon lists all
four percentages. Group selection retains its clickable portrait roster and
per-unit stats tooltips. Both faction counter-marker crops are imported; the
current Good console uses its matching gold-framed variants.

**Unverified interpretations:** subtracting armor from DPS and normalizing event
scales are implementation policies, not recovered formulas. Controlled
original-game duels or executable analysis remain required. Raw weapon ranges
are 1.0 for Aragorn and 0.5 for the others; original range origin semantics are
unknown. Effective center reach is `max(2000 mm, both body radii + source range)`
with wall/layer checks, so larger bodies can fight without overlapping.
The gap interpretation is an adapter policy. Source radius/height are 561/2095 mm for
Aragorn, 927/2534 mm for Elven Knights and 693/2197 mm for Uruk-hai, traced from
scaled Main collision boxes. [Collision notes](../tools/NOTES-unit-collision.md)
record the later Aragorn demo adjustment to 800/2347 mm and 112% visual scale.
Friendly cores use half the combined radii; enemy contact retains full envelopes.
The collision notes also describe clearance profiles and limits. Default running
uses each unit's [source run speed ratio](MOVEMENT.md#build-60-per-unit-run-speeds)
with the Gondor Swordsman at 3 m/s; F2 walking uses the
[source animation ratio](MOVEMENT.md#walkrun-control--october-20-2026). Awareness, spacing and opponent AI
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

The [October 19 original-game session](../references/original-game/LOCAL-SESSION-2026-10-19.md)
adds sampled encounter evidence, not a timing trace. A Gondor swordsman displays
155 HP, Attack 10, Armor 0 and a victory counter; after combat the counter reached
1. HP also rose between later samples. Those observations do not establish
per-hit damage, regeneration rate/cause, armor subtraction or kill attribution.

The [roadmap](../tools/Roadmap.md#next-milestone-original-game-command-and-combat-corrections)
still calls for a repeatable swordsman/worker reference fixture and matching continuous
recordings or health traces, followed by the current three demo types. Hold,
retaliation, retreat, interruption and regeneration must be isolated. Cross-check
the swordsman's source records without adding it to the demo by default. Correct
only measured rules; the current armor/event normalization and body-based reach stay
explicit approximations until evidence resolves them. The owner requested Elven
Archer implementation next; original launch/flight/impact comparison remains open.

```powershell
python -m unittest discover -s tools -p 'test_*.py'
python tools/prepare_prototype.py --rules-only
python tools/prepare_prototype.py --ui-only
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --cache converted/prototype
./prototype/run.ps1 -MeleeSmoke
./prototype/run.ps1 -MeleeSmoke -Resolution 3840x2160 -CapturePath converted/prototype/melee-4k.png
./prototype/run.ps1 -CommandSmoke -Resolution 3840x2160 -CapturePath converted/prototype/unit-hud-4k.png
```

Synthetic checks cover windup, exact hits, recovery, Stop/reissue, retargeting,
death, moving victims, queues, multi-hit normalization, fractional health, armor,
all 16 type matchups, pair modifiers/invincibility, lethal attribution, attacker
slots and replay. The command smoke checks single/group selection and live
victory refresh; inspected 1080p and 4K captures show the stats panel. Import tests cover
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

Unsupported rules: upgrades, levels above one, regeneration in this demo (its cache predates build-52;
`prepare_prototype.py --rules-only` adds the source rates), height bonuses,
poison, knockback, other ranged units, critical deaths and the complete
animation/effect event system. This adapter covers four types, not all 85 units.

## Elven Archer

Requested October 20. Friendly squad 3 now contains six archers; the total stays
36 units. `UNIT_G_ELVEN_ARCHER` (2) uses missile weapon 13, `WEAPON_ELF_ARCHER_MISSILE`:
180 HP, zero armor, 14 DPS, Piercing, minimum range 1 m, maximum range 13 m.
The imported collision cylinder is radius 535 mm / height 2351 mm; its original
portrait, model, walk/run ratio and equipment visibility tracks are retained.

`Elven_Archer_Attack_Missile` lasts 2.0333333 source seconds, rounded up to 41
ticks. Its `missile_release` event 5 occurs at source fraction 0.5333330, so the
arrow launches on tick 22. The projectile model comes from resource 1243 in
`PROJECTILE_ELVEN_ARCHER_ARROW` (14), with the original LPO preserved in the
rules cache. Model, LPO and database hashes are recorded. Refresh an existing
cache with `python tools/prepare_prototype.py --units-only`. Since build-15 each
ranged unit's projectile model, launch sounds and trail are stored per unit
under `Projectiles`; the skirmish Dwarf Axethrower uses the same path (see
[skirmish notes](SKIRMISH.md#build-15-dwarf-hall-trains-the-dwarf-axethrower)).

Archers keep their firing position when a target is in range and visible, and
otherwise use the existing combat approach planner. Bow shots neither consume
nor obey melee attacker slots. When engaged in melee, archers use
`WEAPON_ELF_ARCHER_MELEE` (16), also 14 DPS, with its source 0.5 m surface gap
and all three original `Attack_Melee` animations and hit events. Close enemies
interrupt a bow windup even when it targets a different distant unit. After
contact ends the archer returns to shooting; an explicit distant target resumes
after defeating a temporary melee threat. Melee attacks obey normal attacker
slots. Hold fights without chasing; other existing
orders retain their movement, queue and pursuit rules. Damage uses the same
type/pair/armor policy as melee, frozen at release and applied only at impact.
Stop, Move, retargeting and weapon switches cancel pending attack events while
retaining cooldown; switching weapons cannot bypass recovery.
Released arrows survive replacement orders and shooter death; dead targets
consume arriving arrows without another hit or victory. Active projectiles,
their release/impact ticks and IDs are included in state digests.

Explicit approximations: range is measured between unit centers; arrows travel
at 25 m/s and follow their original target, without interception or collision
after release. The source LPO's seek/motion semantics have not been recovered.
Launch and aim use body-height offsets rather than live skeleton joints.
Line of fire samples terrain and deck planes every 200 mm, treating blocked
navigation columns as opaque unless a walkable deck supports the shot above
them. This conservatively blocks some shots over water or low cover; it is not
a render-mesh ray cast. Allies do not intercept arrows. The switch uses the
same body-based melee reach as infantry (`max(2 m, both radii + source gap)`),
including enemy melee engagement when the enemy has longer reach. This threshold
and shared recovery policy remain approximations; the owner confirmed the
original game's ranged-to-melee behavior. Upgrades and height bonuses remain open.

`RangedChecks` covers release/impact timing, approach and Hold range, interruption,
shooter/target death, moving targets, attacker caps, cover, deck crossings,
both weapon-switch directions, interrupted windups, melee threats and replay.
Cached checks exercise all three source melee timelines. The extended
`-MeleeSmoke` verifies all four imported types, exact release timing, arrow
visuals, delayed hits, source melee animation playback and safe squad fighting.

### Ranged turning and animation pivot

The archer's source `J_Position` is about 0.7 m forward in ready/attack poses,
but near zero in locomotion. Retaining that offset made the mesh swing around
the unit origin during hit-and-run turns. Archer cache adaptation now centers
every clip and the bind pose on `J_Position`, applying the same horizontal
correction to all independent skeleton roots. Vertical bobbing and relative
joint motion remain intact. Refresh older model caches with `--units-only`.

Ranged facing now advances at simulation ticks and is interpolated for drawing,
so render frame rate cannot determine when combat starts. A new windup waits
until both ends of the interpolated turn are within 0.5 degrees of the target
on first acquisition. Build-97 lets an established track compare the previous
heading with the previous tick's aim at that same target, while the current
heading must match its current bearing. Requiring both headings to match the
new bearing could prevent all shots at a circling runner. The aim sample expires
after a tick without aiming and is included in the state digest. Active attacks
can track moving targets; replacements still cancel normally.
Movement redirects turn from the current heading, and tiny arrival corrections
do not change it. Facing is included in replay state.

Source `Units.MaxTurnAngle` is 60 for Elven Archer. Its database description
says the maximum angle a unit can turn at once, in degrees; it does not specify
a per-second speed or the original turn update cadence. This value is retained
in `Source.MaxTurnAngleDegrees`. The runtime retains the existing prototype
8 rad/s speed, quantized to 22.918 degrees per 50 ms tick. It is not claimed as
the original game's turn rate. The 0.5-degree start threshold is also an adapter
policy. Import tests check pivot alignment across idle/run/attack poses; facing
checks cover turn budgets, windup gating, angle wrap and repeated hit-and-run.

### Arrow launch sound and gray trail

Elven Archer's LPO launch event 7 contains opcode 46 and SoundEvents ID 172,
`SOUND_EVENT_WEAPON_ELVEN_ARROW_LAUNCH`. The same payload shape matches ordinary,
fire, ballista and axe projectile launch sounds. All six Elven variants are
converted from the original Microsoft ADPCM files to PCM and hashed in the
cache. The `Release` event plays a spatial one-shot at the shooter, using the
source minimum volume (0.6) and a separate three-voice pool. Bow windup,
cancellation before release and melee hits do not trigger a launch sound.
Pause/resume and scene shutdown control both impact and launch audio pools.

The arrow's first resource binding references effect 12, `EFFECT_TEST_ARROW_TRAIL`,
also shared by ordinary and Legolas arrows. The source track has a 250 ms
duration, width key 0.05 tapering to zero, and packed colors `0x3fffffff` to
`0x19ffffff`. The cache preserves the full LFX and its hash. Runtime draws a
thin, feathered ribbon through recent projectile positions, with a 70% gray
tint and age fade. It remains briefly after impact and then frees its mesh.
World-space points prevent the trail rotating with the arrow or connecting
unrelated shots. Point ages use simulation time, so pause freezes the trail.

The narrow adapter interprets the track duration as point lifetime, key word 3
as full width and word 5 as ARGB. These interpretations, the gray tint and the
feather/fade curve are presentation policies; exact legacy blending remains
unverified. Sound variant scheduling, spatial falloff and voice replacement are
also adapter policies. Neither sound nor trail changes projectile damage,
collision, aiming or replay state. Refresh with `--rules-only`.

`-MeleeSmoke` now checks one launch trigger and one trail per released/visible
arrow, plus trail drawing samples. Both 20 and 80 FPS runs cover these effects;
portable trail checks cover pause, bounded lifetime, post-impact cleanup and
agreement of tail length across render rates.
