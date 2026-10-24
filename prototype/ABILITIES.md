# Aragorn abilities — builds 2–3

The sandbox equips Aragorn's three source abilities so they can be tried without
an economy or upgrade screen. This includes the purchasable passives; it is an
explicit sandbox setup, not the original game's starting upgrade state.

## Try it

Select Aragorn, or a group containing him. The original ability icons appear
above the four bottom command buttons.

- **Q / Greater Kingsfoil:** click an injured friendly unit or Aragorn himself.
  The target must be below 99% health. Aragorn approaches targets beyond 10 m,
  then plays the original healing animation and sound. The heal restores up to
  250 HP, capped at maximum health. A floating green number shows the amount.
  Build-3 adds the original target-effect textures: expanding ground/rising rings,
  leaves, clouds and sparkles following the healed unit for four seconds.
- **Andúril's Fury (slot W):** passive. Equipping it changes Aragorn's weapon
  Attack from 20 to 30. Wraith and Black Rider unit types receive a further 150%
  multiplier. The HUD shows the equipped weapon's Attack 30.
- **Immunity (slot E):** passive protection flags for poison, stun, blind and
  slow. Current demo enemies produce none of these statuses. Status producers,
  status playback and Kingsfoil's poison cleansing are not implemented yet.
- **R:** reserved; Aragorn has no fourth source ability.

W/E do not cast spells or consume a cooldown. Their icons have no text overlay;
their tooltips explain that they are passive. The active skill's hotkey sits at
the bottom-right of the icon and remains visible while its centered cooldown
counts down. While targeting, the original Kingsfoil cursor indicates the skill;
enemies and full-health units show the invalid-target cursor. Hovering the HUD,
casting or cancelling restores the normal pointer.
Q also works from mixed friendly selections; infantry and enemy selections
cannot cast Aragorn's abilities. F2 remains the army-wide Walk/Run switch.

Kingsfoil's cooldown is 17 seconds from the start of the cast, not from targeting
or approach. Its icon shows the remaining time. Esc or right-click cancels
targeting. Move, Stop, Hold, Attack or another replacement order interrupts a
cast; spent cooldown is retained. A queued movement order waits for casting to
finish. Invalid targets and rejected commands preserve the previous order.
Pause freezes casting, approach, healing and cooldown; commands entered while
paused apply after resuming. Issuing F2 during a cast preserves it.

## Source trace

The adapter reads the owner's extracted `.lqr` records through the existing
resolver. Runtime cache records retain gear, spell, target, event and action
values. Their source tables participate in the cache's SHA-256 provenance.

| Feature | Source |
|---|---|
| Ability slots | Units 16 references BattleGear 25/41/44; gear SpecificIndex orders them Kingsfoil/Fury/Immunity |
| Kingsfoil | BattleGear 41 → Spells 89 → SpellAndEvents 215/216 |
| Targeting | SpellValidTargets 54: living allied units including caster, maximum health fraction approximately 0.99, specified range 10 m |
| Heal | SpellAndEventAndActions 422: Apply Health 250 at target |
| Cooldown | Spells 89 RechargeTime 17; action 598 starts cooldown on activation |
| Animation | Action 424 selects UnitAnimStates 90, resolving to `G_Aragorn_Greater_Kingsfoil`, duration 1.0333333 seconds |
| Heal timing | Event 216 listens for toolscript 28 (`DoBattleGear1`); model event 12 fires at fraction 0.377224 |
| Sound | Model event 13 references SoundEvents 189 at fraction 0.375445 |
| Fury | BattleGear 25 → Spells 55; activation switches to Weapons 55 with 30 DPS |
| Fury bonus | Action 258 uses multiplier 1.5; SpellValidTargets 33 specifies Units 52 (Wraith) and 33 (Black Rider) |
| Immunity | BattleGear 44 → Spells 96; activation actions set blind/poison/slow/stun immunity |
| Heal graphics | Effects 171 (`Aragorn_Greater_Kingsfoil_Target.lfx`), five texture tracks with source UVA crops, scale/ARGB keys and durations |
| Heal cursor | Cursors 6 → `cursor_g_greater_kingsfoil.lcr` / original Greater Kingsfoil UVA; rejected targets use `cursor_g_negative` |

The same fixed-tick rounding policy used for attacks maps the heal to tick 8
of a 21-tick animation. The cooldown is 340 ticks. Audio, animation seeking and
floating labels observe simulation events; they never apply health changes.

## Adapter choices and remaining fidelity work

This is a bounded adapter for these three abilities, not a general spell VM.
The original UsageType names and descriptions disagree for Kingsfoil; the valid
target record, face-target action and target-health action support explicit
friendly-unit targeting. A continuous original-game input trace is still needed.

