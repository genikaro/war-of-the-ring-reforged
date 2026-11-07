# Good hero abilities

The Evil heroes' gear (Gollum, the Nazgul King, Saleme, Grishnakh, Saruman) is in [EVIL.md](EVIL.md).

Global Fate powers and bot usage are implemented in build-125; see [GLOBAL-ABILITIES.md](GLOBAL-ABILITIES.md).

## Hero levels — build-106

Heroes now earn combat experience and advance through the original ten levels.
Level bonuses increase health, armor and weapon attack; wounded heroes preserve
their health percentage. Both the console and hero portrait show level and XP,
with the original effect and sound on level-up. Level progression is separate
from abilities: existing slots and cooldowns stay intact. See
[hero experience](HERO-EXPERIENCE.md) for the cumulative XP table and source trace.

## Remaining Good hero skills — build-105

The Citadel's five Good heroes now have their complete source ability sets.
Aragorn, Frodo, Gimli's Siege Aura and Legolas's Guardian Wind keep their previous
rules. Select a hero and use Q/W/E/R or the corresponding command-grid icon:

| Hero | Key | Ability | Effect | Recharge |
| --- | --- | --- | --- | --- |
| Gimli | Q | Sunder | Dazes enemies within 7 m for 5 s; respects stun immunity. | 15 s |
| Gimli | W | Capture | Approach and take an enemy Watchtower or Ballista Tower. | 40 s |
| Legolas | Q | Trueshot | A traveling arrow with the source 125 damage weapon and collision-safe knockback. | 12 s |
| Legolas | W | Elven Speed | Allies within 12 m move and attack 20% faster for 15 s. | 35 s |
| Gandalf | Q | Blinding Light | Dazes enemies within 15 m for 5 s; respects stun immunity. | 20 s |
| Gandalf | W | Flame Shield | An ally burns enemies within 3 m for 9 HP/s for 25 s. | 15 s |
| Gandalf | E | Negate Ability | Disables enemy active and passive abilities in a chosen 13 m area for 15 s. | 30 s |
| Gandalf | R | Rain of Fire | After 1.5 s, ignites enemy buildings and burns enemy units in 10 m for 18 HP/s over 5.5 s. | 60 s |

Negate Ability and Rain of Fire have ground-area previews. Trueshot requires a
visible enemy; Flame Shield accepts living allies including Gandalf himself.
Capture accepts only completed enemy source tower types, approaches the footprint,
preserves health, and updates the defense's allegiance and model colors. Other
buildings, foundations and friendly towers are rejected without spending cooldown.

The existing cast policy applies: finish the complete animation before a replacement
order; Stop before release spends no cooldown, Stop after release preserves the
effect and cooldown. Released arrows, burns, shields and delayed rain survive the
caster's death. Haste changes cadence while preserving per-hit damage; overlapping
Flame Shields apply the strongest burn. Negate Magic removes silence and Rain burns
from allies, and Elven Speed and Flame Shield from enemies. All timed effects show
their source icons and remaining duration and participate in recorded replay.

Source: `tools/hero_skills.py`, through BattleGear / Spells / SpellValidTargets /
SpellAndEvents / SpellAndEventAndActions and the heroes' animation events. Cast
length and release ticks are imported from the original clips. Rain's weapon 67
targets buildings only; its separate child spell supplies the unit burn. Flame
Shield's child spell burns nearby enemies; it does not reflect damage as the old
placeholder description claimed. Original sprite atlases and sounds are exported
in the shared content pack, including Trueshot's special projectile.

Adapters and limits: existing prototype armor, damage/Fate and normalized building
fire rules still apply; knockback uses the existing bounded collision/gravity
adapter. Sprite emission/placement adapts the legacy effects. Controlled comparisons
with the original game remain open. Capture leaves Gimli outside the tower because
tower garrisons followed in build-173 (EVIL.md). Hero levels followed in build-106; ability
purchasing and bot hero recruitment remain future work.

Validation: `SimulationTests --hero-skill-checks --cache converted/content/good`,
Python `test_hero_skills.py`, and `./prototype/run.ps1 -HeroSkillSmoke`.
Add `-CapturePath converted/hero-skills.png` for rendered captures of the skills
and the captured tower.

## Fate ability unlocks — build-113

