# War of the Ring Reforged Roadmap

Updated Oct 21, 2026 · @Evgenii

Phases 1 and 2 implementation are complete. All 54 maps decode through EOF, including 752 decals. Helm's Deep and a decal-heavy Birth of the Uruk-Hai scene render and validate. An initial reference review has corrected prototype rendering bugs; controlled original-game fidelity validation remains open. The user has accepted Phase 3's movement and combat-spacing baseline. The command panel, explicit orders and first imported melee slice are implemented in the Godot 4 C# sandbox (engine chosen Oct 19). The Elven Archer ranged slice is implemented with projectile resolution. A bounded original-game command/combat comparison and correction pass remains open. Original armor/range formulas remain unresolved.

An [original-game reference library](../references/original-game/README.md) now
contains 26 visually retail-compatible images, the original manual and addendum,
editor help and recording candidates, with provenance and checksums. The
[reference audit](../references/original-game/REFERENCE-AUDIT.md) excludes all 48
prerelease images and modified sources from active use. Retained web captures
have unverified provenance and are not final-build baselines; controlled
original-game screenshot comparison and gameplay verification remain open.

The [first local original-game session](../references/original-game/LOCAL-SESSION-2026-10-19.md)
adds 30 build-identified captures from **1.01.0011**, including command tooltips,
Good HUD, recruitment, a melee encounter and technology trees. These are
provisional observations: installation authenticity is unverified, captures are
1920 × 1080 JPEGs, and stills do not establish combat timing. They inform the
next tasks below without closing the original-game fidelity gates.

The [prototype reference review](../references/original-game/PROTOTYPE-REVIEW.md)
corrects missing runtime mipmaps, ignored lake vertex colours, spawned team
colours and selection disks. Menu/launch-time views and capture metadata make further
comparisons repeatable. Lighting, weather, water animation, original UI and
the complete animation-event/appearance adaptation remain open. The three demo
types now have the melee-event and equipment-visibility subset described below.

## Current checkpoint

