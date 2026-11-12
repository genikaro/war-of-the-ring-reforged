# Blood, critical kills, knockback and swing follow-through — builds 129-131

Fights now bleed the way the 2003 game's data describes, knocked-back units get
up with their own animation, and a swing whose victim dies plays to its end.

## Blood on hits

Every weapon hit that deals damage plays two original effects at the struck side
of the victim's body, chest high for melee and mid-body for missiles:

- **The victim's blood**, `Units.BloodEffectType` ("the particle effect that will
  play on the unit when he/she is hit"): red blood for men, elves, dwarves and
  hobbits (`Target_Unit_Blood_Splash`), dark orc blood for the Uruk-hai
  (`Target_Unit_Orc_Blood_Splash`), splinters and sap for the Huorn
  (`Huorn_Blood_Impact`). Wraiths, the Balrog and the Nazgul have none.
- **The weapon's impact**, `WeaponEffects`: the attacker's weapon impact type
  against the victim's composition. Blades on flesh flash white
  (`Weapon_Impact_Cutting_Strong`); blades on orc flesh splash orc blood; blunt
  weapons, staves and hammers play nothing. When the impact is the same effect as
  the victim's blood (a sword on an Uruk-hai), it plays once.

Trueshot's arrow and the bear's Savage Blow bleed too, though spells emit no
weapon hit. Tower arrows bleed their victims without a weapon impact.

Each effect keeps its original layers: flying drops (four random frames),
an animated spray (six frames, 130 ms each), a mist and ground splats, with their
original sizes, colours, durations and emitter counts, speeds and cones.

### Scaled blood (build-131, Reforged option)

The original bleeds on every hit. With **Settings > Reforged > Scaled blood**
(on in the Reforged edition, off in Purist) a hit bleeds by how hard it is and
how wounded the victim is:

- **Amount by damage:** half the source particle count for a glancing blow, the
  source count at 25 damage, half again from 50 damage upward, linear in between
  (12.5 damage: 75%).
- **Chance by health left after the hit:** never at 80% health or more, always at
  20% or less (so a killing blow always bleeds), linear in between (50%: half
  the hits). The draw is fixed per hit, so a replay bleeds the same way.

Only the victim's blood is scaled. The white flash of a blade on flesh, critical
bursts and death pools are not. A blade only becomes bloody from a hit that
actually bled. The rule lives in `prototype/Presentation/BloodIntensity.cs`.

## Blood pools and bloody weapons

A dying unit spills one of its three source death decals
(`Units.DeathDecalType1-3`): it grows to about 2 m over 4 seconds and fades out
after 10 to 15 seconds, with the source size, alpha and timing variations.