Heroes start with their innate skills. Purchasable skills show a dimmed icon,
their Fate price, and the Q/W/E/R hotkey. Click the icon or press its hotkey to
buy the skill; that first press purchases it without casting. An unlocked active
skill uses the next press to cast or choose a target. An unlocked passive applies
immediately. Purchases spend the team's Fate once and survive the hero's revival.

| Hero | Paid unlocks | Innate skill |
| --- | --- | --- |
| Aragorn | Andúril's Fury 2; Immunity 1 | Greater Kingsfoil |
| Frodo | The One Ring 1; Sting 1 | Heroic Aura |
| Gimli | Sunder 2; Capture 3 | Siege Aura |
| Legolas | Trueshot 2; Elven Speed 2 | Guardian Wind |
| Gandalf | Blinding Light 2; Flame Shield 1; Rain of Fire 3 | Negate Ability |

These prices and purchase flags come from `BattleGear.IsPurchasable` and
`PurchaseCostPower`. Nominal prices on innate skills are ignored. Insufficient
Fate disables the purchase button; its hotkey reports the required price.
Purchase commands use the normal fixed tick and replay log and preserve the
hero's current movement, cast, cooldowns, and queued orders.

## Aragorn abilities — builds 2–3

Aragorn starts with Greater Kingsfoil. His two passives require the Fate
purchases described above.

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

After purchase, W/E do not cast spells or consume a cooldown. Their icons have no text overlay;
their tooltips explain that they are passive. The active skill's hotkey sits at
the bottom-right of the icon and remains visible while its centered cooldown
counts down. While targeting, the original Kingsfoil cursor indicates the skill;
enemies and full-health units show the invalid-target cursor. Hovering the HUD,
casting or cancelling restores the normal pointer.
Q also works from mixed friendly selections; infantry and enemy selections
cannot cast Aragorn's abilities. F2 remains the army-wide Walk/Run switch.

Since build-103, Kingsfoil's 17-second cooldown starts when the heal applies,
at tick 8. Its icon shows the remaining time. Esc or right-click cancels
targeting. Move, Hold, Attack and other replacement orders wait for the full
cast animation; the latest replacement wins, followed by subsequent Shift
orders. Stop immediately cancels the cast and waiting orders. Before the heal,
Stop spends no cooldown and permits a retry; after the heal, the effect and
cooldown remain. Invalid targets and rejected commands preserve the previous order.
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
Target death or leaving range cancels casting; cooldown is spent only if the
effect already applied. Healing does not
resurrect, stack on repeated frames or reset attack recovery. Completion returns
Aragorn to idle guarding or releases the replacement order waiting for the clip.

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
death/range cancellation, Stop before/after effects, deferred orders, F2 compatibility, passive damage,
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

Historical checkpoint; the remaining skills were completed in build-105 above.

The Citadel trains the three other Fate heroes with their source gear in its
`SpecificIndex` slots. Two new abilities work:

- **Gimli · E · Siege Aura** (passive): allies within 10 m, Gimli included, deal
  ×1.5 damage to buildings (source child spell 17, `APPLY_BUILDING_DAMAGE_MODIFIER`;
  StackableType 3, so overlapping auras do not stack).
- **Legolas · E · Guardian Wind**: cast on the spot (no target) with the source
  clip (animation state 91); at its toolscript event (29) every ally within 12 m,
  Legolas included, takes 1% of missile damage for 12 s (child spell 30,
  `APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER` 0.01, `START_TIMER` 12). Cooldown 30 s;
  replacement orders wait for the full cast like Kingsfoil's; Stop cancels it.