Range is measured between unit origins in 3D; navigation connectivity prevents
casts across disconnected surfaces. Approach uses ordinary body-aware routes.
Both range origins and legacy approach behavior remain unverified. The full
21-tick clip holds the caster in place even though health is applied at tick 8.
Target death or leaving range cancels casting without refund. Healing does not
resurrect, stack on repeated frames or reset attack recovery. Completion returns
Aragorn to the existing idle guard behavior.

Healing graphics use bounded, deterministic sprite motion with the original
layer textures, size/color keys and durations. Leaves orbit/rise, rings expand
and clouds/sparkles fade; glowing layers use additive blending. Playback follows
the target's rendered position, freezes on pause and frees the effect when its
four-second lifetime ends. Exact original emitter/transform behavior remains
unverified. Rebuild existing caches with both `--rules-only` and `--ui-only`
to obtain the effect textures and cursors.

Fury's weapon/type bonuses use the current documented combat multiplier/armor
adapter. Effects 84/194, the sword effect attachment flag 31, other original spell
effects, poison removal, fate/experience modifiers and purchase prerequisites
are retained as provenance where present but are not executed. No new units or
status-inflicting weapons are added in this slice.

## Validation

Run `./prototype/run.ps1 -AbilitySmoke` for the actual input check. It exercises
Q, the heal button, mixed/passive/nonhero/enemy availability, target rejection,
cast animation, health gain, cooldown, pause/resume and cancellation. Optional
`-CapturePath converted/prototype/ability.png -Resolution 3840x2160` captures the
hero console. Existing Command, Orders and Melee smoke checks remain enabled.
The smoke switches health regeneration off: it counts exact health (60 before the
heal, 310 after), and since build-52 a resting unit regains a little every tick.

The plain C# checks cover precise effect ticks, clamping, self-heal, distant
approach, ownership, invalid targets, source health boundary, interruption,
death/range cancellation, retained cooldown, F2 compatibility, passive damage,
malformed definitions and recorded replay. Cache checks verify the source
ability parameters; Python checks reject broken/cyclic spell chains. These are
implementation regressions; they do not close the original-game fidelity gate.

Build-2 validation passed 626 C# checks with the local cache, 145 Python tests,
25 Ability input checks, 46 Command input checks and 27 Orders input checks.
The 420-tick Melee smoke also passed with all three friendly squads fighting
and no body overlaps. Hero console captures were inspected at 1280x800 and 4K.

Build-3 passes 566 simulation/presentation checks, 147 Python tests and 38 Ability
input checks. The latter include heal/invalid/default cursor transitions, clean
skill labels, bottom-right hotkeys, visible source effect layers, target following,
pause freezing and effect expiry. Healing captures were inspected at 1280x800
and 4K.

## Frodo — build-19

Frodo trains at the skirmish Citadel with his three source abilities equipped:
**Q · The One Ring** (toggle: half-transparent, passive and hidden from enemies,
2 HP per second drain that stops at 1 HP), **W · Sting** (passive, ×1.35 against
orcs, Uruk-Hai and goblins) and **E · Heroic Aura** (passive, allies within 15 m
recharge abilities 1.5× as fast). Rules, adapters and validation are in
[the skirmish notes](SKIRMISH.md#build-19-frodo-and-the-hero-portraits).

## Gimli, Legolas and Gandalf — build-58

The Citadel trains the three other Fate heroes with their source gear in its
`SpecificIndex` slots. Two new abilities work:

- **Gimli · E · Siege Aura** (passive): allies within 10 m, Gimli included, deal
  ×1.5 damage to buildings (source child spell 17, `APPLY_BUILDING_DAMAGE_MODIFIER`;
  StackableType 3, so overlapping auras do not stack).
- **Legolas · E · Guardian Wind**: cast on the spot (no target) with the source
  clip (animation state 91); at its toolscript event (29) every ally within 12 m,
  Legolas included, takes 1% of missile damage for 12 s (child spell 30,
  `APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER` 0.01, `START_TIMER` 12). Cooldown 30 s;
  another order interrupts the cast like Kingsfoil's.

Gimli's Sunder and Capture, Legolas's Trueshot and Elven Speed, and all four of
Gandalf's (Blinding Light, Flame Shield, Negate Ability, Rain of Fire) need
knockback, stuns, tower capture, ground targeting or spell negation. They show on
the card disabled, with an English description after the manual and the source
cooldown. The Elven Lightbearer's Protection does the same. Rules and validation
are in [the skirmish notes](SKIRMISH.md#build-58-the-rest-of-the-good-tech-tree-and-the-fate-heroes).
