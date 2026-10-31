# Global Fate abilities — build-125

Click the Fate orb to open the Good faction's five powers. Q/W/E/R select the
top row; A selects Giant Ent. Click the battlefield to cast, or a living friendly
hero for Heroic Legacy. No unit selection is required. Right-click or Escape
cancels targeting without payment. V/Back closes the page. Commands issued while
paused take effect when simulation resumes; payment and validation happen then.

| Key | Power | Fate | Effect |
| --- | --- | --- | --- |
| Q | Blind | 2 | Enemy units within 10 m cannot attack for 10 s. Movement and abilities remain available. Blind immunity is respected. |
| W | Brambles | 3 | An 8 m patch lasts 25 s. Moving enemies lose 35 HP/s; standing still avoids damage. Allies are unharmed. |
| E | Heroic Legacy | 3 | A friendly hero gains 35% weapon damage, +2 armor and 50% maximum health, preserving the current health fraction. Cannot stack. The hero cannot be recruited again after dying. |
| R | Blessed Wind | 2 | Friendly missile units within 10 m gain projectile knockback for 20 s. Released projectiles retain it after expiration or the shooter's death. |
| A | Summon Giant Ent | 7 | Places a controllable 2,000 HP Giant Ent on clear ground for 120 s. Its source melee/missile attacks, collision and immunities are imported. No population cost. |

Each power has a 10 s recharge. Blind, Brambles, Blessed Wind and Giant Ent cannot
be recast by the same player while that power remains active. Invalid targets,
occupied summon placements, insufficient Fate and duplicate Legacy blessings
spend nothing. The HUD shows costs, recharge/active state, original targeting
cursors, terrain area previews and unit status icons. Blind and Blessed Wind
can be dispelled using the existing Lightbringer ability. Original sprite
layers, Brambles mesh, Giant Ent model, projectiles and spell sounds are exported
through the shared content pack.

Bots whose personality enables special abilities use the same validation and
payment as players. They blind groups of dangerous enemies, lay Brambles on
moving groups, support groups of missile troops with Blessed Wind, summon Ents
for large fights or established assaults, and use Legacy on healthy experienced
heroes in combat. They ignore unseen units, avoid repeat buffs, keep a Fate
reserve for heroes/gear outside base emergencies, and immediately order summoned
Ents into the fight. UsesHeroes still controls hero recruitment independently.

## Provenance and adapters

`tools/global_powers.py` exports source `GlobalSpells` rows 4/6/5/3/1 and their
`Spells`, target filters and action graphs. The five-slot order agrees with the
live Good menu in `references/original-game/GOOD-LIVE-OBSERVATIONS-2026-10-23.md`.
Blessed Wind uses its spell action's 20 s timer; the global row also contains an
unused 30 field. Raw rows and actions remain in `combat/rules.json`.

Global recharge is interpreted as seconds from release, independently per power,
with an active-instance limit; exact legacy global blocking/recharge scheduling
has not been verified against Rings.exe. Knockback uses the source nominal force
and angle with the existing deterministic ballistic adapter, without random
variance. Brambles samples motion and area membership once per tick and uses the
strongest overlapping patch. Permanent Legacy's resurrection restriction follows
the captured tooltip. Sprite emission is bounded and adapted; Blessed Wind's
source hair atlas has a 1/64 edge overhang, clipped during export. Terrain fog is
not simulated, consistent with the rest of the prototype. Only the currently
playable Good faction's powers are implemented.

## Validation

```
converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --global-power-checks --cache converted/skirmish-maps/Sandbox
prototype/run.ps1 -GlobalPowerSmoke
prototype/run.ps1 -GlobalPowerSmoke -CapturePath converted/global-power.png -Resolution 3840x2160
```

The smoke covers the real menu, physical hotkeys, empty selection, paused casts,
invalid targets, cancellation, all five effects, source visuals and moving the
summoned Ent through player input. It disables the Sandbox's unlimited Fate
refill inside its fixture to check real costs.