Gimli's Sunder and Capture, Legolas's Trueshot and Elven Speed, and all four of
Gandalf's (Blinding Light, Flame Shield, Negate Ability, Rain of Fire) need
knockback, stuns, tower capture, ground targeting or spell negation. They show on
the card disabled, with an English description after the manual and the source
cooldown. The Elven Lightbearer's Protection does the same. Rules and validation
are in [the skirmish notes](SKIRMISH.md#build-58-the-rest-of-the-good-tech-tree-and-the-fate-heroes).

## Dwarf units — build-78

Select a Dwarf Axethrower or Shieldbreaker to see its passive ability icons in
the command panel. Before research they are dim. They stay dim during research
and after cancellation. Once
research finishes the icon lights up; the passive applies automatically on attack
and has no activation hotkey or cooldown. Veterans and later recruits of that
team receive the upgrade. Mixed selections retain heroes' ability panels.

Build-79 simplifies every ability tooltip to its name and effect description.
Research costs remain on the Forge's research buttons. A selected burning building
shows the original fire icon under its health bar, with a brief effect description;
the icon disappears when the fire goes out. Ongoing fire damage emits a separate
Burn event, preserving health, Fate and destruction handling without playing a
weapon hit sound. Actual axe impacts retain their hit sounds.

| Unit / passive | Forge cost / time | Required Citadel | Effect |
|---|---|---|---|
| Shieldbreaker · Shield Break | 100 food / 100 ore / 90 s | 2 | Every melee hit removes 1 armor for 20 s; up to five independent stacks. Further hits replace the oldest stack. |
| Axethrower · Razored Edge | 100 food / 100 ore / 90 s | 3 | Missile DPS 10; the axe continues into one enemy behind its target within 5 m, at 70% damage. |
| Axethrower · Flaming Axe | 125 food / 125 ore / 90 s | 4, plus Razored Edge | Missile DPS 10.35; keeps the follow-through hit and sets enemy buildings alight. |

Shield Break's harmful status reports the armor reduction, stack count and timer.
The Armor bonus column turns red and its tooltip includes Shield Break. Armor can
fall below zero. Stacks expire independently even after the attacker dies; friendly
force attacks and invincible targets receive no armor break. Iron/Steel/Mithril
Blades and Plating continue to affect both Dwarf types through the existing Forge
research chains.

Follow-through axes use the incoming line, the next enemy's body width and a clear
line of fire. Each target receives its own armor, matchup and Guardian Wind check;
allies, hidden units, different elevations and targets outside the 5 m reach are
excluded. The axe's upgrades and damage are captured at release, survive shooter
death and cannot be changed by finishing research while it is in flight. Missile
upgrades leave the Axethrower's close-range melee weapon alone.

Flaming axes use source projectile 33. Building fires use the original Torch effect
(Effects 8) with its animated flame, smoke and ember atlas frames. Simulation time
drives the animation, including pause. The simulation uses source FireDamagePerSecond
0.35 as normalized intensity added over the attack's duration, capped at 1; each
building's FireArmor, FireExtinguishRate and MaxFireDamagePerSecond control its
resistance, decay and damage. This intensity model, the 70% chain multiplier reading,
the body-width line test and particle emission positions are adapters; the original
engine's exact fire and chain geometry have not been measured. Worker fire-fighting
and the original fire spreading rules are outside this change.

The source adapter is `tools/dwarf_abilities.py`: Techniques 12/13/32, Weapons
48/49 and Shield Break gear 26 → Spells 59/60. The child spell supplies armor -1,
timer 20 s, StackableMax 5 and recycling of old stacks. The Forge now implements
23 of the 32 Good techniques. Rebuild shared caches with `python tools/content_pack.py`;
this workspace's pack is already refreshed.

Validation: 34 focused Dwarf checks (including the verified content pack), 1,194
full simulation checks, the Python import suite, 14 Research UI smoke checks and
13 Dwarf Hall smoke checks. Research smoke covers locked, pending, cancelled and
researched passives, both axe tiers and a visibly burning enemy Citadel. Its
`-CapturePath` also saves locked/researched Shield Break, Razored Edge and fire
screenshots alongside the final researched Axethrower panel.

### Flaming Axe appearance — build-80

The original flame projectiles (32 right / 33 left) reuse the normal axe models
(1318 / 1319). Their difference is the attached `EFFECT_GRISHNAKH_TORCH` (252),
with MidFire, MovingFlames, Flame01, Smoke and Embers tracks, alongside the
normal axe-swipe trail (362). The adapter now imports and animates these original
atlas frames, sizes and color keys on building-bound axes. Particle positions
around the axe remain a bounded visual adapter.

Unit targets use the normal axe appearance after Flaming Axe research, including
Razored Edge follow-through hits. The visual choice is captured at release;
changing targets cannot alter an axe already in flight. The unit's held axe mesh
does not change. Rebuild other shared caches with `python tools/content_pack.py`;
the local pack is already refreshed.

The focused simulation/cache suite passes 40 checks, the Python suite 198, and
Research smoke 19. The latter checks visible source torch sprites on building
shots and normal projectiles against a unit after researching Flaming Axe. With
`-CapturePath`, it saves both flight appearances beside the existing screenshots.

### Axe rotation — build-82

The normal (17/18) and flaming (33/32) projectile files each contain one motion
key at time zero with an angular speed of 1,080 degrees per second. The left
axe's source axis is -X; the right's is +Z. The adapter converts these axes with
the mesh's source-to-glTF mapping, exports them in the projectile's Spin profile
and applies rotation relative to the direction of flight. The axe skeleton's
projectile clip is static; the rotation belongs to the projectile motion data.

Rotation uses absolute simulation time from release, so render rate does not
affect speed and pause freezes it. A follow-through segment keeps the first
release's rotation clock. Normal unit shots and flaming building shots both
rotate; the building-only flame selection remains unchanged. Exact legacy motion
composition is still an adapter; remaining LPO motion fields stay uninterpreted.

The shared content pack is rebuilt locally. Validation covers 12 projectile
presentation checks, 41 Dwarf simulation/cache checks, 200 Python tests, 14 Dwarf
Hall input checks and 21 Research input checks. The input checks observe the
rendered axe's changing orientation during flight for both appearances.

### Ranger techniques — build-83

The Wilderness Outpost researches Camouflage (E) and Eagle Eye (R), each for
75 food, 75 ore and 90 seconds, requiring Citadel 2. Both apply to living Rangers
and later recruits. Existing Outpost blade and leather chains already upgrade
Ranger attack and armor, replacing earlier tiers.

The Ranger shows Camouflage on Q, Eagle Eye on E, and innate Detection on R.
Locked abilities remain dark; researched passives are bright and disabled.
Tooltips contain only the effect. Q applies Camouflage to every selected Ranger
who has researched it and finished recharging, without a target cursor.

Camouflage traces gear 14 and spell 23: stop, play Idle variant 3, hide, become
passive and play sound event 375; movement or attacking reveals the Ranger.
Deactivation starts its source three-second recharge. Its child spell's icon
appears in the status bar while hidden. Friendly and detected Rangers use 50%
opacity (a presentation adapter; SET_VISIBLE does not specify friendly opacity).
The pose uses G_Ranger_Idle_03, interpreting the source variant as one-based.

Rangers detect normal invisibility within their source 20 m sight radius;
Eagle Eye's sight multiplier doubles it to 40 m. Detection is shared by the team,
updates as detectors move or die, and does not remove the target's Camouflage.
The Ring disables detection in its own spell and cannot be revealed. Undetected
enemies disappear from the world, minimap, picking and combat/AI targeting;
observers see both sides. This implements unit visibility, without terrain fog
or forest sight multipliers. Detection uses center distance without terrain
occlusion; exact legacy fog/detection geometry remains unverified.

Validation: 34 Ranger simulation/cache checks and source-data import tests.
The Research smoke adds ten Ranger checks, including source icons, research
hotkeys, pose, opacity, status, sound availability, cooldown and hidden/detected
enemy rendering and minimap dots. Capture stages include ranger-locked,
ranger-camouflaged, ranger-enemy-hidden and ranger-enemy-detected.

## Elven Sanctuary — build-94

The [Good compendium](../references/original-game/GOOD-COMPENDIUM.md), the
source atlas and the installed spell graph define these abilities. Sanctuary
training and Citadel requirements were already implemented: Elven Archer at
Citadel 3, Lightbearer at Citadel 4.

| Unit / ability | Research | Behavior |
| --- | --- | --- |
| Archer: Elven Cloaks (passive E slot) | Wilderness Outpost, Citadel 3; 165 food / 165 ore, 90 s | Continuous invisibility, including movement and attacks. Normal Detection reveals it. Existing and future Archers receive it. |
| Lightbearer: Protection (Q) | Innate | Target a living ally, including self. Absorbs 150 damage for up to 50 s; recasting replaces the old shield. Range 10 m; recharge 35 s. |
| Lightbearer: Negate Magic (W) | House of Lore, Citadel 4; 75 / 75, 90 s | Ground target, range 15 m, radius 7 m, recharge 10 s. Removes hostile Shield Break / searing spells from allies and Protection / Guardian Wind / Camouflage from enemies. Permanent research and innate auras remain. |
| Lightbearer: Light of Lothlórien (E) | House of Lore, Citadel 4; 125 / 125, 90 s | Ground target, range 16 m, radius 5 m, recharge 45 s. Enemy units in the area receive 22 damage per second for 5 s (110 total); allies, neutral units and buildings are excluded. |

Archer Etched → Mystic → Runic Blades and Improved → Hard → Studded Leather
retain their existing replacement bonuses (+1, +2, +3); Eagle Fletching retains
its 1.2× missile range. Those chains and range apply to existing and future
units. Lightbearer research does not grant those Archer military bonuses.

Area targeting draws the actual radius on the terrain at the pointer. It hides
over the console and on cancellation or acceptance; unreachable areas turn red.
Out-of-range casters approach the clicked point before casting. Replacement
orders wait for the full animation. Stop or death interrupts casting; recharge
is spent only after the effect applies. Exact clicked X/Z coordinates
are recorded in the command and simulation digest. Protection releases at tick
13 of its 31-tick clip, Negate Magic at tick 13 of 34, and Light at tick 3 of 31.
These timings come from the source clips' toolscript events.

Protection absorbs melee, projectile and spell damage before health changes,
with overflow applied normally. Searing damage bypasses armor, following the
source UPDATE_HEALTH action; it follows affected enemies out of the original
area, persists after the caster dies, and refreshes rather than stacks
(STACKABLE_TYPE_REPLACE). Dispel respects the source ally/enemy filters and can
reach concealed enemies. Shields, cloaks and searing spells have status icons.

Presentation uses the original icons, cast clips, cropped effect atlas frames,
size/color timelines and sound events (Protection 101, Negate Magic 265,
Light 267, projectile launch 278). Effect 350 follows shielded units; 349 is the
Protection cast; 308 marks dispelled targets; 75 and 402 draw separate light
beam/drop and ground emitters; 104 marks the caster and 106 the seared enemy.
Lightbearer projectile effect 167 replaces the placeholder sphere. Archer
arrows retain their source mesh and trail. Ranged impact audio now selects the
source weapon/material row, including buildings, instead of a mixed hit pool.

The source art, spell parameters and links are retained in the content pack;
emitter placement, blending and absolute projectile speed remain bounded
presentation adapters, without a claim of pixel parity. Elven Cloaks follows
the technique's IsVisible=0 field continuously; original executable trigger
behavior still needs a controlled live comparison. Terrain fog remains absent.

Validation: 38 simulation/cache checks, three source-import tests, and eleven
in-engine checks of the icons, locked research slots, cast animations, effect
attachments, audio availability and both preview radii. Rendered captures are
under `converted/qa/elven-build94/`. Commands from the repository root:

```powershell
python tools/content_pack.py
python -m unittest discover -s tools -p 'test_elven_abilities.py'
converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --elven-ability-checks --cache converted/skirmish
prototype/run.ps1 -ElvenSmoke
prototype/run.ps1 -ElvenSmoke -CapturePath converted/qa/elven-build94/elven.png -Resolution 1280x800
```

### Build-98: range passive, shield visibility and aiming

Eagle Fletching now occupies the Archer's source slot 3 (R), beside Elven
Cloaks in slot 2. Its source icon and description are visible before purchase,
dim and disabled; research lights the passive icon. The existing 1.2× range
effect remains in the research system, so adding the icon does not apply the
bonus twice.

Protection's target Aura texture is emitted around the protected unit as twelve
light strips, with increased accumulated opacity for visibility at RTS camera
distances. It follows movement and disappears when the shield is depleted,
expires or is dispelled. The previous single strip was too faint to read.

The source LIQDCRSR descriptors specify image-relative hotspots. Protection and
other spell cursors use an arrow tip at (1, 3) in their 64×64 art; the old shared
center hotspot placed the visible arrow about 30 pixels away from the click.
The interface exporter now retains each source hotspot and the panel applies
it. Protection, Negate Magic and Light use their own original cursor art.
Healing uses the same hotspot fix. The negative cursor retains its centered
hotspot. Hover validation and accepted clicks continue to use one pointer ray.

Validation: 41 Elven simulation/cache checks, source-export tests, sixteen
Elven UI checks and all 38 healing UI regressions pass. Rendered shield and
Archer-panel captures are in `converted/qa/elven-build98/`.

## Nature's Haven — build-102

Nature's Haven already recruits Beornings (100 food, 70 ore, 3 population) and
Huorns (235/200, 5 population; Citadel 4). Their abilities now work:

| Unit | Control | Effect | Research at House of Lore |
|---|---|---|---|
| Beorning | W or right-click a wounded ally | Healing Herbs: 75 HP at up to 8 m; 6 s recharge. Idle Beornings approach wounded allies within 15 m. Hold Ground only heals within casting range and keeps the hold order. | Innate |
| Beorning / Bear | Q | Switch forms after the original transformation animation. Bear has base 420 HP, 17 DPS and 2 armor; human has 235 HP, 7 DPS and 0 armor. Shared 20 s recharge. | Bear Form: 125/125, 90 s |
| Bear | W | Savage Blow: 75 spell damage in a 260° forward arc, radius 4 m. Launches susceptible enemies away; 15 s recharge. Allies and neutrals are excluded. | Savage Blow: 125/125, 90 s; Bear Form and Citadel 4 |
| Huorn | Q | Treeform: stays rooted for 8 s and heals 20 HP/s in addition to normal regeneration. Enemies within 10 m on activation are dazed for 8 s and cannot move, attack or cast. Stun immunity protects them. The Huorn then plays its exit animation; 25 s recharge. | Treeform: 100/100, 90 s; Citadel 4 |

Transformation keeps the same unit ID, selection, population, research, orders
waiting for the animation, and each ability's cooldown. Herbs and Savage Blow
have independent timers despite sharing W. Nature attack and armor upgrades
apply again for the new form. Bear is exported as an alternate model/rule set;
it is never offered for recruitment. The new body must fit the terrain and
nearby units both before casting and at completion. A failed completion leaves
the old form and spends no recharge. Stop can cancel a transformation before
completion; death cannot finish it or restore health.

The simulation preserves health percentage across forms, including place of
power bonuses. This is an adapter: the captured full-health transformation is
verified, but damaged-health conversion in the original executable remains
unmeasured. Knockback interprets source force 7 and angle 25° as launch velocity,
using fixed mean force and 9.8 m/s² gravity; its displacement stops at terrain,
buildings and bodies. The vertical arc is visual, keeping ground collision safe.
Original knockback distance and variance still need a controlled comparison.

Treeform follows the spell graph's **10 m child radius**, rather than the outer
spell's 7 m display field, and its activation pulse rather than a continuous
reapplication. It uses the original enter/loop/exit clips, six root-shard meshes and
bound sprite layers. An unused 13 m white ground layer is excluded. Negate Magic
removes allied daze. All abilities retain source icons and sound events, and
Treeform/daze show timed status icons. Root placement adapts legacy emitter
transforms to the victim's body radius. Healing Herbs has its own source cursor.

Validation includes 37 simulation/cache checks, three source-import checks,
rendered HUD/input checks, and existing combat, Ranger, Dwarf and Sanctuary
regressions. Captures are in `converted/build102-validation/`.

```powershell
python tools/content_pack.py
python -m unittest discover -s tools -p 'test_nature_abilities.py'
converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --nature-ability-checks --cache converted/skirmish-maps/Sandbox
prototype/run.ps1 -NatureSmoke
```

### Build-104: manual Healing Herbs

W and the Healing Herbs button reserve the selected Beorning for manual targeting.
Automatic healing pauses while the cursor is active. An automatic heal that has
not applied yet can be redirected without spending cooldown; a completed heal
keeps its normal recharge. Right-click healing also redirects an automatic
wind-up. Cancelling targeting resumes automatic healing. Other manual casts keep
the build-103 rule that replacement orders wait for their animation.

Ability grid hotkeys follow physical Q/W/E/R positions, including non-Latin
keyboard layouts. Targeting reservations and cancellation are simulation commands and enter the
state digest, so replay does not depend on unrecorded HUD state. Nature smoke
checks W, the button, the targeting cursor, clicking a wounded ally, cancellation
and redirecting an automatic cast, in addition to the existing unit checks.