A melee weapon that strikes a bleeding body shows its blood overlay, the model
parts flagged `MODEL_FLAG_WEAPON_BLOOD_RIGHT/LEFT` (for example the Gondor
Swordsman's speckled blade), for the rest of the unit's life. Sap and wraiths do
not stain blades.

## Critical kills (build-130)

When a weapon flagged `Weapons.CanCriticallyKill` ("can cause critical deaths to
occur") lands the killing blow on a unit flagged `Units.CanBeCriticallyKilled`,
the body bursts into the unit's `Units.CriticalDeathEffectType` instead of
falling: `Human_Explode` for men and elves, `Orc_Explode` for the Uruk-hai,
`Horse_Explode` for the Rohan Rider. In this roster the critical weapons are
Gimli's axe, the Giant Ent's fists and boulders, and the Ballista Tower's bolt
(trolls and catapults have them too). Heroes, the Shieldbreaker, the Huorn and
the bear can never be critically killed ("hero units should not be critically
killable").

The burst keeps its original layers: an orange-red blood-vapour cloud flaring to
5 m, a skull, a heart, bones, flesh (and an orc's hand) flying out and landing,
blood splats scattered up to about 2 m, and a 3 m blood mark and scorch on the
ground. The model disappears behind the cloud and leaves no corpse, and the
ordinary blood pool is skipped. A ranged unit's contact attacks use its melee
weapon's flag. Spells (Trueshot, Savage Blow) never critically kill.

The simulation records the critical death on the unit (`SimUnit.CriticalDeath`);
it changes nothing else in play. With blood switched off, a critical death plays
the ordinary death instead.

No shipped unit model uses the Attack/Death Critical Behead/Impale animation
states or parts flagged `MODEL_FLAG_CRITICAL_DEATH_BLOOD` (all 61 Good and Evil
unit models checked), so those stay unused, as in the original.

## Settings

**Settings > Game > Blood** (on by default) shows or hides everything the source
flags `IsMature` ("will not be seen if player turns off maturity in the
options"): hit blood, the blade flash, critical bursts, pools and bloody weapons. The Huorn's
splinters and sap are not mature and always show. This is the original option,
not a Reforged improvement.

## Knockback

`Knocked Back` (unit animation state 37) now plays at its own pace from the
blow and holds its last, lying frame after the flight. Then `Get Up` (state 34)
plays. The unit cannot act, move or regenerate until it is up:
`max(flight, Knocked Back clip) + Get Up clip`, about 2 to 3 seconds. Before
this build the clip was squeezed into the flight time and the unit stood up
instantly on landing. Units without a Get Up clip act on landing as before.

## Swing follow-through

When an attacker's victim dies (or leaves the field) mid-swing, the swing plays
to the end of its clip instead of snapping to the next animation. It deals no
more damage, and any new order from the player or a bot still cuts it short at
once. Only the unit's automatic next step (picking a new target, walking on)
waits, for at most the rest of one attack clip.

## Building hit sounds (build-131)

A weapon striking a material its impact type has no `WeaponEffects` row for now
plays the impact type's `DefaultWeaponEffectSetting` row ("the setting used when
an impact/composition pairing is not defined"). Before, the Elven Archer's arrows
and dagger were silent against every building: piercing weapons list only flesh,
dirt, water and spider rows, so wood and stone now take the default (the arrow
into dirt). Other melee units already had wood and stone rows. This applies to
melee sounds (`tools/game_audio.py`), missile impact sounds
(`combat_rules.projectile_presentation`) and hit effects alike.

## Adapters

- Particle motion: emitted layers leave the wound inside the source cone,
  biased away from the blow, and fall at 9.8 m/s². Flash and mist layers stay
  where they start. The source emitter fields beyond count, speed and cone are
  not decoded.
- Ground splats are bound to the drops in the source (they appear where drops
  land); here they appear around the feet 0.3 s after the hit.
- A layer shows its emitter's source particle count (emitter header word 20: one
  skull, six bones, fourteen drops; the key's larger number is the emission rate
  per second), capped at 12 small, 5 large or 8 ground sprites; at most 48
  effects and 96 pools show at once. Blood uses alpha blending, the weapon flash
  additive blending.
- Gore pieces that last two seconds or more (skull, bones, heart, flesh) fly at
  the source speed varied by a quarter, at least 45 degrees off vertical, spin,
  and rest where they meet the terrain until they fade. Mists hover. Tracks with
  parameter 1 (burst blood, scorch, blood mark) lie on the ground; burst splats
  scatter with their source speed.
- Every qualifying killing blow is critical; the original may add a chance or
  conditions that are not decoded (Rings.exe was searched without finding the
  trigger).
- Pools lie on the terrain slope under the body; the source projects them onto
  terrain and water.
- The Effects/Decals data is exported by `tools/blood_effects.py` into the
  content pack (`combat/blood.json`, `combat/blood/*.png`).

## Validation

```powershell
python tools/content_pack.py
./prototype/run.ps1 -BloodSmoke -CapturePath <abs path>/blood.png -Resolution 1600x900
```

`-BloodSmoke` runs a Swordsman/Uruk-hai brawl, a Legolas Trueshot and Gimli
against two Uruk-hai on the Sandbox map and checks: hits play source blood, the
flash on flesh, death pools, a bloodied Swordsman blade, swings followed through
after kills (none cut short), Knocked Back then Get Up, recovery, both of Gimli's
kills bursting (hidden bodies), the Swordsmen's kills staying ordinary, unhurt
victims never bleeding, badly wounded ones always bleeding, harder hits throwing
more particles, every Swordsman and Elven Archer hit on an enemy building playing
a sound, the Blood
box on the Settings Game tab, and that unticking it and pressing Apply hides
effects, pools and blades. It writes `blood-smoke-report.json` and captures
`-hit`, `-splash`, `-closeup`, `-closeup-late`, `-pools`, `-knockback`,
`-get-up`, `-critical-burst`, `-critical-gore` and `-critical-rest`.

Python: `tools/test_blood_effects.py` (including the default-row fallback).
Simulation: `MeleeChecks` (the swing plays out after its victim dies; only a
critical weapon's killing blow on a killable unit is critical),
`NatureAbilityChecks` (lying and getting up) and `BloodIntensityChecks` (the
scaled blood rule).
