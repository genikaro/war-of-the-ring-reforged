# Hero experience — builds 106–108

Good heroes start at level 1, earn experience by dealing damage, and advance to
level 10. The selected hero's console and right-edge portrait show the level and
progress through the current level. Hover either for XP remaining, total XP and
the current level's bonuses. Reaching a level plays the original Good hero
level-up effect (335) and sound (412). At maximum level the bar is full.

Build-108 matches the original selected-hero console: a left-aligned level label
above a blue XP bar the same size as health, with the original
`console_g_doublebarbackground` frame and `console_g_staminabar` fill. A centered
counter shows whole XP within this level (for example `123/750`); fractional XP
remains in the tooltip. Maximum level shows `MAX`. Status icons sit below the
XP row. Both bars scale with the existing console and interface-size setting.
Layout references: `Screens/GameConsole.lsr` and local original-game captures
`campaign-004-gimli-panel.jpg` and `037-frodo-level-seven-hero-panel.jpg`.

| Level | Total XP | Extra max HP | Attack bonus | Extra armor (Frodo) |
| --- | ---: | ---: | ---: | ---: |
| 1 | 0 | 0 | 0% | 0 (0) |
| 2 | 500 | 25 | 5% | 0 (0) |
| 3 | 1050 | 50 | 10% | 1 (1) |
| 4 | 1650 | 75 | 15% | 1 (1) |
| 5 | 2300 | 100 | 20% | 2 (1) |
| 6 | 3000 | 125 | 25% | 2 (2) |
| 7 | 3750 | 150 | 30% | 2 (2) |
| 8 | 4550 | 175 | 35% | 3 (2) |
| 9 | 5400 | 200 | 40% | 3 (3) |
| 10 | 6300 | 250 | 50% | 4 (3) |

The source thresholds are costs for each next level: 500, 550, 600, 650, 700,
750, 800, 850 and 900 XP. They must be accumulated, rather than interpreting
900 XP as enough for level 10. The new level's bonuses replace the previous
bonuses. Added max HP comes before place-of-power health multipliers; current
health preserves its percentage of the maximum. Leveling keeps the current
order, cast, swing and cooldowns. Passive-ability suppression does not remove
level bonuses. Attack and armor bonuses appear in the console's green columns.

Unit damage awards the hero's source `ExperiencePerUnitDamagePercentage`
(1.0), building damage its `ExperiencePerBuildingDamagePercentage` (0.4), times
the active weapon's `ExperienceModifier`. Ranged hits award XP when the arrow
lands; Legolas's close-range sword uses its own weapon modifier. Damage abilities
carry their spell or special weapon modifier (Trueshot and Rain: 0.1). The Dark
Orb's source `EXPERIENCE` place modifier multiplies hero XP by 1.25 while held.
XP retains fractional precision; no extra kill award is added.

Safeguards: only damage reaching health earns XP, capped at the target's remaining
health; shields, missed arrows and friendly fire cannot farm XP. Dead heroes
cannot gain levels or heal from released attacks. Ordinary units remain without
hero progression, including the Sandbox's Gimli-shaped Hero Dummy.

## Revival — build-107

Defeated heroes retain their level and exact accumulated XP when resummoned at
the Citadel, including fractional progress toward the next level. The returning
hero has full health at its restored maximum and the same level's armor and
attack bonuses. Current place-of-power bonuses apply to the new life normally.
Restoration does not play a new level-up effect or copy temporary spell effects.

Progression is saved separately by player and hero type within the match. Repeated
defeats update that record; older corpses cannot overwrite a later life's XP.
Cancelling or retrying recruitment preserves the record. Recruitment still checks
the normal costs, population and uniqueness rules and follows the rally order.
A hero with no saved record starts at level 1; a new match starts with no records.
Cross-match and campaign carryover remain outside this change.

## Original revival behavior, verified October 28

The original preserves the hero's level and accumulated experience when the hero
is defeated and resummoned, including progress partway through the current level.
A level-6 hero returns at level 6, rather than starting at level 1.

Read-only executable trace: defeat handling calls `0x44b54c` from `0x437d5b`
and `0x437e21`. For a hero, that routine calls `0x43a524` to snapshot current XP
(`unit+0xd64`), level (`+0xd68`), next/previous XP boundaries (`+0xd6c/+0xd70`)
and level bonuses, then stores the snapshot per player and hero unit type through
`0x44b5ef`. New hero initialization at `0x433e34` looks up that saved record
using player and unit type (`0x44b509`), restores it through `0x43a564`, and sets
health from the restored hero's maximum (`0x433e65`). This is distinct from the
fresh level-1 fallback used when there is no saved record.

The prototype follows this save-and-restore rule from build-107. Its integer level
bonuses and XP boundaries are derived from the saved level and imported rules.

## Source trace

`tools/hero_experience.py` imports each hero's `Units.lqr` level 2–10 thresholds,
HP/armor/damage bonuses and XP rates. The separate five-row `HeroExperiences`
table is obsolete for these heroes and is deliberately unused.

Read-only disassembly of the owner's `Rings.exe` confirms the cumulative threshold
updates in `0x43851e`, progress calculation in `0x438944`, and preservation of
health fraction via `0x43acd7` / `0x446769`. Damage award paths include `0x4371ca`
and spell damage at `0x54e66e`; the experience-place multiplier is `0x548968`.
`combat/rules.json` retains the source trace and per-unit progression tables.

Prototype adapters: XP uses integer millionths and existing approximate armor,
weapon timing and spell-damage rules. One large award immediately resolves all
crossed levels, while the original function advances one level per invocation.
Source floating-point rounding and neutral/friendly-fire behavior still need
controlled original-game comparison. Original revival XP retention is verified
by the executable trace above; a live revival comparison has not yet been run.

## Validation

- `SimulationTests --hero-experience-checks --cache converted/content/good`
  checks combat XP, thresholds, fractional progress, stat replacement, health
  percentage, max level, weapons, spells, places, buildings, overkill, shields,
  friendly fire, death, ordinary units, HUD text and deterministic replay.
- `SimulationTests --hero-revival-checks --cache converted/content/good` checks
  combat/scripted defeats, actual production, repeated revivals, partial XP,
  maximum level, player/type separation, full restored health, current place
  bonuses, fees, cancellation, same-tick instant recruitment and replay. It also
  verifies level-6 revival for all five imported Good heroes.
- `python -m unittest discover -s tools -p test_hero_experience.py`
  checks the imported original thresholds and the distinct Frodo armor table.
- `./prototype/run.ps1 -HeroExperienceSmoke` exercises actual combat, console
  and portrait bars, levels 1/2/10, original level-up presentation, selection and
  resummoning a defeated level-6 hero through the Citadel's recruitment button.
  Add `-CapturePath converted/hero-experience.png` for rendered captures.