The Godot sandbox launches from the repository root with `./prototype/run.ps1`.
Setup, controls and known limits are in [the prototype README](../prototype/README.md).
**Build-76 (Oct 25):** Fate is earned in battle, by the rule decoded from Rings.exe:
damage dealt to enemy units and buildings x 0.8 (tower arrows a quarter), divided by
(Fate held + Fate queued + 1) x 1.1, x1 at home rising to x1.45 at the farthest map
corner, x1.4 with the Soulfire Monolith; 500 make a point, up to 20. The first 3-Fate hero
costs about 4100 damage dealt at home. The Fate tooltip and a new observer column show
progress. Warpost corruption (Evil), the Power Acquisition lobby setting and bots
training heroes remain. See [skirmish notes](../prototype/SKIRMISH.md#build-76-fate-earned-in-battle).

**Build-63 (Oct 24):** bones a clip leaves unkeyed go back to rest instead of keeping
the previous clip's pose. The Ranger's hood and cape and the Elven Archer's hair are not
keyed in their idle clips, so after a run they stayed raised or streaming while idle.
Unit animation players are now deterministic. See
[skirmish notes](../prototype/SKIRMISH.md#build-63-bones-a-clip-leaves-unkeyed-return-to-rest).

**Build-62 (Oct 24):** the unit panel's Attack is damage per hit, matching the
Sandbox dummies' floating numbers; the original showed the weapon's damage per second.
Hovering Attack gives the damage per second and the attack speed from very slow to very
fast (Gondor Swordsman 10 per hit, 10 per second, very fast). Hits show whole numbers. See
[skirmish notes](../prototype/SKIRMISH.md#build-62-attack-shows-damage-per-hit).

**Build-61 (Oct 24):** four fixes from the owner's playtest. The Ranger (and the
Rohan Rider's and Shieldbreaker's runs) no longer runs ahead of its centre: root motion
is removed from the rest position, not the clip's first key. A source unit-pair row now
replaces the type matchup instead of stacking with it, so the Rohan Rider hits a Gondor
Swordsman for 150%, not 187.5%. Towers list their weapons in the info panel (the
Ballista Tower its arrows and its bolt). The walk/run button shows the current pace.
See [skirmish notes](../prototype/SKIRMISH.md#build-61-four-unit-fixes-from-playtesting).

**Build-60 (Oct 24):** each unit runs at its own speed. The original has no speed
column: units move as far as their Run clip's root joint travels. The prototype keeps
those ratios and scales them so the Gondor Swordsman still runs 3 m/s, so the Rohan
Rider runs 4.54 m/s, Legolas 4.04, Aragorn 3.66, Frodo 2.82 and the Huorn 2.12.
Walking scales each unit's own run speed by its walk/run ratio. The bot reference match
is shorter: Gimli beats Aragorn on Bilbo's Bluffs at tick 16483 (was 21983). See
[movement notes](../prototype/MOVEMENT.md#build-60-per-unit-run-speeds).

**Build-59 (Oct 24):** training dummies in the Sandbox. Four dummies stand beside the
blue base, one per damage type (Cutting, Piercing, Blunt, Hero), with 1000 health, no
regeneration and no attack; they never fight back. Their buttons add 1 armor, set armor
to 0 and restore health, for every selected dummy; the 0 key selects all four. They are
drawn half again as big and gilded, and every hit on one floats its damage with the type
multiplier, for example "-26.8 (125%)". See
[skirmish notes](../prototype/SKIRMISH.md#build-59-training-dummies-in-the-sandbox).

**Build-58 (Oct 24):** the rest of the Good tech tree, and the Fate heroes, checked
against the Good compendium. Citadel 3, 4 and 5; Nature's Haven, the Elven Sanctuary
and the House of Lore; the Ballista Tower with its source bolt. The Beorning, Huorn
(x3 against buildings), Elven Archer and Lightbearer. Gimli, Aragorn, Legolas and
Gandalf at the Citadel for Fate, which is a new counter; the source hero limit (Citadel
2-5 hold 1, 2, 3, 3 of Gimli, Aragorn and Legolas). Gimli's Siege Aura and Legolas's
Guardian Wind work; the other new hero abilities show disabled. All 32 Good techniques
are listed and 22 simulated, every stat chain with tiers that replace each other (+1,
+2, +3, as captured), plus Eagle Fletching, both axes and Dwarven Engineering. The Build
menu gets an Advanced page (W). Fate is not earned yet, so outside the Sandbox the heroes
wait; bots climb the tree but do not train the new units yet. See [skirmish notes](../prototype/SKIRMISH.md#build-58-the-rest-of-the-good-tech-tree-and-the-fate-heroes).

**Build-57 (Oct 24):** the River Raid stall. A four-bot match froze at 11:07: a
walled-in unit asked to make way searched every free cell of its region for a
refuge, flooding the whole map for each. A cell that a failed search already reached
is now skipped without searching again; the refuges chosen are the same (identical
Bilbo observer digest), and the match plays to the end. See [skirmish notes](../prototype/SKIRMISH.md#build-57-the-river-raid-stall).

**Build-55 (Oct 24):** observer Stats tab. The spectator overview gets a third tab
(S): each player's food and ore gathered over the match and units killed and lost,
with tooltips by unit type plus buildings razed and lost. A tower's kills count for
its side; a friendly kill is a loss only; a surrender's demolition is no loss. The
tally stays out of the state digest. See [skirmish notes](../prototype/SKIRMISH.md#build-55-observer-stats-tab).

**Build-54 (Oct 23):** places of power. A side alone in a place's 13 m zone takes
it and keeps it until another side does; every unit of the holder carries its
source modifier (armor, regeneration, maximum health, attack, siege damage, ability
cooldown; speed, sight, costs, Fate and experience are shown but not simulated yet).
A random place draws one of the 19 random-eligible places from the match seed.
Bots send the source scout war party's five soldiers after their `.rai` Scouting
gate. See [skirmish notes](../prototype/SKIRMISH.md#build-54-places-of-power).

**Build-53 (Oct 23):** the Raider trait. A Raider's assaults go for Mills and Foundries
first, and while an assault fights at the enemy base it sends a small raid, 3 to 6
soldiers by its `.rai` Harassment (source `AIWARPARTY_RAIDING`), at enemy workers
and economy buildings away from towers and defenders; raiders kill the workers
first and run from bigger forces. Tuned against bots until it plays no worse than a
plain bot on three maps; a bot's all-out base defence keeps raids small, so how
much they annoy a person is the owner's playtest. See [skirmish notes](../prototype/SKIRMISH.md#build-53-the-raider-trait).

**Build-52 (Oct 23):** health regeneration from the source tables. A unit regains
its animation state's `HealthRegenerationRate` (1 HP a second idle, walking or in an
attack stance, nothing running, gathering or mid-swing, 2 while building or
repairing; heroes 0.167 mid-swing) times its `HealthRegenerationMultiplier` (heroes
2.25), never while the One Ring is on, as its source actions say. A Hard bot's
retreated soldiers now heal at home and rejoin parties above half health. The
HP-per-second reading of the rates is an adapter until the original game is
measured. See [skirmish notes](../prototype/SKIRMISH.md#build-52-health-regeneration).

**Build-51 (Oct 23):** bot traits. Each bot slot takes traits on top of its style,
picked in a Traits menu on the setup screen or written `-Bots Gimli+Defender`:
Rusher, Expansionist, Defender, Relentless, Marksman and Demolisher (parties raze
buildings under fire). Rusher excludes Defender and Expansionist. Fixed on the way:
a party member chasing an enemy that was no longer a threat held the whole
regrouping party, which froze a match for 18 minutes. See [skirmish notes](../prototype/SKIRMISH.md#build-51-bot-traits).

**Build-50 (Oct 23):** bot order discipline. A bot's orders wait for a swing
under way to land instead of cancelling it, it never repeats an order a unit is
already carrying out, and only urgent orders (falling back, Hard pulling out a
badly wounded soldier) interrupt. Idle bot soldiers guard like a player's and help
any fight within 16 m. A party fights the soldiers and towers hurting it before any
building, then picks buildings by kind (military before economy). Soldiers left
idle in the field rejoin the party out or walk home; a Hard bot's badly wounded
soldier retreats one time in three and rejoins parties above half health. Traits
move to build-51. See [skirmish notes](../prototype/SKIRMISH.md#build-50-bot-order-discipline-idle-guard-and-target-priority).

**Build-49 (Oct 23):** bot difficulty and style. Difficulty now sets how well a bot
plays (think rate, builders, regrouping, target choice, when a party falls back;
Hard gets no income bonus) and a style in an Aggression / Expansion / Tech triangle
what it plays (first attack, party size, workers, expansions, upgrade, research).
Every named bot has its own style and a lobby description, shown in each bot
item's tooltip and a bot card on the setup screen. Hard beats Easy of the same
style from both bases on Sorrow and Dust and Blood. Fixed on the way: in fights
lasting 30 s bots gave up on enemies they were hitting and cancelled their own
swings, which froze Hard armies. Traits follow in build-50.
See [skirmish notes](../prototype/SKIRMISH.md#build-49-bot-difficulty-and-style).

**Build-48 (Oct 23):** one content pack for every map. The Good faction's models,
rules, HUD art, production, research and AI tables now live once in
`converted/content/good/` (`tools/content_pack.py`); a skirmish map cache holds
only its map and names the pack, and its navigation covers every unit body of both
factions. New units, buildings and research reach all maps by rebuilding the pack;
older self-contained caches still load. All 32 map caches are re-imported. See [skirmish notes](../prototype/SKIRMISH.md#build-48-one-content-pack-for-every-map).

**Build-47 (Oct 23):** bots climb the tree. A bot upgrades its Citadel after
4:00-10:40 depending on aggressiveness, builds the Ranger Post, Forge and
Wilderness Outpost, prefers the Rider and Shieldbreaker once its army is a full
party, and researches for the units it has. In the reference match Gimli reaches
Citadel 2 at 7:05 and fields Riders, Shieldbreakers and Rangers with all four
researches, and wins at 17:46. See [skirmish notes](../prototype/SKIRMISH.md#build-47-bots-climb-the-tree).

**Build-46 (Oct 23):** Forge and Wilderness Outpost research. The Forge
researches Iron Blades (+1 attack) and Iron Plating (+1 armor) for the Swordsman,
Rider, Axethrower and Shieldbreaker, the Outpost Etched Blades and Improved
Leather for the Ranger: 75 food, 75 ore, 90 s each, applied to units already
trained. The +1 comes from the source per-unit modifiers; whether the technique's
x1.2 also applies needs one in-game capture. See [skirmish notes](../prototype/SKIRMISH.md#build-46-forge-and-wilderness-outpost-research).

**Build-45 (Oct 23):** the Tier 2 units. The Barracks trains the Rohan Rider once
Citadel 2 stands, the Dwarf Hall the Dwarf Shieldbreaker once a Forge stands, and
the Ranger Post the Ranger, all with the captured costs and stats. Units trained to
one rally point now gather around it instead of queuing for its cell, and a wide
body (the Rider) re-picks a siege spot fighters already block. Abilities and Forge
research remain. See [skirmish notes](../prototype/SKIRMISH.md#build-45-the-tier-2-units).

**Build-44 (Oct 23):** Citadel 2 and the Tier 2 buildings, from the October 22
captures. The Citadel upgrades in place to level 2 (115 food, 105 ore, 50 s, 1000
health) and the Camp to the War Camp (+4 population); the upgrade heads the
building's queue as in the original and pauses its training. Citadel 2 opens the
Wilderness Outpost, Ranger Post and Forge on the Build page; every building and
unit now checks its source prerequisites and locked buttons name what is missing.
Next: the Tier 2 units (Rider, Shieldbreaker, Ranger), then Forge research.
See [skirmish notes](../prototype/SKIRMISH.md#build-44-citadel-2-and-the-tier-2-buildings).

**Build-43 (Oct 23):** no more frozen fighters. Units standing where their cell
centre is too close to a building (after razing it, at work spots) found nothing
connected to them and waited for good; combat now plans from the nearest cell they
can stand on. Bots spread attacks over the enemies in range, at most four on each,
instead of sending a whole army at one, and a party member razing the target takes
the party in instead of holding the march. Freezes of 3 s or more in traced bot
matches fell from 18-75 per match to 1-4.
See [skirmish notes](../prototype/SKIRMISH.md#build-43-no-more-frozen-fighters).

**Build-41 (Oct 23):** bot armies regroup and keep walking. Saruman's army
stopped and started because its Swordsmen and Axethrowers, ordered to attack a
defender more than about 18 m away, never found an attack position within the
bounded search and stood waiting; wide bodies now walk a full route toward a
target more than 12 m away first (player orders too). Bot parties now muster on
their route before leaving and regroup on open ground every 25 m instead of
filing through narrow places. On Bilbo's Bluffs (Saruman against Aragorn) the
party stands still on move orders 7% of the time instead of 39%, marches 4 m
wide instead of 43 m, and Aragorn wins at 19:23 where build-40 stalemated.
See [skirmish notes](../prototype/SKIRMISH.md#build-41-bot-armies-regroup-and-keep-walking).

**Build-40 (Oct 22):** owner playtest fixes. Each bot expansion keeps half an
assault party as its guard; the first Foundry goes on the home pile the bot's
own miners stand on (it used to skip it and build at an expansion's pile). The
frame-rate drop from about 13:00 was a Swordsman trapped in a ten-cell pocket by
its Barracks, waiting for room every tick (21 ms a tick by 12:00): trained units
now exit into an open region, the exit search is one pass, and trapped soldiers
no longer hold up assaults. Two freezes in the reference matches are fixed: home
armies stop chasing enemies far from every building, and a full party's worth of
idle soldiers reinforces a party still out. Aragorn now wins Bilbo's Bluffs at
17:54 and the Wasteland free-for-all at 18:33.
See [skirmish notes](../prototype/SKIRMISH.md#build-40-expansion-guards-home-foundry-trapped-spawns).

**Build-39 (Oct 22):** skirmish setup screen (step 4). `./prototype/run.ps1
-Skirmish` opens a lobby: every multiplayer map with its player count and a preview
of its start positions, You / a bot personality / Closed for each start position (no
human means watching), and the start speed. You can play any slot; closed slots get
no base. Restart replays the setup, and Setup returns to it. Launch flags still
skip it. See [skirmish notes](../prototype/SKIRMISH.md#build-39-skirmish-setup-screen).

**Build-38 (Oct 22):** bots expand from 10-15 minutes (by aggressiveness) with a
Citadel by unclaimed resources, then every 10 minutes up to three. They rebuild a
lost Citadel, go all in when their army can no longer grow, and surrender when
they can never field an army again. Workers keep away from resources near enemy
buildings. Three freezes found by watching are fixed: reachability judged from a
blocked cell, enemy workers counted as a threat, and give-up timers reset by
alternating targets. The reference matches decide again (Gimli wins Bilbo's
Bluffs at 33:26 and Wasteland at 48:15). See [skirmish notes](../prototype/SKIRMISH.md#build-38-bots-expand-go-all-in-and-surrender).

**Build-37 (Oct 22):** an 8-bot free-for-all on MP08_Rift fell to 1 FPS once
armies grew. Speed bursts now stop after 25 ms of simulation per frame, and Godot
no longer stacks catch-up frames, so the game slows instead of the frame rate (the
match panel shows "CPU-bound · running at 2.3x"). Two play changes cut large-map
cost. A map change re-plans only the routes it touches. A stuck unit tries a
local detour first, and its full replan stops after 32,768 nodes. In Rift's big
fights the median FPS went from 2 to 13-20, and ticks from 18-28 ms to 11-17 ms.
The new routes leave two reference bot matches in a stalemate (Gimli against
Aragorn on Bilbo's Bluffs, the Wasteland free-for-all): the bots cannot recover
once their economy is gone. See [skirmish notes](../prototype/SKIRMISH.md#build-37-large-map-performance).

**Build-36 (Oct 22):** spectator HUD in the style of other RTS observer modes. A
foldable overview at the top right lists every player under an Economy tab (bank,
income per minute, population, workers, army, buildings) or a Units tab (portraits
with counts); E, U and Tab switch and fold it, and a player's name centres the
camera on that base. The console's empty action grid becomes the match panel: map
name, game clock, players left, pause button and a notched 1x-16x speed slider
(`-` and `+` still work). The match itself is unchanged (same digest). See [skirmish notes](../prototype/SKIRMISH.md#build-36-spectator-hud).

**Build-35 (Oct 22):** any multiplayer map with any number of players (skirmish
setup steps 2 and 3). All 32 maps import into their own caches (`--map`,
`--all-maps`, `run.ps1 -Map`), with eight source slot colours and a bot on every
side but the player's. A 4-bot free-for-all on Wasteland plays to a result. Bots
attack only buildings they can reach, and every bot side's idle units now chase
noticed enemies as team 1's always did, so the observer match changes: Gimli now
wins at 33:33 (digest 75D5F383...). Mighty Silverlode's rope bridges are too narrow
for armies. See [skirmish notes](../prototype/SKIRMISH.md#build-35-any-multiplayer-map-any-number-of-players).

**Build-34 (Oct 22):** a wide body's attack-position search stops after 1,024
nodes instead of 2,048 (owner's choice after an experiment; remembering failed
searches was tried and dropped). Exhausted searches cost half as much; the observer
match now ends at 30:05 with a new reference digest (31E28C29...). See [skirmish notes](../prototype/SKIRMISH.md#build-34-smaller-attack-position-search-budget).

**Build-33 (Oct 22):** combat targeting pass, no change in play. A unit grid
(8 m buckets kept current by unit moves) serves the distance-bounded threat scans,
and the wide-body attack-position search runs once instead of twice. Combat time in
big fights roughly halves; the observer match keeps its digest. See [skirmish notes](../prototype/SKIRMISH.md#build-33-unit-grid-for-threat-scans-one-pass-attack-position-search).

**Build-32 (Oct 22):** third performance pass, no change in play. Route smoothing's
swept-body test solved each row's cells directly instead of scanning a long
path's bounding box, which halves mid-game unit movement; combat position and
opponent scans skip units that cannot matter. Mid-game ticks fall 10-27% and the
observer match keeps its digest. See [skirmish notes](../prototype/SKIRMISH.md#build-32-swept-body-tests-and-exact-scan-trims).

**Build-31 (Oct 22):** corpse removal and stuck-unit replanning. Bodies sink away
60 s after death (owner's choice; the original fades them after 10 s) and their
models stop rendering and animating. A stuck unit's route fallback no longer
re-floods a region an earlier failed search already proved closed, which removes
most remaining pathfinding spikes without changing any route; the observer match
ends exactly as on build-30 (same final digest). See [skirmish notes](../prototype/SKIRMISH.md#build-31-corpse-removal-and-stuck-unit-replanning).

**Build-30 (Oct 22):** five fixes from an owner skirmish, Warcraft III style.
Attack-move razes enemy buildings it passes (units first) and then marches on. A
building ordered far away goes down, and is paid for, only when a worker reaches
its spot; a ghost marks pending and queued placements, the player's idle units
step aside, and a worker that can no longer lay it says why. The Build page and
training buttons take grid hotkeys (QWER/ASDF/ZXCV by key position, letters on
the buttons). Queued orders show a dashed line and the original gather-point
flags. A Citadel rally point on a Mill, Foundry, well or ore pile sends new
workers straight to gathering.
See [skirmish notes](../prototype/SKIRMISH.md#build-30-owner-playtest-fixes).

**Build-29 (Oct 22):** pathfinding performance, first pass, after an owner report
of frame drops in large matches. `-Profile` prints where simulation and frame time
go. A building laid or razed no longer wipes the pathfinder's edge caches across
every body graph, only around itself, and A* runs on flat cached arrays. Every
route is unchanged (the observer match reproduces build-28's final digest), and
mid-game ticks cost about a third: 1.7-2 ms instead of 5-6 ms, with 16x running at
17-26 fps instead of 5-11. Stuck-unit replanning and combat targeting are next.
See [skirmish notes](../prototype/SKIRMISH.md#build-29-pathfinding-performance-first-pass).

**Build-28 (Oct 22):** observer mode and game speed. `-Observe -Bots Gimli,Aragorn`
runs a bot on every side and nobody plays: any unit can be inspected but not
commanded, and a panel shows each player's economy, buildings and bot status.
Speed runs from pause to 16x (`-`/`+`, panel buttons, `-Speed`) by stepping the
fixed-tick simulation several times per frame, so fast-forwarding never changes
the match. Watching bot against bot exposed four bot bugs, now fixed: stragglers
blocking new assaults, workers leading squads in circles, unreachable units
stalling an army forever, and food starvation. Gimli beats Aragorn at 30:51.
Validation: a 9-case observer smoke, 3 new synthetic checks and all regressions.
See [skirmish notes](../prototype/SKIRMISH.md#build-28-observer-mode-bot-against-bot-and-game-speed).

**Build-27 (Oct 21):** the skirmish AI. The enemy trains workers, builds its
base, trains Swordsmen and Axethrowers, and sends assaults that raze the player's
buildings, defending its own base when attacked. Source data: the shipped `.rai`
personalities and the `Buildings`, `Units` and `AIWarParties` AI columns (the
Assault party is 5 to 12 units, 80% melee). The decision logic is in Rings.exe
and not recovered, so it is an adapter on those numbers; the default personality
is a medium adapter (Difficulty 1, knobs at 50) and `-AiPersonality` picks any
`.rai`. Against a passive player it razes the Citadel at 5:48 of game time. No
scouting, raids, flanking or heroes yet. Validation: a 9-case AI smoke on Bilbo's
Bluffs, 11 synthetic checks and all regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-27-the-skirmish-ai).

**Build-26 (Oct 21):** the skirmish can be won and lost. The captured stock
skirmish's "destruction" game type decodes from the localized menu text as Razing
(text 4401): a player who owns no standing building, foundations included, is
defeated whatever units remain, and the last one standing wins. A centred panel
with the original result lines ("You won!" / "You lost!", in English) pauses the
game and offers the original Continue and Restart. Validation: a 9-case victory
smoke, 9 new synthetic checks and all regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-26-the-razing-win-condition).

**Build-25 (Oct 21):** Watchtower defenses. A completed Watchtower shoots its
source weapon, `BuildingDefenses` row `G_WATCHTOWER` with weapon 40 (14 cutting
damage per second, 19 m), at the nearest enemy in range once a second, from the
tower skeleton's projectile joint (joint flags are bit masks: `J_Projectiles`
carries `1 << 11`, the Building Projectile flag). Arrows use the source model,
launch sounds and trail. An idle armed unit that is shot turns on the tower.
Adapters: centre-distance ranges, 25 m/s arrows without line of fire, no damage
type, and no garrison, so the tower stays at its base rate. Validation: a 10-case
defense smoke, 13 new synthetic checks and all regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-25-watchtower-defenses).

**Build-24 (Oct 21):** axes thrown at buildings now draw, and force attacks no
longer provoke retaliation. See [skirmish notes](../prototype/SKIRMISH.md#build-24-axes-at-buildings-and-force-attacks-without-retaliation).

**Build-23 (Oct 21):** besiegers face the building. A melee attacker kept its
approach heading and hit the wall sideways, since a unit's melee facing is chosen
from its order in the presentation and no case covered an attack on a building. It
now turns to the building's centre once it is in range and striking. Missile units
were already correct. Validation: the siege smoke grows to 17 cases, sampling every
besieger's rendered yaw, plus all regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-23-besiegers-face-the-building).

**Build-22 (Oct 21):** owner playtest fixes. The armed Attack command now forces
its building target, of any team, so force-attack works on buildings instead of
falling through to an attack-move at the ground under one's own; a plain
right-click stays contextual, leaving friendly buildings to the repair order.
Right-clicking the minimap orders the selection to that point, as the original
manual describes, with Ctrl to march and Shift to queue, and without moving the
camera. Validation: the siege smoke grows to 16 cases and the minimap smoke to
15, plus all regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-22-force-attack-on-buildings-and-minimap-orders).

**Build-21 (Oct 21):** combat against buildings. Every skirmish building has
health and armour from the source `Buildings` table, start buildings included,
which become completed prebuilt construction sites at load. Right-clicking an
enemy building with armed units sends them to its footprint to swing their
ordinary attack clips at it, missile units shooting it; a ruin frees its ground,
producer, delivery point, model and, for a start building, its population and
prerequisites, while a Mill or Foundry reverts to an unclaimed resource site.
Workers repair a damaged owned building with the source hammer clip, restoring a
full bar over its own BuildTime. Adapters: no damage type, attacker limit or
retaliation for buildings, and repair is free. Validation: a 14-case siege smoke
plus 722 simulation, 34 worker-cache and 170 Python checks and all other
regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-21-combat-against-buildings-and-repair).

**Build-20 (Oct 21):** work ranges and world health bars. A worker may now stand
at most one body radius plus one navigation cell beyond a target's footprint,
measured against the footprint itself; the earlier circle took its radius from
the footprint's farthest corner, so crews mined and unloaded from two rows out
on a pile's flat sides. The standing band on a flat side drops from 2196 mm to
1000 mm, a single row. The floating `Mill 42% / 300/1200` label over a
foundation is replaced by the original's world health bar, a billboarded black
strip with a green fill at the foot of a unit or building; construction health
climbs with the work done, so the bar doubles as the progress bar, and it shows
only below full health. Start and neutral structures take no damage yet, so they
never carry one. Validation: 722 synthetic simulation and 34 worker-cache checks,
a new real-map check that every miner stands within a body and a cell of the ore
pile's footprint, and all skirmish and Helm's Deep smokes. See [skirmish notes](../prototype/SKIRMISH.md#build-20-footprint-work-ranges-and-world-health-bars).

**Build-19 (Oct 21):** Frodo trains at the Citadel (150 food, 100 ore,
population 0, 36 s, one per player) with his source abilities: the One Ring
toggle (Q: half opacity, passive, hidden from enemies, 2 HP/s drain floored at
1 HP), Sting (×1.35 against orcs and goblins) and Heroic Aura (1.5× ability
recharge within 15 m). Living heroes get the original right-edge portrait strip
from `GameConsole_Hero_Portraits.lsr` with health and experience bars; a click
selects the hero and a double click also centers the camera. Hero experience,
levels and regeneration are not simulated. Validation: 722 synthetic / 789 cached simulation and 170
Python checks, a 13-case Frodo smoke and all skirmish and Helm's Deep smokes. See [skirmish notes](../prototype/SKIRMISH.md#build-19-frodo-and-the-hero-portraits).

**Build-18 (Oct 21):** map navigation. Edge scrolling (the original manual's
"pushing" on screen edges), minimap press-and-drag, a white outline of the
visible ground on the minimap, and the original ore, well and place-of-power
minimap markers. Validation: a 12-case minimap smoke plus all regressions. See
[the prototype README](../prototype/README.md).

Next (after build-28): step 2 of the skirmish setup plan (any number of player
slots), owner playtest against the AI, then scouting, raids and flanking war
parties, Watchtower garrisons, hero experience and levels, and the Axethrower's
second throw.

**Build-17 (Oct 21):** owner-defined Warcraft-style gathering. A raw ore pile
gives one ore per pickaxe hit, five per trip. A completed Mill or Foundry admits
one worker at a time for two seconds, who comes out with 10 food or 9 ore. Loads
are credited after the source put-down clip at the Citadel. Mining, pick-up,
carrying and put-down use the worker's source resource states and sacks;
builders loop the source repair (hammer) clip. Workers choose work spots beside
the target. The F4 debug menu gains +1000 food/ore and instant build/train
cheats. Validation: 704 simulation, 33 worker-cache and 168 Python checks; 27-case
gathering and 28-case construction smokes plus all skirmish and Helm's Deep
regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-17-warcraft-style-gathering-building-animation-and-debug-cheats).

**Build-16 (Oct 21):** owner playtest found construction icons unclickable. The
panel hid and re-showed them every frame, cancelling real clicks that span
frames; each control now has a single visibility owner, and the smokes press and
release across frames. At the owner's request, workers use a Warcraft-style Build
button (B) that swaps the unit commands for the Tier 1 buildings and Back; Esc
steps back. Paging for later tiers is deferred.
Validation: 23-case placement check including cross-frame clicks and Esc/B
navigation, plus all skirmish, Helm's Deep, simulation and Python regressions. See [skirmish notes](../prototype/SKIRMISH.md#build-16-build-menu-and-working-construction-clicks).

**Build-15 (Oct 20):** a completed Dwarf Hall trains the Dwarf Axethrower
(60 food, 10 ore, population 2, 340 ticks; 140 HP, 9 DPS, 0.5–10 m). The rules
export uses an explicit ranged list instead of the Archer-only name check (the
source Uruk-Hai missile weapon stays disabled), and projectile model, launch
sounds and trail are exported and rendered per ranged unit. Helm's Deep rules are
unchanged apart from that format. The Axethrower's right-hand throw uses an
undecoded LMD event type 33 and is skipped for now.
Validation: 680 synthetic / 747 cached simulation checks, 33 worker-cache checks
(including the cached Axethrower throw), 167 Python tests, a 9-case Godot Dwarf Hall
check and unchanged Helm's Deep arrow results; fresh and upgraded caches pass. See [skirmish notes](../prototype/SKIRMISH.md#build-15-dwarf-hall-trains-the-dwarf-axethrower).
Next: Frodo at the Citadel (hero model, rules and abilities), decoding event
type 33 for the second throw, and Watchtower defenses.

**Build-14 (Oct 20):** the skirmish roster exports the Gondor Swordsman, so a
completed Barracks trains it through the build-12 queue (50 food, population 2,
280 ticks; 155 HP, Attack 10, Armor 0, matching `local-020`). Trained bodies
larger than the producer's anchor now exit through a cell they can stand on.
Validation: 680 synthetic / 747 cached simulation checks, 30 worker-cache checks,
167 Python tests and an 18-case Godot placement/training check, plus all
regressions; upgraded and fresh caches pass. See [skirmish notes](../prototype/SKIRMISH.md#build-14-barracks-trains-the-gondor-swordsman).
Next: generalize the Elven Archer projectile path for the Dwarf Axethrower
(Dwarf Hall), then Frodo at the Citadel.

**Build-13 (Oct 20):** workers place all seven Tier 1 buildings in the original
panel order. Open-ground placement uses the map's source building passability
grid, walker clearance, structure/resource spacing, unit blocking and the
source `MaxRadiusAllowingNewBuildings` proximity rule. Placed footprints become
runtime navigation obstacles, rasterized per body profile. Foundations share the
existing construction, resume, cancellation and scaffold presentation. A completed
Camp adds 11 population; a new Citadel accepts deliveries and trains workers.
Validation: 678 synthetic / 745 cached simulation checks, 30 worker-cache checks,
167 Python tests, a 15-case Godot placement check and all skirmish/Helm's Deep
regressions; upgraded and fresh caches pass; 1280x800 and 4K captures inspected. See [skirmish notes](../prototype/SKIRMISH.md#build-13-tier-1-building-placement).
Next: export the Gondor Swordsman and Dwarf Axethrower and train them at the
Barracks and Dwarf Hall, then Frodo at the Citadel.

**Build-12 (Oct 20):** the Citadel trains Human Workers with source costs, time,
population and capacity. Clicks pay and reserve population immediately; the
queue holds five, and selection-panel portraits or Backspace cancel with a full
refund. Trained workers appear outside the Citadel, inherit Walk/Run and follow
a right-click rally point marked by the original banner. The original purple
training bar is used. `skirmish_production.py` imports all Tier 1 producers and
names unavailable units (Frodo not exported, other heroes beyond Tier 1).
Validation: 655 synthetic / 722 cached simulation checks, 30 worker-cache checks,
166 Python tests, a 17-case Godot production check and the structure/command/
gathering/construction/orders/ability regressions; 1280x800 and 4K captures
inspected. Refresh existing caches with the production command. See
[skirmish notes](../prototype/SKIRMISH.md#build-12-citadel-worker-training).
Next: general building placement for the Camp, Barracks, Dwarf Hall and
Watchtower, then Swordsman/Axethrower training through the same queue.

**Build-11 (Oct 20):** brightened ambient lighting and softened the owner's
reported dark terrain patches. Runtime shading lifts baked terrain intensity
while preserving hue, alpha and fully lit values; cast shadows remain. Source
assets/caches and simulation geometry are unchanged. Preview settings and terrain
adjustment audits are recorded in captures. Clean build, 61 structure checks,
Helm's Deep gameplay/import checks and 1280x800/4K visual checks pass. Restart;
no cache refresh required. See [skirmish notes](../prototype/SKIRMISH.md#build-11-brighter-environment-and-softer-terrain-shadows).

**Build-10 (Oct 20):** box selection includes buildings and neutral interactables
when no friendly units are selected. Units take priority, including Shift-retained
units; structure selection remains a single inspector. Visible triangle overlap
supports partial roofs and uses click depth checks. Empty and reverse drags keep
the expected selection behavior. Validation: 61 structure and 49 command Godot
checks pass, including rendered 1280x800 and 4K selection. Build-9 caches work
unchanged. See [skirmish notes](../prototype/SKIRMISH.md#build-10-structure-box-selection-with-unit-priority).

**Build-9 (Oct 20):** all eight resource sites and the map's Place of Power are
selectable as neutral objects with original portraits, outlines and live stock.
Source selectability excludes scenery; objects without health omit HP/armor.
Structure palettes contain their own actions, and enemy/neutral unit inspection
has an empty palette. HP and resource counters center against bars/icons despite
font minimum heights. Unit groups, worker orders and the global pace control
remain available. Validation: 164 Python tests, 51 structure and 49 command Godot
checks, plus construction/order/ability regressions. Fresh and upgraded caches
pass at 1280x800 and 4K. Local cache updated; older caches use the documented
skirmish upgrade. See [skirmish notes](../prototype/SKIRMISH.md#build-9-neutral-inspection-and-command-palettes).

**Build-8 (Oct 20):** corrected excess collision clearance in the owner's
Citadel/Foundry gap. Versioned skirmish footprints use a single swept-body circle
against the source polygons; the real worker crosses directly in 134 ticks
instead of a 262-tick route with a 12 m detour. Citadel, Mill and Foundry now
select separately from units, with visible-mesh picking, footprint outlines,
original portraits, health/armor/progress/resource details, enemy inspection and
direct cancellation of a selected foundation. Unit groups remain independent.
Existing skirmish caches need the documented upgrade; the local cache is updated.
Validation: 633 synthetic / 700 cached simulation checks, 30 worker-cache checks,
162 Python tests and 26 structure Godot checks, plus gathering/construction/
command/order/ability regressions. Fresh and upgraded caches pass; 1280x800 and
4K views are inspected. See [skirmish notes](../prototype/SKIRMISH.md#build-8-building-clearance-and-selection).
Next: remaining Tier 1 construction and production queues. Building damage/repair,
worker work animations, fog and the scripted opponent remain open.

**Build-7 (Oct 20):** corrected the owner's worker-stalling report for gathering
and ordinary movement near buildings/resources. Off-center bodies now reconnect
to the route graph through a short, collision-checked ground leg when their
cell-center routes are disconnected. Long raw/construction cases reduce maximum
route stalls from 8,884/7,754 ticks to 7/8; all workers keep delivering. Move
orders recover the exact stranded position. Validation: 624 synthetic / 691
cached simulation checks, 29 worker-cache checks and expanded 23-check gathering
smoke, plus Construction/Command/Orders regressions. No asset refresh required.
See [movement notes](../prototype/MOVEMENT.md#build-7-reconnecting-from-an-off-center-ground-position).
Remaining Tier 1 buildings, building selection and production are still next.

**Build-6 (Oct 20):** Mill/Foundry construction is connected to worker portraits
and resource-site placement, including a green/red ghost, imported costs and
prerequisites, shared worker progress, original scaffold stages, Stop/resume and
75% cancellation refunds. Completed owned buildings unlock food and improved
ore gathering; cargo returns to the Citadel. Rules, work, costs, footprints,
ownership and fractional resources replay deterministically. See [skirmish
notes](../prototype/SKIRMISH.md) for source values and explicit adapter policies.
Validation: 613 synthetic / 680 cached simulation checks, 160 Python tests,
23 construction and 19 gathering Godot checks, plus Command/Orders/Ability
regressions. Fresh and upgraded caches pass; 1280x800 and 4K views are inspected.
Next: remaining Tier 1 construction, building selection and production queues
with population reservation and cancellation checks. Building combat/repair,
worker work animations, fog and the scripted opponent remain open.

**Build-5 (Oct 20):** raw-ore gathering is connected to the Bilbo's Bluffs
skirmish. Workers gather carried loads, return to an owned Citadel and credit
the live resource bank. Shared deposits, partial final loads, cancellation,
queued orders and economy replay state use the deterministic simulation.
Imported source values and explicit timing/range interpretations are documented
in [skirmish notes](../prototype/SKIRMISH.md). Food gathering requires Mill
construction; production and the rest of the Good Tier 1 tree remain open.
Validation: 588 simulation checks, 158 Python tests and 19 Godot skirmish checks,
including all four workers completing a delivery and exact resource conservation.

**Build-4 (Oct 20):** first skirmish-loop step. `prepare_prototype.py --skirmish`
imports MP02 Bilbo's Bluffs into its own cache: the map's editor markers decode
(Start Point 0/1), each side gets a Citadel 1 and four Human Workers, and the four
ore/well pairs from the map remain in the scene. The console shows the original
population/food/ore strip with the default start state (4/10, 115 food, 325 ore,
matching the captured start). Economy, construction and production are still
absent; they follow in builds 5-8. The Helm's Deep cache is byte-identical after
the importer refactor. Validation: 566 simulation checks, 156 Python tests,
11 skirmish smoke checks and the Command/Orders/Ability/Melee smokes; start
captures inspected at 1280x800 and 4K. See [skirmish notes](../prototype/SKIRMISH.md).

**Build-3 (Oct 20):** Greater Kingsfoil uses original Effects 171 texture layers
for expanding rings, leaves, clouds and sparkles, with finite, pause-aware visual
playback following the target. The original Kingsfoil cursor appears while
targeting; enemies/full-health units show the invalid cursor, and UI/cancel/cast
returns the normal pointer. Passive labels are removed, with active skill
hotkeys at the bottom-right and the cooldown centered separately. Validation:
566 simulation/presentation checks, 147 Python tests and 38 Ability input checks;
healing captures inspected at 1280x800 and 4K. Legacy particle motion remains a
documented adapter in [ability notes](../prototype/ABILITIES.md).

**Build-2 (Oct 20):** Aragorn's source ability slice is implemented. Q and the
original Kingsfoil icon target injured allies/self, approach within 10 m, heal
250 HP on source toolscript tick 8 and recharge over 17 seconds. The original
clip/sound, health popup and cooldown overlay are connected. Fury and Immunity
are equipped passive trial upgrades; W/E do not activate spells and R is empty.
Fury replaces weapon Attack 20 with 30, with the source Wraith/Black Rider bonus.
Immunity flags are imported; status producers, poison cleansing, original spell
effects and purchase progression remain open. See [source trace and limits](../prototype/ABILITIES.md).
Validation: 626 C# checks with the local cache, 145 Python tests, 25 Ability,
46 Command and 27 Orders Godot checks, plus the 420-tick Melee smoke pass.
The hero console was inspected at 1280x800 and 4K.

**Build-1 (Oct 20):** the top-right overlay shows the build number, current local
date and live FPS, with readable scaling through 4K. Menu and Walk/Run use native
artwork proportions in the matching console slots. Double-click/Ctrl-click
selects all living same-type units on the clicked unit's team; Shift-click
adds/removes units. Walk/Run is now global for the issuing player's army,
independent of selection, and new units inherit its setting. Orders, queues,
targeting and attack timing continue. Validation: 524 simulation checks,
46 Godot command checks and 27 Godot Orders checks pass; 1280x800 and 4K captures
were inspected. `AGENTS.md` requires one build-number increment per implemented
change set.

Latest checks: 137 Python tests, eight JavaScript tests (unchanged), and 420 total
synthetic/cache C# checks pass. Cache checks include imported combat definitions,
four repeated duels and runtime masks matching the initial
snapshot, all ten building centers being
blocked and all six friendly units traversing the four Helm's Deep surfaces
without overlap, plus opposing six-unit squads swapping between the clearing
and each walkway, plus safe startup for all 36 demo units. The cache contains
1,342 separate surface nodes and 48 terrain entrances, preserving elevation
and base 350 mm body support along mesh edges, with additional per-type
clearance/headroom/object masks for the three demo bodies. A 54-map audit
(`tools/verify_walker_masks.py`) matches the painted walker bits to the
original walker region graphs.
Eight-way weighted routes now use straight diagonal motion at the same speed as
cardinal moves, with blocked-corner checks and swept-body crossing reservations.
Wide-ground orders now use full-route any-angle smoothing, next-tick redirects,
rolling body checks, local steering and minimum-travel formation assignment.
The six-unit open-field fixture finishes in 211 ticks versus 274, with no waits.
[Movement notes](../prototype/MOVEMENT.md) document the benchmark and limits.
Single-file and mesh routes retain constrained coordination; deck links
remain eight-way. Rendering follows the recorded path between fixed ticks and turns
gradually. An 80 FPS motion smoke check verifies interpolation and turning with
the same 20 Hz simulation, input and combat checks.
Godot builds without warnings or errors; input and combat pass over 420 fixed
ticks. An elevated preview was inspected, and actual Godot right-click dispatch
selects a surface node and terrain beneath it in a separate headless check. The original four sandbox
GLBs validated without errors or warnings. Original-game fidelity remains unverified.

Combat now reserves individual reachable melee and waiting positions, maintains
allied spacing, holds settled fighters still and faces them toward their targets.
Moving/blocked targets trigger approach replanning, and surviving attackers can
continue onto nearby enemies. Ground approaches use the any-angle follower when
supported. Three six-unit squads per side populate the demo, with a unit-focused
default camera and 1/2/3 squad selection. A separate Godot combat smoke check
requires all three friendly squads to fight without overlaps.

The selected-unit command panel now exposes Move, Attack, Attack-move, Stop and
Hold Ground, plus Guard, Patrol and Force Attack. The visible bottom row is
Attack, Hold, Guard, Patrol. A single Attack/A action resolves to pursuit,
force attack or attack-move based on enemy, neutral/friendly or ground targets.
Move and Stop remain available without visible buttons; F/B shortcuts are removed.
Original console art, portraits and command/cursor assets are traced through
source UVA crops into the hashed local cache. The console scales proportionally
with height, supports a saved 75–150% size setting and fits 4K/ultrawide displays.
Editable groups 1–0 support Ctrl assignment, Shift additions, recall, lead-unit
centering and death pruning. Shift-click queues formation waypoints; queued
orders are recorded and replayed. Twenty-seven actual Godot input checks cover
these additions, alongside 34 command checks. Explicit intent survives temporary
combat routes; Attack-move resumes after combat, force moves ignore retaliation,
and stationary orders never chase. Names, health, tooltips, active orders and
disabled commands are visible. Twenty-two actual Godot input checks cover all
three blue squads, cancellation, pause/resume and dead selections. The
[command notes](../prototype/COMMANDS.md) record behavior, original references,
intentional shortcut/layout differences and remaining limits.

Debug tools now show live simulation/selection/order state, with pause/resume,
a single paused tick, restart, camera/focus buttons, interface sizing and a
collapsed current-controls guide. Menu visibility is independent of the guide;
obsolete shortcut labels and the old static sandbox banner are removed.

The October 20 command correction adds the agreed **F2 Walk/Run** toggle and
original run-icon button. Build-1 makes pace global to the player's army; changing mode preserves
orders, queues, targeting, swing timing and the camera. Source root-track ratios
set walking speeds; default running stays at 3 m/s. The visible Hold label is
now **Hold Position**. The [command behavior matrix](../references/original-game/COMMAND-BEHAVIOR-MATRIX.md)
records evidence and intentional choices; original timing comparisons and
Aragorn's Q/W/E slots are connected in build-2; other heroes remain open. The 4K Walk UI was inspected; 27 Orders checks,
34 Command checks pass. The 206-hit historical melee result predates the larger bodies.

The October 20 collision correction imports scaled Main boxes for the demo:
Aragorn 561 mm, Elven Knight 927 mm and Uruk-hai 693 mm radii. Body contact,
formations, passage clearance, swept reservations and melee spacing now use
these sizes; selection rings show collision radius. Rules format 3 and
`--rules-only` rebuild body-specific clearance over the existing map graph.
Combined 18-unit orders reproduced jitter/stalling from cell-center fallback
and sealed arrival slots. Large units now retain wide-ground avoidance and
exchange blocked/crossing formation slots. Enemy clicks inspect live combat
stats with a red ring and disabled controls, preserving friendly groups.
See [collision provenance and limits](NOTES-unit-collision.md).

The army-engagement correction distributes overflow attackers, shares nearby
active threats and replans stalled steering. Three units plan per tick using
bounded goal-set searches; region caches warm while loading. A new 18-versus-18
fixture requires broad participation and replay stability. The measured fixture
uses about 52% less simulation CPU time than the reported-bug baseline; actual
Godot reproduction checks all-ally input, both armies, body safety and step times.
See [engagement measurements](../prototype/ENGAGEMENT.md).

Further owner feedback reopened crowded melee and attack-move responsiveness.
Friendly personal space now compresses to a solid half-envelope core; terrain,
enemy contact and resting formation spacing retain full envelopes. Nearby fights
can recruit rear units across march lanes, and regrouping/idle units immediately
react to reachable enemies. Combat routes obey the same encounter area as motion,
and steering retains a passing side. Aragorn's demo model is enlarged 12%, with
an 800 mm full radius and 2,347 mm height; raw source bounds remain preserved.
Both full-army Attack and Attack-move checks now record 18 hitters per side.
See the [crowd follow-up](../prototype/ENGAGEMENT.md#crowd-and-attack-move-follow-up).

The owner confirmed the crowd changes feel much better. Idle player units now
automatically watch a 10 m radius, assist nearby allies, and use a 12 m pursuit
limit before returning near their resting origin. Move arrival resets that
origin. Stop cancels the current action and returns to idle guarding from the next
tick; Hold Position remains stationary. Focused awareness, support, return and
replay checks cover the follow-up idle-unit report.

**Owner acceptance (Oct 20):** the crowd/melee changes, 10 m default idle guard
and Stop-to-idle behavior work well and are approved as the current checkpoint.
This accepts the play feel of this slice; original-game fidelity remains open.

The three types now use imported health (485/350/240), armor, melee DPS,
classification and attacker caps. Original clip durations and `.lmd` hit events
drive integer windup, damage and recovery. Cancellation retains cooldown;
multi-hit damage preserves cycle DPS. Rendering seeks from simulation state;
equipment visibility and three converted impact sounds are connected. The
[combat notes](../prototype/COMBAT.md) record provenance and explicit armor,
range and event-scale approximations. Four source-unit duels replay identically.
The current melee Godot check records correctly timed hits over 420 ticks with all
three squads fighting safely; a 4K capture was inspected. Original-game formula
and duel comparisons remain open.

Weapon ratings now implement the owner's 125%/75% cycle and neutral Hero damage,
with all 16 matchups stored in the hashed rules cache. Equal types and Hero
targets use 100%. Single selection shows the original type icon and live Attack,
Armor and Victories, following the saved original-game HUD. Enemy lethal hits
increment the attacker's victory counter once. See the
[reference note](../references/original-game/COMBAT-RATINGS-2026-10-19.md) for the
diagram, addendum confirmation, linked guide and unresolved formula policies.

## Accepted baseline: movement and combat spacing

**User acceptance (Oct 19):** movement, group pathfinding and placeholder combat
spacing are good enough until actual combat and the command interface are
implemented. Keep the six-squad demo and unit-focused default camera. Preserve
the 171 simulation checks and existing Godot checks as regression coverage.

This accepts the prototype milestone, not complete navigation or original-game
combat fidelity. Further movement/spacing polish is deferred. Revisit it only
for regressions or when verified combat ranges, unit sizes or new gameplay
actually require a change.

## Implemented milestone: unit commands and first data-driven combat

**Goal:** use the command panel to control a small battle between the existing
Aragorn, Elf Knight and Uruk-hai types, with distinct imported stats and attacks
whose damage lands at the appropriate point in the animation.

1. **Implemented: explicit orders and a functional command panel.**
   - Introduce recorded order kinds for Move, Attack, Attack-move, Stop and Hold
     Ground. Keep the existing right-click controls and route buttons, hotkeys
     and contextual clicks through the same fixed-tick command path.
   - Define order state separately from the current target and path. Stop clears
     the current order/pursuit; Hold Ground fights within reach without chasing;
     Attack-move resumes its destination after an engagement. Specify explicit
     movement versus automatic retaliation, target death and order replacement
     so combat cannot accidentally discard the player's intent.
   - Build the selected-unit command panel with names, health, tooltips, shortcut
     hints and visible active/available commands. Check the original manual,
     addendum, `Hotkeys`, `Controls` and source interface assets for layout,
     labels, bindings and behavior before calling them faithful. Guard, Patrol,
     basic queues, control groups and original console artwork are now implemented;
     hero abilities and full interface reconstruction remain subsequent work.
   - Verify cancellation during combat, Hold versus chasing, Attack-move
     resumption, mixed selections, dead units and identical replay state for
     equivalent button/hotkey/contextual orders.
2. **Implemented: replace the three demo types' placeholder stats with imported definitions.**
   - Resolve `Units` to `Weapons`, `DamageTypes`, `UnitToUnitModifiers` and model
     data. Export a minimal hashed runtime rules cache from the owner's existing
     data. Keep source IDs/provenance; quantize values at the adapter boundary
     so the simulation remains integer-only and independent of Godot.
   - Start with base health, armor, melee weapon, damage classification, range
     and the supported attacker limit. The source `Units` records already give
     base health of 485 for Aragorn, 350 for Elf Knight and 240 for Uruk-hai;
     all three now use these imported maxima instead of the former 100 HP.
   - Recover or explicitly document each field's interpretation. The linked
     melee weapons store `CuttingDamagePerSecond`, not per-hit damage. Their
     `MaximumRange` values are 1.0 for Aragorn and 0.5 for the other two: establish
     world scale, body-radius and range-origin semantics before using these as
     center-to-center reach. Do not shrink the accepted spacing based on an
     unverified interpretation.
3. **Implemented with documented approximations: deterministic melee and matching presentation.**
   - Model attack preparation, hit timing, recovery, cancellation, target death
     and retargeting. Consume the decoded `.lmd` melee-hit events and damage
     scales through a runtime adapter; convert clip times to fixed simulation
     ticks with a documented rounding rule.
   - The simulation owns hit resolution and damage. Animation/render callbacks
     observe combat state and cannot cause damage or alter simulation timing.
   - Apply the supported armor, damage-type and unit-to-unit rules, labeling
     unresolved formulas as approximations. Compare small controlled duels with
     the original game; use targeted executable analysis where tables and
     recordings cannot establish a formula.
   - Connect the required attack-ready/weapon visibility states. Add the small
     set of hit/attack sounds needed to read these fights; the complete event,
     effects, cloth and voice adapters remain subsequent work.
4. **Implemented regression validation; original-game comparison remains open.**
   - Exercise all initial orders through the actual UI across the three blue
     squads. Retain safe movement, waiting attackers and deterministic command
     playback while health and damage differ by unit type.
   - Save repeatable duel scenarios, damage/hit-timing evidence and a Godot
     command/combat report. Keep original-reference comparisons distinct from
     implementation regressions and record any unverified formulas.

**Acceptance:** a player can select a squad, issue Move/Attack/Attack-move/Stop/
Hold Ground through the command panel or its shortcuts, and see those orders
behave consistently during a battle. The three existing types use traced source
definitions; damage is synchronized with simulation attack timing, and the same
order log replays identically. No navigation polish or full economy system is
required to accept this slice.

**Following milestones:** complete the verification/correction pass below, then
hero commands/abilities and more unit counters. The first ranged unit, Elven
Archer, now has projectile/hit resolution. Follow with workers, resources, construction, training queues and
fog of war to form a complete skirmish loop. Full UI reconstruction, first-run
installer packaging, campaign, multiplayer and broad presentation polish remain
later work. Do not expand this slice to all 85 unit records.
Build-147 added the front end and a game executable: the original main menu over its
3D menu scene, Skirmish and Sandbox setup screens in the original game-creation frame,
the One Ring loading screen with staged loading, and `prototype/export.ps1` building
`build/WarOfTheRingReforged.exe` ([MENUS.md](../prototype/MENUS.md)). Campaign stays disabled.

## Next milestone: original-game command and combat corrections

Use the [local findings](../references/original-game/LOCAL-SESSION-2026-10-19.md)
and [capture protocol](../references/original-game/CAPTURE-CHECKLIST.md).
Preserve the accepted movement/spacing baseline. Implementation checks remain
valid; they demonstrate our behavior, not equivalence to the original.

1. **Resolve command input and bindings, then correct the prototype.**
   - Keep the confirmed bottom row: Attack, Hold Position, Guard, Patrol.
     `local-010`/`011`/`021`/`022` supply the tooltip intent. Plan to use the
     released Hold Position label in the player UI; this correction is now
     implemented. Internal HoldGround naming remains unchanged.
   - The original Attack button is **Force Attack**, including object/terrain
     targeting regardless of hostility. Compare its targeting with contextual
     right-click and the separate A/Ctrl attack-move modifiers. Record key down,
     key release, default move mode, cancellation and invalid-target behavior;
     distinguish a held modifier from a persistent toggle. Do not assume the
     current unified Attack/A dispatch is faithful.
   - **Chosen bindings (user decision): Q/W/E/R for abilities 1–4 and F2 for
     walk/run. Do not use F1–F4 for abilities.** Keep S Stop, H Hold Position,
     G Guard and T Patrol. Q/E rotation and F1/F2/F3 camera shortcuts are now
     removed; Menu camera buttons, launch-time views and the F4 menu remain.
     F2 and the run-icon button are now implemented; source-ratio walking leaves
     the accepted run speed unchanged. Build-2 connects Aragorn's Q heal and W/E
     passives; R and other heroes remain open. Any future enter-building shortcut must also
     avoid R. These are intentional control differences from the original;
     Stop remains a prototype convenience until its original behavior is known.
   - Compare stationary Hold, guarding a unit/building/area, and Patrol against
     idle behavior: acquisition, pursuit, return, target death and replacement.
     The current awareness/leash distances and patrol resumption remain policies.
   - **Acceptance:** a reference-to-prototype input/behavior matrix identifies
     each match, correction and unresolved rule. Apply evidenced corrections
     through the existing deterministic order path, with focused input/replay
     regressions; document intentional conveniences separately.
2. **Measure melee instead of tuning from isolated stills.**
   - Start with a repeatable Gondor swordsman/worker encounter to reproduce
     `local-020`–`027`, then compare Aragorn/Elf Knight/Uruk-hai with the existing
     demo definitions. Fix upgrades, unit level, ownership, camera and setup.
   - The swordsman panel shows **155 HP, Attack 10, Armor 0, Victories 0** before
     combat. Cross-check its source records and HUD meanings; Attack 10 is not
     evidence of ten damage per hit. Keep this an isolated validation fixture,
     not an unplanned fourth demo squad/type.
   - Use continuous, timestamped recordings or repeatable health traces for
     windup/hit/recovery, interruption, retaliation, retreat and regeneration.
     HP increased between captured samples; isolate healing/regeneration before
     inferring a rate or cause. (Build-52 regenerates by the source tables, reading
     the rates as HP per second; a measured idle Swordsman, 155 HP, should regain
     about 1 HP a second if that reading holds.) Establish armor, range origins and event-scale
     formulas with controlled cases or targeted executable analysis.
   - **Acceptance:** preserve original setup, recording/trace and prototype
     replay with an explicit discrepancy list. Correct only measured rules;
     leave unresolved formulas labeled as approximations. Stills and synthetic
     duel checks cannot satisfy this comparison gate on their own.
3. **First ranged unit implemented: Elven Archer (owner request, Oct 20).**
   - Squad 3 contains six archers using source model/portrait, 180 HP, 14 piercing
     DPS and 1–13 m bow range. Source missile-release events launch visible
     original arrows; fixed-tick projectile arrivals apply damage. Existing
     commands, body clearance and Walk/Run remain connected. See the
     [ranged implementation and approximations](../prototype/COMBAT.md#elven-archer).
   - Ranged units switch to source melee weapons/animations during melee combat,
     as clarified by the owner, and resume bow use after contact ends. Switching
     preserves cooldown; released arrows remain in flight. Elven Archer's three
     melee clips and separate weapon 16 are imported and checked.
   - Original Elven arrow launch sound event 172 and trail effect 12 are connected
     to release and projectile playback. Trails are subtle gray ribbons with
     source-derived width/lifetime and fade after impact. Blending, scheduling
     and spatial falloff remain documented adapter policies; checks pass at
     20 and 80 FPS.
   - **Owner checkpoint (Oct 20):** accepted the Elven Archer work and requested
     saving it, including melee switching, centered turning, turn-before-attack,
     launch sound and subtle gray trail. Source-fidelity limits above remain open.
   - Continuous original-game launch/flight/impact and interruption comparison
     remains open. Arrow speed, center-based range, target following and
     navigation-based line of fire are explicit adapter policies. The earlier
     melee comparison gate remains unverified; implementation is not evidence
     of original-game timing or formula fidelity.
   - **Build-2: first hero ability slice implemented.** Aragorn uses source slot
     Q for Greater Kingsfoil, W/E for passive Fury/Immunity, and no fourth slot.
     The sandbox equips these without purchase progression. Original spell
     effects, status producers/cleansing and continuous reference comparison
     remain open; see [ability notes](../prototype/ABILITIES.md). Collect Evil,
     hero and mixed-selection panels before generalizing the Good infantry HUD.
4. **Build the skirmish loop from the captured selection cases.**
   - Separate infantry statistics, worker construction choices and building
     recruitment/queue panels. Include health, armor, damage icon and victory
     counter where supported by source data; validate counter semantics.
   - Trace construction costs, prerequisites and stages through source data.
     Captured checks: barracks **60 food/105 ore, 700 HP, armor 1**; swordsman
     **50 food/0 ore, population 2**. Recruitment reserved population while
     queued; reproduce that case and verify cancellation/refund rules separately.
     Keep the red blocked-placement preview and purple recruitment progress.
   - Use the captured Good technology trees to check prerequisites, then verify
     Evil equivalents. Add fog of war with separate visibility and explored
     memory: observed enemy units disappear under fog while known buildings
     remain. Resource minimap symbols must not be mistaken for enemy markers.

Installation/patch provenance, exact scenario filename and a matched 4:3 capture
remain prerequisites for approving renderer expected-output images. Resolve
those alongside gameplay research; this does not block evidence-backed command
corrections. Keep the 26 web references, provisional local captures and future
approved baselines distinct. Continuous timing evidence and matched scenes are
still required for the relevant fidelity claims.

## Performance backlog

Owner request (Oct 22): keep the optimization ideas here. Measure with
`./prototype/run.ps1 -ObserverSmoke -Profile` (Gimli against Aragorn, 16x,
headless): it prints the unit count and per-section time every two game minutes,
and its final digest must not change unless a change is meant to alter play.
Build-29 measured: mid-game ticks 1.7-2 ms (5-6 ms before), worst two-minute
window 4.2 ms per tick, 16x at 17-26 fps.

- [x] Remove dead bodies (build-31, after 60 s). The original fades a corpse after
  `WorldVariables.UnitCorpsePersistSeconds` (10 s), beginning within
  `ObjectFadeProximity` (15 m) of the camera; the owner suggested 60 s. Dead units
  keep skinned, animated models in the scene for the whole match today. Free the
  view, its animation player and health bar; keep the simulation entry (unit ids
  are list indices).
- [ ] Stuck-unit replanning (build-37 bounded it: a local detour of up to 1,024
  nodes back onto the route first, then a full replan capped at 32,768 nodes, or
  8,192 when retrying after a failure; failed and capped searches on Rift fell from
  6.1 to 0.7 ms per tick in big fights). Original note: blocked units re-plan with other units' cells as
  obstacles, and when bodies wall off a reachable goal the search floods the whole
  region. Build-31 removed the repeated floods from the fallback start cells (5-7 s
  down to under 1 s per worst two-minute window); each stuck unit still floods once
  per retry. Options: remember that a goal was unreachable for a few ticks, bound a
  blocked replan by distance or visited nodes and fall back to waiting, or search
  to the nearest free cell beside the goal. Build-32 finding: only about 6% of the
  failed floods have a goal sealed in a small pocket (most are two large regions
  split by a plug of bodies), and a backward flood from the goal once disagreed
  with A*, so the open-edge relation is not exactly symmetric; find out why (edge
  cache invalidation?) before relying on symmetry.
- [x] Refuge search floods (build-57): `RequestYield` searched a full flood for every free
  cell around a unit whose way back to its goal was cut off, stalling one tick for
  minutes on River Raid. Cells a failed search reached are now skipped (exact). Left:
  the walk over the region itself repeats every tick while that unit stays walled
  in, about 1 ms per tick there; a memory of the failure for a few ticks would end it.
- [x] Route smoothing (build-32): the swept-body test scanned a long path's whole
  bounding box (0.9 ms per tick at 12:00); rows are now solved as intervals.
- [ ] Unit movement step (0.5-0.65 ms per tick mid-game after build-32): each
  moving unit checks body overlap against every unit per step and reserves cells
  against every unit; a per-tick spatial grid would make both local.
- [x] Combat targeting (build-33): `UnitGrid` serves the distance-bounded threat
  scans, and the in-reach and waiting position searches share one pass. Combat fell
  from 0.8-1.3 to 0.5-0.7 ms per tick in big fights.
- [x] Failed attack-position searches (build-34): budget 2,048 -> 1,024 nodes,
  halving each exhausted search. Remembering a failure for 2 s was measured and
  dropped (it skipped 681 of 7,924 failures and lengthened the late fights). About
  three in four position searches still fail in crowded fights; a cheaper way to
  tell early that no position is reachable would help more than any budget.
- [ ] Use `UnitGrid` for the movement scans too (`ClearGroundStep`,
  `ClearCombatStep`, body overlap, `CanReserveMovement`); they matter more as unit
  counts grow beyond the current 70.
- [ ] Occupancy: `OccupiedNodes` and `CanReserveMovement` scan every unit, several
  times per unit per tick (0.2-0.27 ms per tick after build-32's LINQ-free rewrite,
  quadratic in unit count). Keep
  an occupancy count per node updated as units move, or a per-tick spatial grid.
- [x] Route rebase after a map change (build-37): only units whose remaining route
  passes near a toggled obstacle, or never reached its goal, re-plan. On MP08_Rift
  with 8 bots a change cost about 0.5 s and now about 90 ms. Play change: other
  routes may miss a shortcut a destroyed building opened.
- [ ] Large maps (build-37 status): on MP08_Rift big fights (150-215 units) cost
  11-17 ms a tick after the local route repair, the 32,768 and 8,192 node replan
  caps and the local rebase. 4x runs at about 2-3x there, at a median of 13-20 FPS.
  Next: `OccupiedNodes` (about 17 full unit scans a tick, a fifth of the time)
  and long but successful replans.
- [x] A trained unit waiting for room (build-40): the exit search rescanned a
  49 x 49 window up to 16 times a tick, testing each cell against every unit's
  node, and a unit whose exit lay in a pocket waited for good (21 ms a tick on
  Bilbo's Bluffs). It is one sorted pass now, and exits only into regions of 200
  cells or more.
- [ ] Bot think spikes: the placement ring search calls `CanPlaceBuilding` up to
  176 times in one think, and repeats it each second while nothing fits; cache
  refusals per building until the map changes, and stagger heavy bot work.
- [ ] Presentation at high speed: impact and launch sounds and per-event work run
  for every fast-forwarded tick; thin them above 4x. Animation and health-bar
  updates for off-screen units could be skipped or rate-limited.
- [ ] Measure the GPU side on a real display at 16x with large armies (the profile
  above is headless), including skinned-mesh and shadow cost.
- [ ] Allocation pressure: many per-tick LINQ queries and fresh `HashSet`s in the
  simulation; replace the hottest with reusable buffers once profiling points at
  them.

## Deferred navigation and engine work

1. Extend footprint updates to new/moving objects and settle the corner
   diagonal policy. The walker masks are verified: on all 54 maps the original
   walker region graph covers exactly the painted bits, and its links equal
   eight-way adjacency including corner-only diagonals. mask_b is the editor
   input before slope, water and outer-frame exclusions; mask_c forces cells
   passable. The prototype still requires both flank cells for a diagonal,
   which leaves 66 Helm's Deep cells (2,192 on 43 maps) outside the original's
   main area. Existing placed obstacles now have independent node/edge masks:
   removal/restoration runs on fixed ticks, preserves overlapping blocks,
   invalidates routes, and rejects restoration over occupied units or segments.
   Alt + right-click toggles an obstacle and its visual in the sandbox.
   Mesh-edge body clearance now checks continuous swept-square support with a
   350 mm radius, preserving slopes and rejecting gaps or another floor's support.
   Static oriented-collision footprints are integrated:
   108 Buildings/IsPathingObstacle placements block 1,741 ground cells. Original
   collision-query formulas remain unverified; this policy is conservative.
   Bridge/walkway geometry now uses a layered grid with continuous triangle
   coverage, height-matched terrain entrances and integer elevation. The stored
   MPL route codes are unused. Exact source-triangle picking selects visible
   navigation surfaces; Ctrl + Alt + right-click targets terrain beneath overpasses.
   Walkway cells whose center cannot hold the body take the nearest supported
   point within 400 mm (135 nodes; 129 more walkway nodes now reachable), kept
   700 mm from other nodes and links. Paths narrower than the body stay closed.
2. Extend the accepted coordination only when later gameplay exposes a need.
   Basic congestion replanning is implemented: occupied waypoints wait, routes
   retry around occupied cells after ten blocked ticks, and cleared corridors
   resume. Opposing friendly move orders now use reachable side pockets and
   resume their original destinations. Six-unit gate and opposing-traffic tests
   pass. Formation slots now stay in the destination's connected region rather
   than snapping onto nearby disconnected patches. Region queries update after
   obstacle changes; unreachable replacement orders recenter moving units.
   Opposing three-unit squads pass wide bays and single-entry three-cell pockets,
   including reversed orders, cancellation and recorded command playback.
   Movement endpoints stay reserved in transit. Refuges fill from the back,
   remain stable while approached, and can relocate after a route changes.
   Extra formation slots avoid nearby cut points so arrivals leave room for
   other traffic; idle yielders settle nearby if their old spot is claimed.
   Larger gridlocks resolve through chained refuges past standing units, head-on
   detection at junctions, refuge replacement and early release. Opposing
   six-unit squads clear a 12-cell single-file corridor, three squads share it,
   four squads cross a single-file junction, a squad clears idle units out of
   a corridor, 60 random order sequences for 6 to 10 units finish, and six-unit
   squads swap on all four Helm's Deep walkways. Corridors without passing space
   wait safely; dense orders that park units inside single-file passages others
   must cross can still wait. Continuous movement on mixed wide/narrow routes
   and elevated surfaces remains open; the larger dense stress suite needs
   revalidation after this movement pass.
3. Complete the remaining animation/presentation event adapter after the melee
   subset: all equipment/texture changes, footsteps, effects and voice events.
4. Package the importer with an in-game installation picker for distribution.

Navigation regression coverage: send the six friendly units from the clearing through
Helm's Deep's gates, bridges and elevated walkways. The squad must reach
reachable destinations without crossing blocked geometry or overlapping;
unreachable orders must fail cleanly. Existing narrow-passage, opposing-group
and repeatable-playback checks stay enabled during command/combat work.

The command/combat slice is implemented. The next step is the bounded comparison
and correction milestone above, using the first local session to target further
measurements. Deferred navigation refinements, complete audio/appearance
adaptation and importer packaging do not block it.

## Decisions to make

The engine is chosen. Remaining choices can be made as their implementation approaches.

| Decision | Options | Recommendation | Needed before | Status |
| --- | --- | --- | --- | --- |
| Engine | Godot 4 (C#), Unity, a custom C++ engine, Recoil (Spring) RTS engine | Godot 4 with C#: open source, imports glTF directly, and the simulation can live in plain C# for determinism | Phase 3 | Godot 4 with C# chosen Oct 19 |
| How players get the assets | Convert the player's own install on first run, or ship converted files | First-run import from the player's install (the OpenMW model); shipping converted assets is not legal | Phase 3 | Open |
| First playable scope | Skirmish sandbox, or a campaign mission | Skirmish first: it needs no mission scripts or dialogue | Phase 3 | Open |
| Rule fidelity | Match the exe's formulas exactly (disassembly), or approximate from the tables | Tables first for a playable game, then disassemble combat and economy to match | Phase 4 | Open |
| Mission scripts | Host the original 32-bit DLLs behind the same interface, or rewrite each mission in the new engine's script language | Rewrite, using the DLL imports as the API spec; hosting ties the game to 32-bit Windows | Phase 5 | Open |
| Multiplayer model | Deterministic lockstep, client-server, or none at first | Keep the simulation deterministic from day one so lockstep stays possible; build networking last | Phase 3 | Open |
| Ground corner diagonals | Keep requiring both flank cells, or allow corner-only diagonals between painted cells like the original region links | Allow them across unpainted terrain but not across obstacle footprints, so reachability matches the original | Phase 3 | Open |

## Phase 1: finish the asset pipeline

**Gate:** every unit and building exports with correct team colour and the right gear in each clip, matched against in-game screenshots.

**Status (Oct 19): implementation complete.** The initial prototype review against collected references is documented in the reference library. Controlled, matching original-game captures are still needed; the fidelity gate is not marked verified. `converted/phase1-report.json` retains the earlier conversion audit and its deferred visual gate. Effects are data exports; cloth previews use the initial particle surface, without simulation. See `NOTES-presentation-assets.md` for limits and reproducible checks.

- [x] Apply team colour: additive and unlit, texture × light + mask × team colour (decoded from Team Color.lmt), using the `*_Mask.dds` (744 parts); exported as glTF emissive, the viewer has a team colour picker
- [x] Decode the `.lmd` animation state list, to get per-clip attachment visibility (sword drawn or sheathed) and building states (construction, healthy, damaged, ruins) — 5576 states / 5998 weighted choices in all 2108 models; resolver exposes named states, clips, mesh selectors and attachment timelines; see `NOTES-animation-states.md`
- [x] Replace the inferred default-gear rules with the decoded ones — GLB default scenes use the declared idle state; the viewer switches state/choice meshes and attachments, including timed visibility; named level/blood preview settings replace mask-frequency and filename guesses; Shelob now exports (84/85 units); see `NOTES-model-appearance.md`
- [x] Parse v1.0 skeletons (83 files, 15 units incl. Merry and Pippin) and the one v1.0 mesh — all 247 skeletons and 2395 meshes parse; Merry and Pippin export with 21 and 19 clips respectively, including static poses; see `NOTES-legacy-assets.md`
- [x] Export buildings: models with several state meshes and damaged texture swaps. Done: `lms2gltf.py buildings` exports 41/41; the choice's second key group turned out to be per-stage texture selection (construction, healthy, damaged, ruins; ruins drop team colour), applied in the GLB default scene and the viewer; see "Texture keys" in `NOTES-animation-states.md`
- [x] Decode the serialized effects/projectile/UV/cloth formats and export their data: 405 effects, 37 projectiles, 1027 UVAs, 2 texture sequences and 36 cloths parse to EOF; known resource references resolve, unnamed parameters are retained. Projectiles have 33 model previews plus 4 data-only definitions. Building GLBs include initial cloth surfaces, team masks and state visibility; 109 model cloth attachments, 114 collision volumes and 38 groups decode. All 142 unique unit/building/projectile GLBs pass Khronos validation. Particle rendering, cloth simulation and full unnamed-property semantics remain engine work; see `NOTES-presentation-assets.md`.
- [x] Find where clip events live (footsteps, hit frames, voice cues); candidates are the `SoundEvents` and `SpeechFXEvents` tables and the `.lmd` state list. Done: a per-model event table after the attachments, referenced by each choice's event indices; 1936 events in 122 models decode, with footstep joints, melee hit joints, SoundEvents, SpeechFX voice types, effects, projectiles and attachment show/hide named; see "Clip events" in `NOTES-animation-states.md`
- [x] Export the database tables to JSON (one file per table) as the data source for later phases — `lqr.py export --out-dir ../converted/database`; 154 tables, 24,468 records, schema and reference index included

## Phase 2: maps and terrain

**Gate:** one campaign map (Helm's Deep is a good first target) renders in the viewer with terrain, textures, every placed object and water.

**Status (Oct 19): complete.** Helm's Deep renders with all 767 model placements (93 models), 5,720 painted ground tiles and both lakes. Four navigation-only helpers are retained but hidden. Birth of the Uruk-Hai adds a second rendered scene with ten decals. Both GLBs pass Khronos validation with zero errors/warnings; 82 Python and eight JavaScript tests pass. `converted/phase2-report.json` audits maps through EOF, all decal projections, mesh adjacency/route checks and current screenshots. Two cyclic source bridge matrices are reported for rebuilding from geometry during runtime work; see `NOTES-world-scenes.md`.

- [x] Map the contents of `Levels.H2O` (54 files, 102 MB unpacked): all are `.wor` containers with embedded terrain and world records; `wor.py` inventories metadata, CRC-checked elevation grids, interned names and opaque ranges, with minimap/elevation previews; 54/54 maps, 64,639 names; texture layers and placement payloads remain the following tasks; see `NOTES-levels.md`
- [x] Read the shipped `RingsEditor.chm` for the level concepts and terminology — terrain palettes/vertex colour, separate walker/building passability, walkable meshes, painted lakes, placeables, markers, camera margins, lights and sound lines summarized in `NOTES-levels.md`
- [x] Finish terrain overlays/decals: all shipped secondary layers are empty; transitions are in primary textures. All 51 editor and 701 object decals resolve and project onto terrain. Rotated rectangles clip to source triangles and preserve height, tint and opacity; custom secondary painting and dynamic shader behavior remain explicit runtime limits.
- [x] Decode object placement and link each placed object to its model — 59,850 placements across 54 maps resolve, including saved ModelEvents variants; another 1,022 placed effects resolve as data.
- [x] Decode navigation geometry/indexing: ground-category grids, masks and region/portal graphs are retained; all 17 MPL dimensions match LMS triangles after excluding footer flag bit 1. Edge codes and destination-major indexing are proven; 583,802 pairs pass across 15 resources. Two cyclic source bridge tables are flagged, with adjacency retained for rebuilding. Ground graph query/clearance behavior belongs to Phase 3.
- [x] Export a map as one glTF scene and view it — `wor2gltf.py` exports Helm's Deep with shared mesh/image resources, separate placement nodes, painted lake surfaces and a detailed JSON companion; browser preview and zero-error/warning validation saved.

## Phase 3: engine prototype

**Gate:** a skirmish sandbox on one map where you select units, order them to move and attack, and they path, animate and fight with placeholder rules.

**Status (Oct 20): per-type bodies and enemy inspection implemented.** `prototype/run.ps1` launches 36 animated units in six squads with original console art, a scalable four-button bottom row (Attack, Hold, Guard, Patrol), target-dependent Attack/A, hidden Move/Stop controls, editable groups and queued waypoints. The integer 20 Hz core passes 397 synthetic/cache checks; 34 command checks plus existing group/debug and combat/motion/picking checks cover actual Godot dispatch. Source body sizes replace the shared radius, rings show collision size, and an 18-unit jitter/stalling regression is corrected. Imported melee definitions, type ratings, kill counts, single-unit stats, animation-timed damage, equipment visibility and impact sounds are connected. Original formula validation, advanced navigation, complete presentation and importer packaging remain open. See `prototype/README.md` and `prototype/COMBAT.md`.

- [x] Settle the engine: Godot 4 with C#; current local cache is generated from the owner's install, with no assets in source control. Production asset-delivery packaging remains open.
- [ ] Package the first-run importer: `prepare_prototype.py --install` now extracts/verifies source archives and writes a hashed runtime cache; in-game install selection and production packaging remain.
- [x] Load a map scene and units; RTS camera (pan, zoom, rotate), selection box, move and attack orders
- [ ] Complete animation state adaptation: idle/run/ready/attack/death clips and the three demo types' melee/equipment visibility subset play; complete attachment, texture and event timelines remain open.
- [x] Establish prototype navigation and combat spacing (accepted): eight-way layered A*, any-angle wide-ground movement, immediate redirects, formation assignment, mesh entrances/elevation, swept-body clearance, off-center walkway nodes, removable/restorable footprints, deck/underpass picking, congestion replanning, chained yielding and individual combat/waiting positions work. Six-unit traversal and six-versus-six walkway swaps pass. Advanced cases remain in the deferred navigation section; full original-game pathing fidelity is not claimed.
- [x] Implement the selected-unit command panel and explicit Move/Attack/Attack-move/Stop/Hold Ground states, with shared button/hotkey/contextual dispatch, independent intent, cancellation, health, tooltips and replay checks. A bounded three-type combat adapter is now implemented as the first Phase 4 slice.
- [x] Fixed-step integer simulation separate from rendering: 20 Hz core, tick commands, stable ordering and repeatability tests; lockstep networking remains later work.
- [ ] Audio: the three melee impact variants are connected; Ogg speech/music and the complete clip-event effects/voice system remain open.

## Phase 4: game rules

**Gate:** a skirmish against a scripted opponent that plays like the original: same costs, build times, damage outcomes and unit counters.

**Current slice implemented:** the three demo types use traced definitions and
fixed-tick melee events, with repeatable duel and Godot evidence. Armor, range
and event-scale interpretations remain explicit approximations pending original
comparisons. Next: compare and correct command semantics, bindings and measured
melee behavior using the milestone above. The first ranged unit and
deterministic projectile/hit resolution are implemented; compare their policies
against continuous source-game evidence before claiming fidelity. Powers, economy
and production remain later work.

- [ ] Load unit, weapon, building, spell and technique stats from the JSON tables (`Units`, `Weapons`, `Buildings`, `Spells`, `Techniques`, `UnitToUnitModifiers`, `BattleGear`, `HeroExperiences`); three demo units' base melee definitions and pair-rule support are implemented.
- [ ] Economy: resources, workers, building and training queues, places of power (build-54 captures them and applies the armor, regeneration, health, damage, siege and cooldown modifiers; speed, sight, cost, Fate and experience modifiers remain; builds 5-8 add carried ore/food, Citadel delivery, Mill/Foundry construction and refunds, independent structure selection and worker clearance fixes; build-12 adds Citadel worker training with population reservation, cancellation and rally points; build-13 places every Tier 1 building with source passability and proximity; build-14 trains the Gondor Swordsman at the Barracks; build-15 the Dwarf Axethrower at the Dwarf Hall; build-17 replaces source gather rates with owner-defined Warcraft-style trips; build-19 trains Frodo, the last Tier 1 unit; build-20 narrows work ranges to the target's own footprint; build-44 adds the Citadel 2 and War Camp upgrades and the Tier 2 buildings with source prerequisites; build-58 completes the Good tree: Citadel 3-5, the Tier 3 buildings, the Ballista Tower, the Tier 3-4 units, the Fate heroes with the source hero limit, and every stat research chain; build-76 earns Fate in battle by the Rings.exe rule; Warpost corruption and the Power Acquisition setting remain); use captured construction/recruitment cases and source-backed costs/prerequisites, including population reservation and cancellation/refund validation
- [ ] Combat: attack preparation/hit/recovery and interruption, damage types, armour, unit-to-unit modifiers, projectiles, morale or knockback where the original has them; build-21 adds damage to buildings, their destruction and worker repair, without damage types for buildings or a win condition; build-25 adds Watchtower defenses at the base rate of fire, without garrisons; build-26 adds the Razing win condition
- [ ] Heroes, fate powers and spells (`SpellAndEventAndActions` chains events to actions)
- [ ] Open `Rings.exe` in Ghidra to recover the formulas the tables only feed: damage, hit chance, experience, upgrade effects
- [ ] Validate original command semantics against recorded inputs and expand hero abilities; build-2 connects Aragorn's source Q heal and W/E passive slots. F2 global Walk/Run and Hold Position labeling are implemented. Preserve documented prototype conveniences.
- [ ] Record continuous reference fights and controlled health traces, then compare outcomes; the 30 local stills establish HUD/encounter observations, not timing or damage formulas
- [x] Skirmish visibility: explored terrain/building memory, currently visible enemy units and minimap markers; match the captured fog behavior before calling the skirmish loop complete (build-150: sight grid from the source sight ranges, remembered buildings, haze and grey minimap after local-023/027, bots that scout and search; see `prototype/FOG.md`; line-of-sight blocking and the forest multiplier remain)

## Phase 5: AI and campaign

**Gate:** the first campaign mission (Light Side tutorial 1) plays start to finish, with objectives, dialogue and cinematics.

- [ ] Skirmish AI from the `.rai` personalities and the AI tables (`AIWarParties`, `AIUnitDescriptors`, `AIBuildingClasses`); build-27 adds the core loop (economy, build plan, training, Assault parties, base defence) as an adapter on those numbers, without scouting, raids, flanking, heroes or abilities
- [ ] Customizable bots (owner request, Oct 21): choose a difficulty (Easy / Normal / Hard), then combine style traits. Difficulty sets how *well* a bot plays (think rate, worker target, reaction to attacks, retreating the wounded, target choice), ideally without resource bonuses; traits set *what* it plays by adjusting one shared strategy profile. Trait ideas: Expansionist (claims contested wells and ore early, second Citadel, more workers), Defender (Watchtower ring, attacks only with a full 12-unit party), Rusher (thin economy, two or three Barracks, smallest waves), Raider (small parties against workers and Mills; source `AIWARPARTY_RAIDING`), Tactician (assault with flank and fix groups; source war-party combos), Tower killer (Axethrower-led parties; source `AIWARPARTY_TOWERKILLER`), Hero lover (early Frodo). Some traits exclude each other (Rusher and Defender), others stack; the twelve shipped `.rai` personalities become presets; traits as data files would serve the modding item. Groundwork, in order: gather `SkirmishAi.cs` tunables into one profile record; raids, flanking and Watchtower garrisons; expansion with multiple Citadels; a skirmish setup screen for difficulty and traits (today only launch switches). (Build-49, owner model Oct 23: difficulty = execution, style = a point in an Aggression / Expansion / Tech triangle, one `AiProfile` record in `Simulation/AiRoster.cs`, a fixed style and description per named bot, a lobby bot card. Build-50: order discipline (patient bot orders, urgent interrupts), idle guard for bot units, target priority by kind with threats first. Build-51: player-picked traits per slot (Rusher, Expansionist, Defender, Relentless, Marksman, Demolisher), in the lobby and as `Id+Trait`. Build-53: Raider (raids from `AIWARPARTY_RAIDING` and the `.rai` Harassment). Scout (needs fog of war) and hero use (needs fate points for heroes beyond Frodo) remain open. Build-150: fog of war; every bot scouts by its .rai Scouting, searches, remembers armies seen and holds weaker attacks; hero courage follows Aggression. A Scout trait remains open.)
- [ ] Skirmish setup, bot free-for-alls and an observer mode (owner request, Oct 21): create a skirmish with map selection, player slots (human, bot with difficulty and traits, observer, closed), teams and colours, so large free-for-alls between different bots can be watched. Facts: `extracted/Levels/Multiplayer` holds 32 maps whose `MPnn` prefix gives the player count, up to six 8-player maps; the skirmish importer is hard-wired to MP02 Bilbo's Bluffs and two start points, the cache carries two team colours, about 84 places in `prototype/` assume the human is team 0 and the AI team 1, and only the Good faction is exported. The simulation already handles any number of teams as separate sides, which is exactly a free-for-all, and its Razing outcome takes any number of contenders. Steps, in order:
  1. (Done in build-28; spectator HUD redone in build-36, Stats tab in build-55.) Observer and speed: a bot on every side, a player-less observer HUD (every player's resources, population, army and AI status; read-only selection; camera free), and game speed control (pause, 1x to 16x by stepping the fixed-tick simulation several times per frame, so it stays deterministic) to skip the opening and jump into the fighting.
  2. N players: replace the team-0/team-1 assumptions with player slots; per-slot colours from the source team-colour table; an AI per bot slot. (Build-35: eight slot colours, a bot on every side but the player's, `-Bots` by slot, minimap and rings by slot; the human is still always slot 1, which the setup screen will lift.)
  3. Map pipeline: `prepare_prototype.py --skirmish --map <name>` for any multiplayer map and its start points, one cache per map, and a batch for all 32; verify navigation, resources and start bases on each. (Build-35: `--map`, `--all-maps`, `run.ps1 -Map`; all 32 maps import, and `SimulationTests --crossing-probe` shows every map connected for a Swordsman except MP04_Mighty_Silverlode, whose rope bridges only fit Frodo. Remaining: rope bridges wide enough for armies, and a bot that has lost every worker cannot recover; build-38 adds Citadel rebuilding, all-in attacks and surrender.)
  4. Setup screen: map list with player count and minimap, slots, difficulty and traits, start. (Build-39: map list with previews, You / bot / Closed per start position, any human slot or none, start speed; bot difficulty and traits wait for the customizable-bot work. Build-49: each bot item's tooltip and a bot card give its difficulty, style and description. Build-51: a Traits menu per bot slot.)
  5. Teams and alliances (2v2 and up): split a unit's owner from its alliance, so allies share victory and do not attack each other.
  6. The Evil faction, so bots can differ by side as well as by style. (Builds 133-135: playable Evil side, bots, Fate powers and hero abilities in part; see [Evil notes](../prototype/EVIL.md).)
- [ ] List every `BattleScriptInterface` function the 26 mission DLLs import, with its signature, as the scripting API spec
- [ ] Implement that API in the new engine: objectives, triggers, camera paths, cinematics, dialogue, war parties
- [ ] Rewrite the missions one by one, starting with the three Light Side tutorials (disassembly of each DLL as the reference)
- [ ] Dialogue and subtitles from `Dialogue.lqr` and the Ogg speech files; text from `Text.H2O` (this install is the Russian localization; an English install would add English text)
- [ ] Movies: play the two Bink files through FFmpeg, or convert them once at import

## Phase 6: multiplayer and polish

**Gate:** two players finish a skirmish over the internet without desyncs, and the game runs at native resolution on current hardware.

- [ ] Deterministic lockstep networking to replace the defunct GameSpy service
- [ ] Replays (the original `.wrr` format is a reference for what a replay stores)
- [ ] Interface: complete menus, fonts, abilities and faction/selection-specific panels from `Screens.H2O`, `Fonts.H2O`, `Cursors.H2O` and interface textures; basic commands/artwork are implemented, command compatibility is the next milestone, worker/building panels belong to the skirmish loop
- [ ] Settings, remappable hotkeys (`Hotkeys.lqr`, `Controls.lqr`), widescreen and high-DPI, controller support if wanted; implement the chosen ability/walk-run bindings in the command pass, rather than deferring all binding work to polish. Conflicting Q/E and F1/F2/F3 camera shortcuts are already removed.
- [ ] Modding: load extra content from folders so the community can extend it

## Risks

The biggest risk is the game logic: assets are data and decode cleanly, but rules, AI and missions are compiled code.

| Risk | Fallback |
| --- | --- |
| Combat and economy formulas take too long to recover from `Rings.exe` | Ship rules approximated from the tables, tuned against recorded fights, and refine later |
| Weapon damage-per-second, range and animation-event fields are misinterpreted | Trace table descriptions and executable behavior, compare controlled original-game fights, and label approximations; keep the integer rules adapter separate from rendering |
| 26 compiled missions are too much to rewrite | Release skirmish first; port missions one at a time |
| Legal exposure from assets | Never distribute assets or converted files; only the importer that runs on the player's own copy |
| Only a Russian install is at hand | Text and speech from an English install, imported the same way |
| Floating-point differences break lockstep | Fixed-point or strictly controlled float maths in the simulation from Phase 3 |

## Resume kit

Everything lives in the project folder `war-of-the-ring-reforged`; the research report [Reverse Engineering War of the Ring Assets](https://claude.ai/code/artifact/95bab9cd-8ac8-4c5b-bdfb-289bf5b92397) explains each format and how it was found.

| Path | What it holds |
| --- | --- |
| `TLOTR - War of the Ring/` | The original install, read only |
| `extracted/` | Unpacked archives (regenerate with the first command below) |
| `converted/units/` | 84 unit records (69 unique GLBs) plus `index.json`; butterfly source gap recorded |
| `converted/presentation/` | 1507 presentation asset JSON files, typed references and inventory |
| `converted/buildings/`, `converted/projectiles/` | Building GLBs with initial cloth; projectile definitions/previews |
| `converted/phase1-report.json` | Reproducible conversion audit; visual comparison deferred until after Phase 2 |
| `converted/phase2-report.json` | Complete map/terrain/decal audit and two validated scene gates |
| `converted/walker-mask-report.json` | 54-map audit of ground bitsets against the original walker region graphs |
| `converted/prototype/` | Local hashed runtime cache, rendered preview and smoke/verification reports |
| `prototype/` | Godot C# project, launch script and integer simulation core; start with `prototype/README.md` |
| `tests/simulation/` | Independent C# navigation, movement, combat and repeatability checks |
| `tools/` | Asset import/export code; start with `tools/README.md`, then `tools/NOTES-database.md` |
| `.claude/launch.json` | The `viewer` server config (Python HTTP server on port 8765) |

Run from `tools/`:

```bash
python h2o.py extract "../TLOTR - War of the Ring/Archives" ../extracted --verify
python lms2gltf.py units --out-dir ../converted/units
python lms2gltf.py model UNIT_G_HERO_ARAGORN --out ../converted/units/MODEL_G_ARAGORN.glb
python resolve_model.py UNIT_E_URUK_HAI
```

The viewer is `http://localhost:8765/tools/viewer.html` once its server runs;
the current alternative is port 8767. To resume development, open a coding
session in this repository and start with the navigation milestone above.
