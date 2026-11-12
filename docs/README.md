# Godot C# skirmish sandbox

**build-188:** a launcher. The exported game (`build/WarOfTheRingReforged.exe`) now opens on it: pick the folder the
original War of the Ring is installed in (Browse, or type it and press Enter) and the launcher checks that its database,
archives, text and AI players parse, then shows the text language and counts. **Import game data** extracts every
archive, builds the content pack and the menu art and imports every multiplayer map and the Sandbox, with a progress
bar and a log. Maps import several at a time, largest first; a full import of the English install takes about 17
minutes on a 16-thread machine, most of it the 8-player maps (`--jobs 1` imports them one by one). **Start Reforged** opens the main
menu once a complete import is there (`converted/install.json`); data made earlier with the developer tools counts too.
The launcher remembers the folder and says when the data came from an older build. Importing needs Python 3.13 with
Pillow and NumPy (`converted/tooling/python` or on PATH). The same import runs without the game:
`python tools/import_install.py "C:/Games/War of the Ring"` (`--check` only checks). `run.ps1 -Launcher` opens the
launcher from the project. The tools now read the install named by `REFORGED_INSTALL` when given none.

**build-187:** an English install now imports cleanly. AI player names are read in the install language's code page,
so the English "Grishnákh" no longer turns into "Grishnбkh". The English release's unfinished entries ("Empty
description", "Placeholder") count as missing text, as the Russian "Затычка" entries already did. A content pack built
with `--install` takes the AI players from that install too.

**build-186:** building tooltips match the others: the construction time reads "Time:" and the extra-worker and
"Must stand near your buildings" lines are gone. The Build (Q) and Build advanced (W) buttons list the buildings each
page holds for the selected workers, and B / Shift+B no longer open them.

**build-185:** research tooltips show only the original game's description after the costs and time: the per-unit
bonus numbers, "Applies to units already trained" and "Each tier of this line replaces the one before" are gone, and
hero recruitment tooltips no longer repeat how many heroes may be out at once. The
training, construction, upgrade and research time now stands on its own line above the Description strip instead of
opening the description text.

**build-184:** tooltips follow: construct, train, upgrade, research, ability, Fate, place power and status tooltips use
the install's own descriptions and labels (Level, Sec., Cooldown, Food, Ore, Attack, Armor...), with the numbers in
catalog lines in English and Russian. Cost cells no longer depend on English words. Refusal reasons, order readouts and
menus are still English until build-188.

**build-183:** names now come from your own install's text, in its language: units, heroes, buildings, research,
abilities, Fate powers, places of power and maps. A Russian install shows Russian names. Run
`python tools/game_text.py --pack converted/content/good` once to import the text. Strings the original never had
live in `Locale/strings.csv` in English and Russian. See [LOCALIZATION.md](LOCALIZATION.md); tooltips and menus follow
in the next builds.

**build-170** restores the original gold frame for ready abilities and the faction Fate emblem for unlearned hero gear.
Click the idle-worker button beside the minimap or press **I** to cycle through and center on idle workers.
The main menu logo now has a Reforged subtitle. Run `python tools/content_pack.py` to refresh the shared HUD art.

**build-169:** The game's own code runs about three times faster in `run.ps1` and editor builds, with the same results
and pictures: they now compile the C# with optimizations, as exported games always were. In the 8-bot MP08_Rift late game a tick takes 3.3
instead of 10.1 ms, the late-game play benchmark draws 105 instead of 91 FPS (frame p99 17 instead of 32 ms), and the
300-unit stress fight 46 instead of 39 FPS. Set `ReforgedDebugJit=true` in the environment before starting `run.ps1`
or the editor when a debugger needs every local. Timings from earlier builds were unoptimized. See
[PERFORMANCE.md](PERFORMANCE.md#build-169-the-debug-build-is-compiled-optimized).

**build-168:** Big matches simulate faster with the same results. Connected regions are updated around a building
that goes up or comes down instead of being flooded again for every body (about 400 floods in two minutes of a late
8-bot match), route searches run as one tighter loop, the occupied-cell set is no longer built where nothing needs it,
and ground sweeps, tower targeting and opponent choice do less repeated work. On MP08_Rift from 10:00 to 12:00 a tick
takes 8.6 ms instead of 15.0 and allocates a quarter less; the late-game play benchmark draws 79 instead of 71 FPS.
State digests and stress captures are unchanged. See
[PERFORMANCE.md](PERFORMANCE.md#build-168-route-searches-regions-and-occupancy-in-real-matches).

**build-167:** Late-game matches with heroes, buildings and fog draw faster with the same pictures: the fog overlay
blurs with running sums and fades only the cells still changing, hero glows are rebuilt only for heroes on screen (from
a fifth of the points they used to compute), and buildings skip repeated work. In an 8-bot MP08_Rift match from 10:00 to
12:00 at 1x the frame median falls from 12.5 to 9.9 ms (64 to 77 FPS). `run.ps1 -FastUntil 10
-ProfileUntil 12 -CameraTour` measures that late-game play. See
[PERFORMANCE.md](PERFORMANCE.md#build-167-fog-hero-glows-and-buildings-in-real-matches).

**build-166:** The original pointers and targeting cursors default to half size. In **Settings → Game → Cursor size**,
choose **50%**, **75%**, or **100%**. Apply or OK saves the size for menus and matches.

**build-165:** Damaged buildings show their original damage states and faction dust. Razing a building plays its
ruin clip where available, tilts into the dust and scatters debris while immediately opening the ground to units.
Heroic Aura, Siege Aura and Aura of Terror now show effects on the bearer and affected units.
Legolas's Elven Speed has a stronger effect on the caster, and Obelisk protection marks its recipients.
The menus use the original Good pointer, and matches switch between the original Good and Evil pointers.
Particle movement and building damage thresholds are adapted for the prototype. Rebuild the shared assets with
`python tools/content_pack.py` to pick up the new art.

**build-164:** The last two missing Evil abilities. **Summon Obelisk** (4 Fate) raises a 5000-health Obelisk. Friendly
units within 15 m take half of every hit and the Obelisk soaks the other half. It loses 1.5% of its health each second and
falls when its health is gone; bots raise it in larger fights. **Ringsight:** Gollum, the Nazgul King and Black Riders see
Frodo while he wears the Ring, within their sight range, and their side may attack him.
See [EVIL.md](EVIL.md#build-164-the-obelisk-and-ringsight).

**build-163:** Bots finish their matches. A bot that can never earn food again and has nothing left to train makes a
**last stand**: every soldier, guard, hero and worker charges at once, heroes no longer run home to heal, and losing it
loses the match. Until then its last food buys soldiers instead of waiting for a building, and its heroes count toward an
attack, so strong heroes no longer wait at home for a full party. Bots leave three guards at each place of power they
take, mine ore only near their own depots, and an Evil side whose last Slavemaster fell can train one again. Six-bot
matches that stood still from 20:00 to the 120-minute limit now end. See [SKIRMISH.md](SKIRMISH.md#build-163-the-bots-late-game).

**build-162:** Big melees cost less, with the same results: the attack-position search around wide units (riders,
Uruk-Hai and the like) works out each nearby body once per search instead of once per ring point, runs its cheapest
tests first and asks the attack-move lane once per cell. In a 300-unit fight a tick takes 16 ms instead of 20, and
the stress benchmark shows 39 FPS instead of 35 with the fight on screen. Digests and pictures are unchanged. See
[PERFORMANCE.md](PERFORMANCE.md#build-162-cheaper-combat-position-searches).

**build-161:** The setup screen's Side box offers **Random** beside Good and Evil: the slot plays a side drawn from the
match seed when the match loads, so Restart keeps it and a new Start draws again (`--factions=random,good` works from
the command line too). No place of power stands twice on a map any more: random markers and the stand-ins for the
three excluded landmarks draw among the landmarks the map does not hold yet, named ones listed later included. See
[PLACES-OF-POWER.md](PLACES-OF-POWER.md#build-161-one-of-each-landmark-per-map).

**build-160:** Faster big battles, with the same matches: formation slot searches looked at every occupied cell for
each cell they tested, so idle guards and guard orders cost 19 ms a tick at 10:00 in an 8-bot MP08_Rift match. That
falls to 0.4 ms, and the whole tick from 33 to 13 ms. Graph-link movement checks, idle-guard ally checks and the
tick's commands now look only at nearby units or this tick's entries. Every new check is compared with the old full
scan in the simulation checks, and the Rift match has the same state digest at 14:00. See
[PERFORMANCE.md](PERFORMANCE.md#builds-157-160-rendered-frames-unseen-units-and-local-scans-november-5).

**build-159:** The console's unit text, the minimap and several per-tick HUD scans are redone only when something
they show can have changed, and they no longer walk the whole casualty history. Fogged minimap terrain is rebuilt
whole pixels at a time. At 300 units in view the stress benchmark goes from 31 to 34-38 FPS (runs vary) and
allocates 1.4 MB a frame instead of 3.4. Pictures are unchanged.

**build-158:** Units off screen or in fog stop animating and updating, then catch up when they come into view; a unit
whose shadow reaches the screen still counts as seen. Unit appearances are compiled once per model instead of being
read from JSON every frame, and health bars share their meshes. With 300 units out of view the frame takes 8 ms
instead of 26. The benchmark's captures are pixel-identical to build-157.

**build-157:** A rendered stress benchmark: `run.ps1 -StressBenchmark -Units 300` puts two durable armies in a
windowed fight and records frame-time percentiles, draw calls and allocations with the fight on screen, off screen
and at 4x. Every run draws the same frames, so `tools/compare_captures.py` can prove a presentation change altered
nothing. `-Profile` adds frame percentiles and writes its log; `-ProfileUntil` and `-Headless` measure a match
unattended.

**build-156:** A bot's journal no longer misses a building laid in its very first think. A Hard bot in slot 2 thinks first
at tick 3 with a starting worker beside its home well, so its home Mill went up in that think and the journal took it for
part of the start base. Both slots build the same Mills; only the journal differed. See
[SKIRMISH.md](SKIRMISH.md#build-156-the-journal-takes-the-start-base-before-the-first-think).

**build-155:** Bots claim food first: a Mill (50 ore) on every free, safe well near home now comes right after the
first economy buildings instead of last, so a razed Mill is rebuilt at once, and the expansion Citadel comes as soon as
its style's time is due instead of after every research building. Under fog a bot also counts the enemy towers it
knows by its target before attacking, so a Hard bot no longer feeds small parties into a tower-first base. Easy Farmer
Maggot used to beat Hard Aragorn in 5 of 14 matches on Bilbo's Bluffs; now it wins 1 (Aragorn 11, 2 unfinished at 40:00).
See [SKIRMISH.md](SKIRMISH.md#build-155-food-first-and-towers-in-the-attack-hold).

**build-154:** Cloud shadows only darken the ground, as in the original: build-152 also let them brighten it, so
sunlit grass came out brighter than the retail game. See [LIGHTING.md](LIGHTING.md).

**build-153:** The fog of war's haze takes each map's own fog colour from the original map files (dark green-grey on
Bilbo's Bluffs, reddish brown on Dust and Blood, light tan on Freedom!), its strength scaled by the map's stored fog
alpha. Needs the map caches refreshed by `python tools/map_lighting.py` (build-152). See [FOG.md](FOG.md).

**build-152:** Classic lighting: every map is lit by its own original sun, time of day, ambient colour, drifting
cloud shadows and skybox, with the 2003 game's lighting maths, so the ground keeps the original's bright greens, brown
earth and deep shadows instead of the old flat, blue-grey preview light. Refresh the map caches once with
`python tools/map_lighting.py` (and `python tools/map_lighting.py converted/prototype --source "extracted/Levels/Helms Deep.wor"`
for Helm's Deep). See [LIGHTING.md](LIGHTING.md).

**build-151:** Observer mode shows fog of war: it starts on every player's sight together (all units and
buildings shown, haze where no player sees, grey minimap there); the Sight list in the console's match panel (or V)
picks each player's own sight or no fog. An observed match opens on player 1's base instead of the
whole map from far above, and a player in another slot starts on their own base. The start camera is a little
closer (40 m instead of 48). See [FOG.md](FOG.md).

**build-150:** Fog of war, close to the original: what your side does not see sinks into a dim haze, enemy units
there vanish, enemy buildings appear once found and stay as last seen, and the minimap is grey outside your sight
(lobby: Game settings › Fog of war, on by default; observers press V to see one side's sight; F4 › Reveal map).
Bots play by it: they scout (by their .rai Scouting), search when they know no enemy building, remember the armies
they saw and hold an attack against a stronger one. Bot heroes fight more and retreat later, by the bot's
Aggression. Patch or rebuild the content pack (`python tools/content_pack.py`) for the buildings' source sight;
map caches can be reused. See [FOG.md](FOG.md).

**build-149:** Uruk-Hai use their source crossbow at range and switch to their source melee animation in close combat.
Observer mode adds Buildings [B] (counts by type, including foundations) and Upgrades [G] (completed research,
research progress and building upgrades in progress). Evil hero recruitment is checked against the imported Fortress
roster: Nazgul King, Saleme and Grishnakh all appear as first picks across match seeds, with repeatable choices and
normal hero limits. Refresh `python tools/content_pack.py` for the crossbow data; map caches can be reused.

**build-147:** A main menu and a game executable. `./prototype/export.ps1` builds
`build/WarOfTheRingReforged.exe`, which opens (like a bare `./prototype/run.ps1`) on
the main menu: Campaign (disabled), Skirmish, Sandbox, Settings and Exit on the
original's torn-parchment buttons in its Immortal font, over the original 3D menu
scene of Barad-dûr and an orc army. Skirmish and Sandbox each open a setup screen
in the original stone game-creation frame. Every map now loads behind the original
loading screen, the One Ring filling with light, instead of freezing the window;
loads are also much faster (the cache check no longer re-hashes unchanged files).
Run `python tools/frontend_assets.py` once first. See [MENUS.md](MENUS.md).

**builds 137-141:** The Evil heroes' battle gear. All sixteen gears of Gollum, the Nazgul King, Saleme, Grishnakh and Saruman are simulated: Prison of Ice, Invisible Force, Pestilence and
Twisting Influence (137), Shadow Bind and Shadow Walk (138, adds the Black Rider; rebuild the pack only, the maps already cover its body), Mystic Blades, Blood Mark and Poison Trap (139), Torch and Satchel Charge (140),
Sneak, Surprise Attack and Dark Tracking (141). Rules, source numbers and adapters are in [EVIL.md](EVIL.md); `-EvilSmoke -Sandbox` casts them through the HUD.

**builds 133-135:** The Evil faction. Pick Evil beside any start position on the setup
screen (the Sandbox lets you pick both of yours), or launch with `-Factions evil,good`.
The Minions play from a Goblin Hovel with Goblin Slaves; a Slavemaster supplies
population and lays War Posts, which corrupt the ground the other buildings must
stand on; the Fortress of Mordor trains five heroes and opens five tiers; bots play
the side, cast the Evil Fate powers (Bog, Crows, Vile Surge, the Balrog) and
heroes carry their source abilities (all sixteen simulated since builds 137-141). Refresh
`python tools/content_pack.py` and re-import the maps (`python tools/prepare_prototype.py --all-maps`)
before fielding an Evil troll or Balrog. See [EVIL.md](EVIL.md).

**build-132:** Skirmishes exclude Steed of Rohan, Radagast's Aviary and Enchanting
Willow; explicit placements become seeded replacements from the remaining
12-place pool. Monument of Narsil now gives heroes +25% attack damage, replacing
its army armor bonus. The attack HUD includes place damage multipliers.
See [Places of Power](PLACES-OF-POWER.md).

**build-131:** Blood is toned down by the new Reforged option *Scaled blood*:
harder hits throw more blood (half at a glancing blow, the source amount at 25
damage, half again from 50), and a unit bleeds more often the more it is wounded
(never above 80% health, always at 20% or less). The Purist edition bleeds on
every hit as the original. Elven Archer arrows and daggers now make their sound
on buildings, using the original's default impact row. Refresh
`python tools/content_pack.py` and restart. See [BLOOD.md](BLOOD.md#scaled-blood-build-131-reforged-option).

**build-130:** Critical kills from the original data: when Gimli's axe, the
Giant Ent or the Ballista bolt (weapons that can critically kill) slays a unit
that can be critically killed, the body bursts into its original gore (blood
vapour, skull, bones and flesh that land and rest, splats, a blood mark) and
leaves no corpse. Heroes never burst. Refresh `python tools/content_pack.py` and
restart. See [BLOOD.md](BLOOD.md#critical-kills-build-130).

**build-129:** Fights bleed with the original effects: each hit plays the
victim's source blood (red, orc or Huorn sap) and the weapon's impact flash, a
dying unit leaves a growing blood pool, and blades that cut flesh show their
bloody model parts. Settings > Game > Blood turns it off. Knocked-back units play
Knocked Back, then Get Up, before acting, and a swing whose victim dies plays to
its end. Refresh `python tools/content_pack.py` and restart. See [BLOOD.md](BLOOD.md).

**build-128:** Places of Power resolve to their real names and original models,
with complete random-object bonuses. Adds speed/sight and recruitment discounts,
exact paid-cost refunds, and Willow/Aviary powers with Q targeting and cooldowns.
The audit confirms 15 distinct source models with some shared variants. Refresh
`python tools/content_pack.py` and restart. See [Places of Power](PLACES-OF-POWER.md)
for rules and adapters.

**build-127:** Ballista Tower bolts deal 50 damage before armour every 3 seconds;
arrows retain their 14 damage per second. This is a prototype balance override of
the imported 35 DPS / 10-second bolt, whose previous conversion dealt 350 per shot.
Both tower weapons can automatically target enemy buildings, including foundations,
when they have no unit target. Their usual minimum and maximum ranges still apply.
The selected tower uses the unit stats panel style: damage per hit in each weapon
row, with DPS, attack speed and range in hover tooltips. Existing caches work as-is.

**build-125:** The Fate orb opens five usable Good global abilities: Blind,
Brambles, Heroic Legacy, Blessed Wind and Summon Giant Ent. Bots use them for
combat support while reserving Fate for heroes. See [GLOBAL-ABILITIES.md](GLOBAL-ABILITIES.md)
for controls, source rules, adapters and validation.

First Phase 3 milestone. Loads the local Helm's Deep GLB, Aragorn, eleven Elf
Knights, six Elven Archers and eighteen Uruk-hai in three six-unit squads per side. Select friendly units, issue orders through the command panel and
fight a simple opponent. The engine choice was approved on Oct 19, 2026.

**build-120:** setup plays its own menu cue. Matches play the player's faction's
normal tracks in source order, with 15 seconds of silence between tracks.
Combat no longer switches themes or restarts playback, and music is never shuffled.
Menu, ten Good and ten Evil tracks are included in refreshed local caches.
Restart; other installs run `python tools/game_audio.py` for existing caches.
See [audio notes](AUDIO.md).

**build-118:** attack animations use shuffled weighted cycles for both unit and
building attacks. Each cycle includes every equal-weight clip, avoiding the
long runs of one animation caused by the old selector's interaction with source
weights of 10000. Missile/contact variants remain separate, damage stays fixed
per strike, and animation choice remains deterministic for replays.

**build-117:** bots expand tower coverage as each base grows: one tower normally,
two for Defenders, plus another per four completed local buildings, capped at
four or five per base. New towers cover exposed buildings and expansions.
Watchtowers upgrade to Ballista Towers when unlocked, one at a time, while
preserving construction and technology reserves. Restart; no cache rebuild.

**build-116:** attack animations now deal the same damage per strike as the HUD.
Unupgraded Aragorn deals 26 damage on each strike across all three animations,
including both strikes of the double attack. Damage bonuses and armor still
apply; the attack tooltip's DPS follows actual fixed damage and attack speed.

**build-115:** heroes have a soft player-colored halo at their feet, visible
without selecting them. It follows the terrain and movement, stays separate
from selection/hover rings and spell effects, and hides during invisibility or
after death. Newly recruited and revived heroes receive the same glow.

**build-136:** corrupted ground follows the terrain and uses the source's ash tiles; bots train heroes sooner (an upgrade waits for a hero
it can pay for, Fate-only heroes ignore the building reserve, the first hero is saved for). See EVIL.md. Restart; rebuild the content pack once
(`python tools/content_pack.py`) for the tiles.

**build-114:** bots recruit and revive heroes, buy their paid abilities, defend
home and support the main assault. Wounded heroes retreat below 30% health and
return at 75%. Heroes use healing, stuns, shields, Trueshot, area spells and
Capture when useful; Frodo uses the Ring to escape. Personality hero/ability
flags, resource reservations and normal training limits apply. Restart; no cache rebuild.

**build-112:** audio hooks connect each UI control once, including populated
subtrees, newly added controls and controls leaving and re-entering the tree.
Fixes the repeated `child_entered_tree` / button signal errors in the log.
The audio smoke also fails on engine errors, even if Godot returns success.
Restart; no cache rebuild.

**build-110:** peace/battle switches preserve the soundtrack's playback position
instead of restarting the same introductions. Music progresses through all ten
original pairs in shuffled order, with no repeats until each pair has played.
Frequent skirmishes can no longer keep the soundtrack on one pair. Restart;
no cache rebuild. See [audio notes](AUDIO.md).

**build-109:** original music, sounds and unit speech are connected to play.
The soundtrack crossfades between building and battle themes; units respond to
selection and orders, announce training and react to combat. Weapon impacts use
their source weapon/material pairs; workers mine and hammer, and menus and
construction play their original effects. Settings → Audio has Master, Music,
Sound effects and Unit speech volume, Mute all and a speech test. Changes preview
live; Apply / OK saves, Cancel / Esc restores the applied mix. The shared pack and
standalone demo audio are refreshed locally. Restart; other installs run
`python tools/game_audio.py` for existing caches or rebuild the content pack.
See [audio notes](AUDIO.md).

**build-108:** the selected hero's XP row follows the original console: level
label above a full-height blue bar, original beveled frame and centered current /
required XP count. Health sits above it and status icons below. Counters show
whole XP; the tooltip retains fractional detail. Restart; no cache rebuild.

**build-107:** defeated heroes retain their level and exact XP when resummoned at
the Citadel, including progress toward the next level. They return at full health
with their restored level bonuses. Records are separate for each player and hero
type and survive repeated defeats and cancelled recruitment. Restart; no cache
rebuild. See [hero experience](HERO-EXPERIENCE.md).

**build-106:** heroes earn experience from combat and advance through the original
ten levels, gaining maximum health, armor and attack. The selected hero and the
right-edge portrait show level and XP progress; level-up plays the original effect
and sound. Wounded heroes keep their health percentage. Unit, building, weapon,
spell and experience-place rates are source-backed; overkill and friendly damage
give no extra XP. The shared pack and standalone demo cache are refreshed locally.
Restart; other installs rebuild with `python tools/content_pack.py`. See
[hero experience](HERO-EXPERIENCE.md) for thresholds, rules and validation.

**build-105:** all Good heroes have usable ability sets. Gimli gains Sunder (Q)
and tower Capture (W); Legolas gains Trueshot (Q) and Elven Speed (W); Gandalf
gains Blinding Light (Q), Flame Shield (W), Negate Ability (E) and Rain of Fire (R).
Includes source slots, icons, animations, sounds and sprite effects; area previews,
timed statuses, dispelling, collision-safe knockback and tower ownership colors.
The shared content pack is refreshed locally. Restart; other installs need
`python tools/content_pack.py`. Hero levels followed in build-106; tower garrisons
remain future work. See [hero abilities](ABILITIES.md#remaining-good-hero-skills--build-105).

**build-104:** Healing Herbs' W key and button reserve the Beorning while you
choose an ally, so automatic healing cannot take the cast first. Manual targeting
can redirect an automatic heal before it applies; spent cooldowns remain intact.
Cancelling targeting restores automatic healing. Ability grid hotkeys also use
physical key positions on other keyboard layouts. Restart; no cache rebuild.

**build-103:** units finish ability animations before carrying out replacement
orders. Stop cancels casting and waiting orders immediately; stopping before
the effect leaves the ability ready. Cooldown begins when the effect applies;
an effect already released keeps its cooldown. Restart; no cache rebuild.

**build-102:** Nature's Haven units have their source abilities. Beornings heal
wounded allies automatically or by right-click/W; House of Lore research adds
Bear Form (Q), the bear's Savage Blow (W), and Huorn Treeform (Q). Transformation
keeps selection, population, research and health percentage; both forms use their
own models, stats and abilities. Savage Blow deals 75 damage in its forward arc
and knocks susceptible units back. Treeform heals the Huorn and dazes nearby
enemies for 8 seconds, with the original root mesh, animations, icons and sounds.
The shared content pack is refreshed locally. Other installs need
`python tools/content_pack.py`. Restart; see [Nature abilities](ABILITIES.md#natures-haven--build-102).

**build-101:** Fate sits on the middle arch's round ornament. Fate and group
buttons use the console art without yellow hover, pressed or focus borders.
Selections larger than 20 units scroll within the same two portrait rows, with
a slim scrollbar and mouse-wheel support. Scrolling preserves the selection;
portraits retain their tooltips and click / Shift-click behavior. Restart;
no cache rebuild.

**build-100:** Archer arrows have a more visible arc (6% of ground distance,
capped at 1.2 m) and launch from the model's animated bow joint at its release
pose, rather than the body's generic firing height. Arrow trails start at the
same bow position. Restart; no cache rebuild.

**build-99:** Elven Archer arrows follow a very shallow arc, rising 2% of the
shot's ground distance above the direct line (at most 35 cm). The arrow turns
along the curve and its trail follows it. Restart; no cache rebuild.

**build-98:** Eagle Fletching appears in the Archer's passive R slot, dim until
researched and bright afterward. Protection surrounds its target with a visible
blue aura using the original target texture. Spell cursors use their own source
art and original arrow-tip hotspots, fixing the offset when choosing a shield
or healing target. The shared pack and local prototype cache are refreshed;
restart the game. Other installs need `python tools/content_pack.py`.

**build-97:** ranged units can keep aiming and releasing shots at a running or
circling target. Tracking compares each facing angle with the aim for that same
tick, instead of requiring the previous angle to match the target's new position.
First acquisition still waits for the turn; attack timing, range, line of fire
and order cancellation retain their existing rules. Restart; no cache rebuild.

**build-96:** the console's Menu button opens the original in-game menu: the
Good menu frame over a dimmed, paused battlefield with Settings, Exit and Resume
in the original order. Settings uses the original Game Options frame, gold-edged
buttons and check boxes, with Game (health bars, interface size), Controls (the
key reference) and Reforged tabs, and Reset / Apply / Cancel / OK. The Reforged
tab picks the Purist, Reforged or Custom edition; it lists no improvements yet.
Exit asks Exit to Main Menu (the setup screen), Exit to Windows or Cancel. Esc
steps back out of the menu. Settings persist in `user://settings.cfg`. The debug
tools moved to a **Debug · F4** button at the top right, above the build line.
The local shared interface pack and `converted/prototype` are refreshed; other
installs need `python tools/content_pack.py`. Restart the game.

**build-95:** placed building outlines use their actual footprints, fixing the
Camp's empty space on the left. Human Workers use the original single-punch
animation: 5 base damage every 1.5 seconds, before armor and bonuses. Construction
adds only the health earned by new work, preserving attack damage through
completion. Fate moves to the original orb in the console's center with an
overlaid counter (0–20); hover shows progress and click opens a placeholder
global-abilities page. Back, V or Esc returns to the selected unit's commands.
The local shared interface pack is refreshed; other installs need
`python tools/content_pack.py`. Restart the game.

**build-94:** Sanctuary units have their source abilities and upgrades. Elven
Archers gain passive Elven Cloaks at the Wilderness Outpost; Lightbearers have
Protection (Q), with Negate Magic (W) and Light of Lothlórien (E) researched at
the House of Lore. Ground spells preview their full 7 m / 5 m radius under the
cursor. Original sprite effects, cast animations, ability sounds and the
Lightbearer projectile replace the placeholder orb; ranged impacts use the
target material's source sounds. The shared content pack is rebuilt locally;
other installs need `python tools/content_pack.py`. See [Sanctuary abilities](ABILITIES.md#elven-sanctuary--build-94).

**build-93:** colored overhead health bars are on by default and appear only
when damaged (including unfinished construction). Building bars center over
their visible roof geometry with a small screen-space gap; flags and shadows
do not shift the anchor. HUD bars keep their original colors. Restart; no cache rebuild.

**build-92:** the ground-click ring is 20% smaller with 0.8 peak opacity,
retaining its double pulse and fade. Restart; no cache rebuild.

**build-91:** unit and building right-click outlines share two smooth opacity
pulses, with eased fades and a gentle return to the hovered or selected outline.
Both keep their owner color and normal size. Restart; no cache rebuild.

**build-90:** ground command feedback uses the unit selection-ring geometry laid
onto the terrain. Its perspective, slope and occlusion follow the world, while
the small double pulse remains. Elevated clicks keep the ring on the clicked
surface. Restart; no cache rebuild.

**build-89:** right-clicking owned buildings gives selected units a footprint-aware
approach order. Carrying workers deliver to the clicked drop-off building; empty
workers repair, resume construction or gather as appropriate. Mixed selections
split those jobs, and deliveries can resume the worker's previous gathering.
Restart; no cache rebuild.

**build-88:** right-clicking a unit gives two rapid owner-colored ring blips,
separated by a brief dark gap, then restores its selected or hovered appearance.
The ring keeps its normal size. Restart; no cache rebuild.

**build-87:** structures and neutral interactables use the same feedback as units:
a half-opacity owner-colored footprint on hover and one brief solid blip when
targeted. Attack, repair, gathering and structure rally targets all acknowledge
orders; selected structures keep their normal outline. Restart; no cache rebuild.

**build-86:** ground right-click feedback returns to the small gold circle,
with two gentle size pulses and a short fade. Restart; no cache rebuild.

**build-85:** player-colored health bars affect only world overlays; console and
hero bars retain their original colors. Right-clicking a unit briefly shows its
owner's color on the normal-sized ring once. Hovered units show a half-opacity
ring; selected units keep their normal ring. Restart; no cache rebuild.

**build-84:** accepted ground orders show animated converging arrows and a fading
ripple anchored to the clicked world point. Unit targets briefly pulse their
selection ring without changing selection. Battlefield box drags keep working
over the HUD, and a box touching a unit's body selects it. F4 → Experimental health
bars has independent player-color and overhead toggles (both off by default).
Overhead bars also show full-health units and buildings. Restart; no cache rebuild.

**build-83:** Ranger Camouflage and Eagle Eye are researchable at the Wilderness
Outpost after Citadel 2 (each 75 food, 75 ore, 90 seconds). The Ranger's command
panel shows Camouflage on Q, passive Eagle Eye on E, and innate Detection on R;
research-locked icons stay dark. Q hides selected Rangers until movement or
attack, with a three-second recharge after revealing. Detection reveals enemy
camouflage within 20 m; Eagle Eye extends this to 40 m. Hidden enemies disappear
from targeting, the world and minimap. Terrain fog is still absent. The shared
content pack is refreshed locally; other installs need `python tools/content_pack.py`.

**build-82:** Axethrower projectiles tumble in flight using their source spin
axis and speed (1,080 degrees per second). Both normal and flaming axes rotate;
pause freezes their pose, and follow-through hits preserve the spin phase.
The local shared content pack is refreshed; other installs need `python tools/content_pack.py`.

**build-81:** Flaming Axe's description states its maximum fire damage (7 per
second) and duration (6.25 seconds after the last hit), without referring to other
abilities. Normal building animations now loop, including the Mill's sails;
prebuilt buildings also start their animations. The local shared content pack is refreshed.

**build-80:** Flaming Axe uses the original projectile torch effect for building
attacks. The source flaming projectile shares the normal axe mesh and adds flame,
smoke and ember sprites; unit targets and follow-through hits retain normal axes.
The shared content pack was rebuilt locally; other installs need `python tools/content_pack.py`.

**build-79:** ability tooltips show the name and effect description. Burning
buildings show a fire status icon under their health bar; it disappears when the
fire goes out. Ongoing fire damage no longer plays weapon hit sounds.
Restart; no cache rebuild.

**build-78:** Dwarf Shieldbreakers research Shield Break at the Forge; Axethrowers
gain Razored Edge follow-through hits and Flaming Axe building fires. Their passive
icons stay visible and dim until researched, then light up automatically. Shield
Break shows its armor reduction and timer on the target. The shared content pack
has been refreshed locally; rebuild other installs with `python tools/content_pack.py`.
See [Dwarf abilities](ABILITIES.md#dwarf-units--build-78).

**build-77:** skirmish bot choices run Easy, Normal, Hard, alphabetically within each
difficulty. Each group starts with its own Random option, which draws a named bot
of that difficulty when the match starts and keeps the slot's selected traits.
Returning to setup keeps the Random choice; Restart repeats the same draw.
See [the setup notes](SKIRMISH.md#build-77-bot-order-and-random-choices).
Restart; no cache rebuild.

**build-75:** the camera looks straight from south to north, as in the original game. The
view and the minimap had been turned half way round from the original: on Bilbo's
Bluffs the original minimap shows your base bottom left and each inner ore pile left of
its well (local captures local-008, local-018), while ours showed them rotated 180°.
North is now source +y (Godot +Z), up on the minimap and the setup map, and the camera
faces it, so every building shows its front: the Camp's door flap, the Dwarf Hall's
torch-lit stairs, the Barracks' open arcade on the right. Building models and their
placement are unchanged. Menu camera views reset to north; Rotate left / right still
turn it. The preview sun turned with the camera, so the lit sides are the ones in view.
The Barracks lost its black blocks where the wings meet the tower: its Shadow_Alpha
overlays are dark texels at 18-58% opacity, and the converter's 0.5 alpha cutoff drew them
solid. A mesh that samples mostly partly transparent texels now converts with alpha
blending (only the Barracks overlays and the Ranger's hidden web mesh qualify); rebuild
the pack with `python tools/content_pack.py`. Building models can take an owner-chosen
turn that leaves footprint and selection border as placed: the Mill's blades now face
south-east, toward the camera.

**build-74:** idle guards now attack nearby enemy buildings as well as units.
Building awareness is 10 m from the guard post to the footprint; unit threats
take priority. Guards keep their original post, use bounded siege approaches,
and return after the building falls or becomes friendly. Hold Position stays
stationary. See [idle building guarding](ENGAGEMENT.md#build-74-idle-building-guard).
Restart; no cache rebuild.

**build-73:** building attacks now keep the same combat spacing. Melee units
reserve wider spots around the footprint and need swing room before attacking.
Packed attackers spread within reach; reserves keep the siege order and show
"Waiting for room" until a gap opens. Ranged building attacks also keep their
firing space. See the [siege spacing checks](MELEE-SPACING.md#build-73-building-attacks).
Restart; no cache rebuild.

**build-72:** ranged groups keep the same combat spacing. Packed archers and
axethrowers spread within firing range so the whole group can shoot; shorter-range
units can pass friendly ranks to reach a free firing position. Hold Position keeps
its anchors, and melee weapon switches retain the existing melee rules. See the
[ranged spacing checks](MELEE-SPACING.md#build-72-ranged-groups). Restart; no cache rebuild.

**build-71:** combat leaves more room between allied bodies. Attack positions,
waiting ranks, approach routes and local steering use the same wider spacing;
ordinary movement still lets allies squeeze past each other. Large battles form
a clearer front, with reserves advancing as room opens. See the
[spacing measurements and checks](MELEE-SPACING.md). Restart; no cache rebuild.

**build-70:** a selected unit has a status bar, the original console's row of effect
icons under the health bar: Gimli's Siege Aura, Frodo's Heroic Aura, Guardian Wind
(darkening as it runs out), the worn One Ring and each place of power its side holds.
The status bar replaces the "Running" / "Standing" activity line, which stays in the
portrait tooltip. Attack and Armor read the unit's own values with research, place and dummy bonuses
beside them in green, as the original "+ 9" column. Every console tooltip opens in one
place, the original parchment box flush right above the console: title band, food, ore,
Fate and population cells, then Description. See [the console notes](COMMANDS.md#status-bar-bonuses-and-tooltips-build-70).
The tooltip art is in the content pack (`python tools/content_pack.py`; the local pack
and Helm's Deep cache are updated); an older pack shows a plain box. Restart.

**build-69:** distant building attacks start moving again. Long approaches and terrain
detours use directed routing; nearby melee keeps the crowd-aware flanking approach.
Regression checks cover 6–100 m starts, groups, detours and the real Sandbox Attack
command. See [the fix notes](FLANKING.md#build-69-building-attacks-from-a-distance). Restart.

**build-68:** queued melee units try reachable flanks against nearby enemy ranks.
Melee sieges choose free attack positions around the whole building and keep the
route around friendly blockers. Searches share the existing six-per-tick budget.
See [flanking behavior and regression results](FLANKING.md). Restart; no cache rebuild.

**build-67:** the console drops its how-to hints (targeting instructions, the
group-key line, the "N selected" title, click and Shift-click notes in tooltips)
and shows orders instead. A single unit shows its order and target with the
distance beside the portrait, what it is doing this tick under the health bar
(striking, closing on, waiting for room, blocked, making way, gather phase, cargo),
and its queue on the bottom line. For a bot's unit that line names the bot's job
for it: assault and its target and leg, raid, place squad, expansion guard,
builder, worker or home army; an observer also sees which player it belongs to.
Restart; no cache rebuild.

**build-66:** armies keep their siege objective when passive workers appear. Local
screens answer nearby threats while the rest continue the attack. Bots recall
field soldiers when their buildings or nearby workers are attacked, hold that reserve
through short lulls, and escalate when a Citadel is close to falling. See the
[target priority and base defense notes](TARGET-PRIORITIES.md). Restart; no cache rebuild.

**build-65:** rear melee ranks detect repeated walking without progress, wait in a ready
stance and advance when space opens. Attack-move boundary targets no longer cause
alternating pursuit/regroup turns. Tiny combat corrections do not play locomotion.
See the [reproduction, behavior and verification](MELEE-QUEUE.md). Restart; no cache rebuild.

**build-64:** combat planning now serves actual waiting units, caps search work per tick,
and lets clear explicit attacks start while route searches wait. Local movement and
combat use spatial queries and shared terrain candidates. See the
[RTS research, architecture, benchmarks and limits](RTS-RESEARCH.md).

**build-48:** skirmish maps share one content pack. Run
`python tools/content_pack.py`, then re-import each map cache
(`python tools/prepare_prototype.py --skirmish` and `--all-maps`); older caches still
load unchanged. See [skirmish notes](SKIRMISH.md#build-48-one-content-pack-for-every-map).

**build-16:** construction clicks work again; buttons are no longer hidden and
re-shown between a press and its release. Workers get a Build button (B) that
swaps the unit commands for the seven Tier 1 buildings and a Back button. Esc
cancels placement, then closes the page. Cancel a foundation by selecting it.
No cache refresh is needed. See [skirmish notes](SKIRMISH.md#build-16-build-menu-and-working-construction-clicks).

**build-15:** a completed Dwarf Hall trains the Dwarf Axethrower (60 food,
10 ore, population 2), which throws original axe projectiles with their own
launch sounds and trail. Ranged presentation is now per unit; the Elven Archer
is unchanged. Refresh the skirmish cache with
`python tools/prepare_prototype.py --skirmish --units-only` and Helm's Deep with
`python tools/prepare_prototype.py --rules-only`, then restart. Both local caches
are already updated. See [skirmish notes](SKIRMISH.md#build-15-dwarf-hall-trains-the-dwarf-axethrower).

**build-14:** a completed Barracks trains the Gondor Swordsman (50 food,
population 2, 14 s), which shows the original 155 HP / Attack 10 / Armor 0.
Refresh an existing skirmish cache with
`python tools/prepare_prototype.py --skirmish --units-only`, then restart.
The local cache is already updated. See [skirmish notes](SKIRMISH.md#build-14-barracks-trains-the-gondor-swordsman).

**build-13:** workers build every Tier 1 building. Citadel, Watchtower, Camp,
Barracks and Dwarf Hall go on open ground that the map marks as buildable, near
your buildings, with a green/red ghost and Shift-queued placement. A completed
Camp adds 11 population; a new Citadel accepts deliveries and trains workers.
Upgrade an existing skirmish cache with
`python tools/skirmish_construction.py --cache converted/skirmish`, then restart.
The local cache is already updated. See [skirmish notes](SKIRMISH.md#build-13-tier-1-building-placement).

**build-12:** the Citadel trains Human Workers. Select it and click the worker
portrait; repeated clicks queue up to five. Cost and population are paid when
queued. Cancel from the selection-panel portraits or with Backspace for a full
refund. Right-click the battlefield with the Citadel selected to set a rally
point (original banner). Add the training data to an existing skirmish cache with
`python tools/skirmish_production.py --cache converted/skirmish`, then restart.
The local cache is already updated. See [skirmish notes](SKIRMISH.md#build-12-citadel-worker-training).

**build-11:** brighter ambient lighting and softer baked terrain shadows make
the dark patches readable. Runtime terrain shading lifts shadow intensity while
preserving its color ratios, alpha and fully lit values. Cast building/tree
shadows remain. The original cache stays intact; restart without rebuilding it.
Preview settings and the terrain adjustment are recorded in capture metadata.

**build-10:** drag selection includes buildings and neutral interactables when
the box contains no friendly units. Units take priority, including those retained
by Shift-drag. Selection stays separate: a structure-only box opens one structure
inspector, preferring the visible structure nearest the box center. Partial mesh
overlap works, including a roof with its ground anchor outside the box. Restart;
the build-9 cache works unchanged.

**build-9:** food wells, ore piles and the Place of Power are selectable with
their original portraits and neutral outlines. Resource inspection shows live
stock; objects without health have no HP/armor display. Structure palettes show
only structure actions, and enemy/neutral unit inspection leaves the palette
empty. HP numbers and population/food/ore counters stay vertically centered.
Refresh an older skirmish cache with
`python tools/skirmish_construction.py --cache converted/skirmish`, then restart.
The local cache is already updated.

**build-8:** workers can cross the gap between the Citadel and Foundry. Skirmish
collision uses the original footprint polygons with one body-radius clearance;
the extra grid padding and repeated inflation are removed. Click a Citadel,
Mill or Foundry to select it separately from units and inspect health, armor,
construction progress or remaining resources. Shift-click toggles it; drag
selection and control groups still select units. A selected owned foundation
can be cancelled directly. Upgrade an older skirmish cache with
`python tools/skirmish_construction.py --cache converted/skirmish`, then restart.
The local cache is already refreshed. See [skirmish notes](SKIRMISH.md#build-8-building-clearance-and-selection).

**build-7:** workers recover from movement stalls at building/resource corners.
Gathering, return trips and Move orders reconnect from the actual body position
when the cell-center graph cannot find a route. Collision checks still apply.
Restart the sandbox to load the fix; the existing asset cache works unchanged.

**build-6:** workers can build a Mill on a food well or a Foundry on an ore pile.
Select workers, choose the building icon, then click its resource site. A green/red
preview shows placement validity. Workers approach and build together; Stop pauses
their work, right-click resumes it, and Cancel refunds 75% of the cost. Completed
buildings unlock food/improved ore gathering. See [skirmish notes](SKIRMISH.md)
for source values, cache refresh and remaining Tier 1 work.

**build-5:** Bilbo's Bluffs workers gather raw ore and deliver it to their own
Citadel. The resource strip reads the live simulation bank. Right-click ore to
gather, Shift-right-click to queue; ordinary orders interrupt and preserve cargo.
Build-6 adds Mill construction for food. See [skirmish notes](SKIRMISH.md) for
cache refresh, source values and adapter limits.

**build-3:** Kingsfoil now has its original healing sprite layers and a targeting
cursor, with invalid feedback for enemies/full-health units. Passive labels are
removed from skill icons. Active skill hotkeys sit at the bottom-right, separate
from the cooldown countdown. Effects follow the target and freeze when paused.

**build-2: Aragorn abilities.** Select Aragorn or a group containing him, press
**Q** (or the Kingsfoil icon), then click an injured ally or Aragorn. Greater
Kingsfoil heals up to **250 HP** within **10 m**, with a **17-second cooldown**.
He approaches distant targets and uses the source heal animation/sound. The
trial equips Andúril's Fury and Immunity as passives; W/E do not cast them and
R remains unused. Fury raises his displayed Attack to 30. Status-inflicting
weapons, poison cleansing and other original spell effects remain future work.
See [abilities, controls and source trace](ABILITIES.md).

**build-1:** the top-right corner shows the build number, current local date and
live FPS. Each implemented change set increments `BuildInfo.Number` once.
Double-click or Ctrl-click a unit (or portrait) to select every living unit of
that type on its team across the map. Shift-click adds/removes a unit;
Shift with type selection adds that type, or removes it when fully selected.
Shift-drag adds friendly units. Menu and Walk/Run use their native artwork sizes
inside the console's matching slots.

Squads now traverse layered walkable geometry with terrain entrances and
congestion replanning. Static building/object footprints now block movement.
Walkway edges now require support for each type's full collision radius.
Placed obstacles can be removed and restored while units replan their routes.
Painted walker bits match the original walker region graphs on all 54 maps.
Narrow walkways keep off-center nodes, and squads clear longer gridlocks.
New/moving footprints and the corner-diagonal policy remain open.
Open-ground orders now follow full-route any-angle shortcuts, react to new
orders from their current position and steer around nearby units. Squads match
units to formation slots by travel cost. The six-unit benchmark finishes in
10.55 seconds versus 13.70 seconds before, with no waiting samples. See the
[movement notes](MOVEMENT.md) for the fixture, measurements and remaining limits.
The remaining Phase 3 work and acceptance criteria are in
[the local roadmap](ROADMAP.md).

The accepted movement baseline now includes per-type body sizes and a correction
for large formations getting stuck behind early arrivals. The original console artwork, scalable HUD, control groups,
queued waypoints and explicit orders are implemented. The bottom action row is
Attack, Hold, Guard, Patrol. Attack/A resolves by target: enemy pursuit,
neutral/friendly force attack, or ground attack-move. Move and Stop work without
visible buttons. These four types now use imported health, armor, weapon DPS and
animation-timed hit events, with weapon visibility and impact sounds. See
[combat rules and approximations](COMBAT.md) and [command behavior](COMMANDS.md).

Weapon ratings now follow cutting > piercing > blunt > cutting: 125% in the
favorable direction, 75% in reverse, and 100% for Hero attacks. Equal types
and Hero targets are neutral. Single selection shows the original type icon,
Attack, Armor, Victories and live health; hover the icon for its matchups.
Group selection retains the portrait roster. Enemy kills update Victories.

**Squad 3 is six Elven Archers.** Press **3** to select them. They use the
original model, portrait and shooting animation, with 180 HP, Attack 14, Armor 0
and Piercing damage. Their source bow range is 1–13 m. They approach until they
can fire, release visible original arrows on the animation's source event, and
apply damage on arrival. In melee combat they switch to their original melee
weapon and attack animations, returning to the bow after contact ends.
Their animation pivot is centered for hit-and-run turns, and new attack
windups wait until they face the target. Turning uses fixed simulation ticks;
the original per-turn angle field is recorded, while turn speed remains a
[documented prototype value](COMBAT.md#ranged-turning-and-animation-pivot).
Arrow releases play the original Elven launch sound and leave a narrow gray
trail that fades after impact. Refresh older sound/effect caches with
`python tools/prepare_prototype.py --rules-only`.
Attack/A, attack-move, Hold, Guard, Patrol, queued
orders and Walk/Run use the existing controls. Arrow speed and target following
are prototype policies; [ranged details and limits](COMBAT.md#elven-archer).

Click an enemy to inspect the same stats with a red ring and health bar.
Unit commands disable while inspecting it; the global Walk/Run switch remains
available. Drag selection remains friendly-only.
A unit or building below full health carries a black world health bar with its
owner's color above the model. Building bars center just above the visible roof.
Menu → Settings → Health bars can restore green fills or placement at the model's foot.
A foundation's health climbs with the work done, so the same bar is its construction progress bar.
Body radii are **0.800 m** for Aragorn, **0.927 m** for the Elven Knight and
**0.693 m** for the Uruk-hai. Selection rings extend beyond those envelopes.
Aragorn's demo model is enlarged 12% with a wider hitbox. Raw source bounds remain recorded. Friendly personal space while
travelling can compress to half the combined envelope so units can squeeze between allies.
Solid cores still prevent stacking; enemies and terrain retain full clearance.
Resting formations keep full spacing. Large formations retain continuous movement and exchange
blocked arrival slots. See [collision details](NOTES-unit-collision.md).
Refresh an existing cache with `python tools/prepare_prototype.py --rules-only`;
this also rebuilds body-specific navigation profiles for rules format 3.

Attack-move reacts immediately to reachable adjacent enemies, including while
regrouping and after arrival. Rear units may flank a nearby battle involving
allies, while pursuit of fleeing enemies remains bounded. Combat steering keeps
a passing side, and approach routes obey the same limits as movement. See the
[crowd follow-up](ENGAGEMENT.md#crowd-and-attack-move-follow-up) and run
`./prototype/run.ps1 -AttackMoveEngagementSmoke` for the full-army input check.

Idle player units automatically watch a 10 m radius and help nearby allies fight.
They pursue within a 12 m limit and return near their resting position afterward.
With no unit to fight, they also attack enemy buildings whose footprint is within
10 m of that position, keeping the same approach limit and combat spacing.
Completing Move starts guarding the new position. Stop cancels the current action
and resumes default guarding on the next tick; Hold Position stays stationary.

**F2** or the original run icon beside the minimap switches your entire army
between walking and running, including units spawned afterward. It works with
an empty selection or while inspecting enemies. Each player's setting is
independent. Orders, queued waypoints and attack timing continue.
Each unit runs at its own speed, keeping the original ratios with the Gondor
Swordsman at 3 m/s (build-60); walking uses the source animation ratio for each
unit. See [movement details](MOVEMENT.md#walkrun-control--october-20-2026) and
[run speeds](MOVEMENT.md#build-60-per-unit-run-speeds).

Combat reserves separate reachable attack positions around each target, keeps
at least 85% of the combined allied body envelope for imported units, and parks
excess attackers outside the melee ring. Settled
fighters hold position and face their target. Attack orders continue onto nearby
enemies when their target dies. These are prototype rules, not recovered original
combat formulas.

The map starts with all six squads in view. The Menu has Squad, Fortress and Map
camera buttons; their F1/F2/F3 shortcuts are removed. Runtime textures have mipmaps and
anisotropic filtering, painted lake colours/opacity render correctly, and the
spawned teams use the original blue/red colours. Selected units have hollow
gold rings for friendlies and red rings for inspected enemies, sized to the
collision body. The [reference review](../references/original-game/PROTOTYPE-REVIEW.md)
records before/after captures, fixes and remaining fidelity gaps. Lighting and
selection art remain preview approximations.

## Run

From the repository root, with the current local cache and portable tools:

```powershell
./prototype/run.ps1
```

With no scene-choosing parameters it opens the main menu (build-147,
[MENUS.md](MENUS.md)); `-View squad` (or `-View fortress`, `-CapturePath`, the
smokes) loads the Helm's Deep scene described below, as a bare run did before.
`./prototype/export.ps1` builds the stand-alone `build/WarOfTheRingReforged.exe`, which opens on the launcher
(build-188): it imports the original install chosen there and then starts the main menu. `-Launcher` opens the
launcher from the project; `-LauncherSmoke -LauncherFolder <install>` checks the folder, presses Start Reforged and
waits for the main menu (`-LauncherImport` imports first, which replaces this checkout's game data).

On a new machine, or for another install, the launcher's import replaces the steps below in one go:

```powershell
python tools/import_install.py "C:/Games/War of the Ring"
```

The launcher builds C# first. It uses portable tools in ignored
`converted/tooling`, or accepts `-GodotPath` and `-DotnetPath` explicitly.
Pinned development versions: Godot **4.7.2 .NET**, .NET SDK **10.0.402**.
The Windows Godot executable's Authenticode signature was verified; the SDK
ZIP matched Microsoft's published SHA-512. No global PATH changes are needed.

Rebuild the cache from the already extracted local install:

```powershell
python tools/prepare_prototype.py
```

To add the archer to an existing cache, refresh models, portraits, rules,
body navigation and demo squads together:

```powershell
python tools/prepare_prototype.py --units-only
```

To update just the squads and camera in an existing cache:

```powershell
python tools/prepare_prototype.py --demo-only
```

To refresh imported combat rules, equipment bindings and impact sounds in an
existing cache, run `python tools/prepare_prototype.py --rules-only`.
Existing caches also need `python tools/prepare_prototype.py --ui-only` for the
new type icons. Rules format 3 requires refreshing older combat/clearance caches.

For another owner's install, provide its folder (containing `Database/`
and `Archives/`). Extraction verifies CRCs and paths; the install is read only:

```powershell
python tools/prepare_prototype.py --install "C:/Games/War of the Ring"
```

Bilbo's Bluffs skirmish (build-5; see [SKIRMISH.md](SKIRMISH.md)) uses its own
cache, so the Helm's Deep sandbox above stays untouched:

```powershell
python tools/prepare_prototype.py --skirmish
./prototype/run.ps1 -Skirmish
```

`-Skirmish` on its own opens the skirmish setup screen (build-39; `-Setup` always
does). It is a lobby in the manner of Warcraft III and StarCraft II: every
multiplayer map with its player count and a preview of its start positions, and one
row per start position set to You, a bot personality (with its difficulty) or
Closed. With no slot set to You, you watch. Each bot item's tooltip, and a card below
the slots for the bot last chosen or clicked, give its difficulty, its style in the
Aggression / Expansion / Tech triangle and how it plays (build-49). Each bot slot also
has a Traits menu (build-51): Rusher, Expansionist, Defender, Relentless, Marksman,
Demolisher or Raider (build-53), combined freely except Rusher with Defender or Expansionist. It also sets the start speed. Restart on
the result panel replays the same setup; its Setup button, or the debug menu's
Skirmish setup button, goes back to the screen. Any flag that chooses the match
(`-Map`, `-Observe`, `-Bots`, `-Speed`, `-Seed`, `-NoAi`, `-AiPersonality`, the smokes) skips the screen.

The **Sandbox** test map (build-56) heads the lobby's map list: an empty, level
200 × 200 m field for trying units out, where you command both players, their bases
80 m apart. Every building stands finished around both Citadels and fully upgraded, so
every unit and technique is on offer, and new units gather at a rally point in front of
their base, toward the centre, marked by a flag and a ground ring in its player's colour; both banks stay full and the population cap is the maximum
(100); building, training, upgrades and research finish at once (the debug menu's
instant cheat, on by default; F4 turns it off); no bots play and nothing ends the match. Tab, or the Player 1 / Player 2 tabs
beside the console's menu button, switches the player you command; clicking the
other player's unit or building, or boxing only its units, switches too. Build its
cache once, then pick it in the lobby or launch it directly
([skirmish notes](SKIRMISH.md#build-56-the-sandbox-test-map)):

```powershell
python tools/prepare_prototype.py --sandbox
./prototype/run.ps1 -Sandbox
```

The skirmish plays against the enemy bot (build-27). Since build-49 a bot's difficulty
sets how well it plays (Easy decides every 3 s with one builder and marches straight;
Hard decides twice a second with three builders, finishes wounded enemies and falls
back at half a party; no bot gets extra income) and its style what it plays: when it
first attacks, its party size, workers, expansions, Citadel upgrade and research. Each
named bot has its own style (see [skirmish notes](SKIRMISH.md#build-49-bot-difficulty-and-style)).
Since build-50 a bot lets its units finish a swing before a new order (only falling
back and pulling out the badly wounded interrupt), its idle soldiers guard and help
fights within 16 m, soldiers left idle in the field rejoin the party or walk home,
and a party fights the soldiers and towers hurting it before razing buildings ([skirmish notes](SKIRMISH.md#build-50-bot-order-discipline-idle-guard-and-target-priority)).
Bots expand with a new Citadel
between 5 and 15 minutes by style, go all in when their army can no longer grow, and
surrender when they can never field one again (build-38). Each expansion keeps half an
assault party as its guard, home armies stop chasing enemies that have left, and idle
soldiers reinforce a party already out (build-40). Parties muster on their route and
regroup on open ground every 25 m on the way (build-41). `-AiPersonality Gimli` picks a
shipped `.rai` personality and `-NoAi` keeps the enemy passive. To watch two bots
fight instead (build-28), observe: nobody plays and any unit can be inspected but not
commanded. The spectator HUD (build-36) follows other RTS observer modes: a foldable
overview at the top right lists every player under an Economy tab (food, ore,
income per minute, population, workers, army, buildings), a Units tab (a portrait
per unit type with its count) or a Stats tab (build-55: food and ore gathered over
the match, units killed and lost; the tooltips list them by type with the buildings
razed and lost), and the console's action grid, which has nothing to command, shows
the map, the game clock and the speed controls. The page button beside a bot's name
opens its decision journal under the overview (build-148): its macro decisions with
the game time, newest first (buildings and expansions laid, tech, its army plan and
each new soldier type, every attack with its size, mix and target, raids, place
squads, guards, base defence, heroes, Fate powers and surrender), never unit orders.

```powershell
./prototype/run.ps1 -Observe -Bots Gimli,Aragorn -Speed 4
./prototype/run.ps1 -Observe -Bots Gimli+Defender,Aragorn+Rusher+Marksman -Speed 8
```

A bot slot takes traits after a `+` (build-51), on `-Bots` and `-AiPersonality` alike.

Places of power work (build-54): a side whose units stand alone in a place's 13 m
zone takes it, and every unit of that side carries its benefit (Wretched Sentinel +1
armor, Ancient Fountain +1 HP a second, Elven Temple +18% health, ...) until another
side takes it. Selecting a place shows what it gives, who holds it and its zone; bots
send squads of five to take them. A random place draws its kind from the match seed:
the setup screen draws one per match (Restart keeps it), and `-Seed 5` sets it for a
launch ([skirmish notes](SKIRMISH.md#build-54-places-of-power)).

Any of the 32 multiplayer maps imports into its own cache (build-35), with a Good
Citadel and four workers on every start point and one colour per player slot. A
bot runs every side but yours; `-Bots` names their personalities by slot (your own
slot's entry is ignored while you play), and observing a big map is a free-for-all:

```powershell
python tools/content_pack.py
python tools/prepare_prototype.py --skirmish --map MP04_Wasteland
./prototype/run.ps1 -Map MP04_Wasteland -Observe -Bots Gimli,Aragorn,Legolas,Saruman -Speed 8
python tools/prepare_prototype.py --all-maps
```

Game speed works in every mode: `-` and `+` step through 1x, 2x, 4x, 8x and 16x,
and Space pauses. While observing, the match panel's pause button and notched
slider (click, drag or scroll it) do the same. When the simulation cannot keep up
with the chosen speed (big fights on large maps), the game runs slower instead of
the frame rate, and the match panel shows the speed actually reached.

`-Profile` (any mode) prints, every two game minutes, the unit count and where
simulation and frame time went, for chasing slow matches. It also reports total
units, buried bodies, native nodes/resources, managed heap, private memory,
allocated bytes and GC counts. [Performance notes](PERFORMANCE.md) explain the
retention checks, allocation sampler and remaining large-match costs.

All generated game data stays in ignored `converted/prototype/`, `converted/skirmish/` and `converted/skirmish-maps/`. The source
project contains no game assets. At launch the manifest's content hashes are
checked before loading. Never distribute the local cache with the project.

Since build-48 a skirmish map cache holds only its map (terrain, navigation,
resources, start layout) and names the shared Good content pack in
`converted/content/good/`, which holds every unit and building model, the rules,
HUD art and AI tables. Build the pack once with `python tools/content_pack.py`
before importing maps; after a unit, building, research or AI change, rebuilding
the pack updates every map. Navigation covers the body of every unit of both
factions, so a new unit never needs a map re-import. The per-section refresh
scripts (`--units-only`, `skirmish_*.py --cache`) only edit older self-contained
caches and Helm's Deep.

## Controls

| Input | Action |
|---|---|
| Left-click | Select a unit, skirmish building or neutral interactable; inspect enemies |
| Left-drag | Select friendly units first; otherwise inspect a structure in the box |
| Shift + selection | Add to selection |
| Shift + structure click | Select/toggle that structure separately from units |
| Structure portrait | Center the camera on the selected structure |
| Hero portrait (right edge) | Select the hero; Shift-click adds or removes him; double-click also centers the camera |
| Q with Frodo selected | Put the One Ring on or take it off |
| Workers selected: Build [B], then a building | Place a Tier 1 or Tier 2 building (Shift queues); Mill/Foundry choose a resource site; Back or Esc returns; a locked building names what it needs |
| Workers selected: right-click a foundation | Resume or assist its construction |
| Workers selected: right-click a damaged own building | Repair it with the source hammer clip |
| Armed units selected: right-click an enemy building | Attack it until it falls |
| Attack [A], then click any building | Force-attack it, including your own |
| Right-click the minimap | Order the selection there; Ctrl marches, Shift queues |
| Citadel selected: worker portrait | Train a Human Worker; click again to queue (five maximum) |
| Citadel selected: training portrait / Backspace | Cancel that unit / the last queued unit, with a full refund |
| Citadel selected: right-click terrain | Set the rally point for trained units |
| Citadel or Camp selected: upgrade [Z] | Upgrade in place to Citadel 2 / War Camp; the upgrade heads the queue and its portrait (or Backspace once the queue is empty) cancels it for a 75% refund |
| Forge or Wilderness Outpost selected: Q / W | Research the blades / armor upgrade; its portrait in the queue (or Backspace) cancels it for a full refund |
| 1 / 2 / 3 | Select the corresponding blue squad |
| Ctrl + 1–9 / 0 | Assign the selection to a control group (1–3 start with the demo squads) |
| Shift + 1–9 / 0 | Add the selection to that stored group |
| 1–9 / 0, double-tap | Recall a group; double-tap centers its lead unit |
| Right-click terrain | Move the selection in a small formation |
| Ctrl + right-click | Attack-move to the clicked location |
| Ctrl + Alt + right-click | Order movement on terrain beneath bridges and walkways |
| Right-click an enemy | Attack, approaching when outside melee range |
| A, then left-click | Attack an enemy, force attack a neutral/friendly target, or attack-move to ground |
| M, then left-click | Move (no visible button) |
| S | Cancel the current action and queued orders, then resume default idle guarding (no visible button) |
| H | Hold Position; engage within reach without chasing |
| F2 | Toggle Walk / Run for selected units without replacing orders |
| G / T, then left-click | Guard / Patrol |
| Shift + click / right-click | Queue orders and formation waypoints |
| Alt + right-click a placed obstacle | Remove it; click the same space again to restore it |
| Arrow keys / screen edges / middle-drag | Pan the camera; rest the pointer on any window edge or corner |
| Left-click or drag on the minimap | Move the camera there; the white outline shows the visible ground |
| Mouse wheel | Zoom |
| Space | Pause/resume simulation and unit animation |
| - / + | Game speed down / up (1x, 2x, 4x, 8x, 16x) |
| Observing: E / U / S | Open the overview's Economy / Units / Stats tab; the open tab's key folds the overview |
| Observing: Tab or the overview's chevron | Fold or unfold the overview |
| Sandbox: Tab, or the Player 1 / Player 2 tabs beside the console menu | Command the other player (clicking its unit or building does too) |
| Observing: a player's name in the overview | Center the camera on that player's base |
| Observing: the page button beside a bot's name | Open, switch or close its decision journal |
| Observing: speed slider / pause button | Choose the game speed / pause and resume |
| Esc | Cancel targeting; then close the build page; otherwise clear selection. In the game menu: back out (Settings drops changes, Main Menu resumes) |
| Right-click while targeting | Cancel targeting and retain the current order |
| Restart sandbox | Reset the encounter |
| Console Menu button | Open the game menu (pauses play): Settings, Exit, Resume |
| F4 / Debug button (top right) | Open/close debug tools |
| F10 | Show/hide the debug UI |

Control policy (user decision): **Q/W/E/R for abilities 1–4** and **F2 for
walk/run**, retaining S Stop. These are intentional differences from the
original game's bindings. Q/E rotation and F1/F2/F3 camera shortcuts have been
removed. Launch-time `-View` captures remain available. The
Aragorn's Q heal, W/E passives and global Walk/Run are implemented. See
[command plans](COMMANDS.md#planned-corrections-from-the-local-original-game-session).

Debug tools shows the live pause state and tick. It includes Pause/Resume, Step
one tick (50 ms while paused), Restart encounter and the **Show unit current
action** toggle. The Cheats section can grant food, ore and Fate, enable instant
building and training, or reveal the map. The menu scrolls when needed to fit
above the console. Opening it does not pause play. The interface size, health bar options and shortcut guide
moved to the game menu's Settings in build-96.
Use `./prototype/run.ps1 -DebugMenu` to start with it open, including when saving
a normal viewport capture with `-CapturePath`.

## Verification

```powershell
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --cache converted/prototype
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --worker-cache converted/skirmish
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --crossing-probe converted/skirmish-maps/MP04_Wasteland
./prototype/run.ps1 -Smoke
./prototype/run.ps1 -MotionSmoke
./prototype/run.ps1 -CombatSmoke
./prototype/run.ps1 -MeleeSmoke
./prototype/run.ps1 -CommandSmoke
./prototype/run.ps1 -OrdersSmoke
./prototype/run.ps1 -SkirmishSmoke
./prototype/run.ps1 -ConstructionSmoke
./prototype/run.ps1 -StructureSmoke
./prototype/run.ps1 -ProductionSmoke
./prototype/run.ps1 -PlacementSmoke
./prototype/run.ps1 -DwarfHallSmoke
./prototype/run.ps1 -MinimapSmoke
./prototype/run.ps1 -FrodoSmoke
./prototype/run.ps1 -TechSmoke
./prototype/run.ps1 -Tier2Smoke
./prototype/run.ps1 -ResearchSmoke
./prototype/run.ps1 -SiegeSmoke
./prototype/run.ps1 -DefenseSmoke
./prototype/run.ps1 -VictorySmoke
./prototype/run.ps1 -AiSmoke
./prototype/run.ps1 -ObserverSmoke
./prototype/run.ps1 -SetupSmoke
./prototype/run.ps1 -SandboxSmoke
./prototype/run.ps1 -SurfaceSmoke
./prototype/run.ps1 -ObstacleSmoke
./converted/tooling/dotnet/dotnet.exe run --project tests/simulation/SimulationTests.csproj -- --movement-benchmark converted/prototype/movement-after.json
```

The simulation runner has no Godot or test-package dependency. Build-8 passes
633 synthetic checks and 700 checks with the Helm's Deep cache. The separate
worker-cache suite passes 30 checks on Bilbo's Bluffs. These cover
blocked routes, unreachable areas, adjacent-cell paths, deterministic ties,
terrain interpolation, repeatable command playback, movement, combat,
ownership and non-overlapping units, stationary-blocker detours, a six-unit
narrow gate, waiting-route replacement and resuming after a corridor clears.
They also cover opposing units using a siding, repeatable yielding, opposing
three-unit groups in open space and replacing an order during yielding.
Layered checks cover ramp heights, explicit bridge entrances, separated floors,
surface picking, formation spacing and slot exchanges after an early arrival.
Obstacle checks cover overlapping footprints, separate floors, blocked surface
edges, route invalidation and recovery, deterministic obstacle commands, and
safe restoration around standing units and units in transit. Replacement orders
retain the occupied physical segment on constrained routes; open-ground orders
turn or stop at their current position.
Input geometry checks cover stacked decks, ground below overpasses, slopes,
map boundaries, ray range and shared triangle edges far from the origin.
Formation regressions cover disconnected patches near the destination,
connectivity changes after obstacle updates, and stopping safely after an
unreachable or invalid replacement order.
Directional checks cover weighted diagonal routes, blocked corners and flank
edges, equal movement speed, mid-diagonal cancellation, crossing diagonals and
repeatable fractional progress.
Smoothing checks cover full-body ground shortcuts, blocked-corner detours,
constant speed, cancellation, construction and spawn reservations, and periodic
combat target tracking. Presentation checks follow recorded movement around
corners and verify that rendering does not change simulation state.
Traffic checks cover two opposing three-unit squads using wide bays and
single-entry pockets, reversed command order, cancellation during yielding,
recorded command playback, movement endpoint reservations and bottleneck cache
invalidation. Gridlock checks swap opposing three- and six-unit squads through
a 12-cell single-file corridor, add a third squad behind the first, cross four
squads at a single-file junction, clear four idle units out of a corridor and
run 60 seeded random order sequences for 6 to 10 units on both maps. They check
constrained endpoints and physical overlap every tick. Responsiveness checks
cover twelve distinct shallow headings, next-tick redirects, repeated squad
redirects, full-body ground clearance, interpolation-time separation and
deterministic playback. Off-center walkway nodes
are covered as well. Combat regressions cover six attackers holding distinct
positions, fourteen attackers with a stable waiting ring, moving targets,
obstacles invalidating attack positions, walls and separate floors, target death,
move-order cancellation, separation between ticks and deterministic playback.
Order checks cover cancellation during combat, force-move immunity to retaliation,
Hold Ground versus pursuit, Attack-move resumption after successive targets,
replacement orders, dead/foreign selections, invalid attacks, stopping on decks,
targeting cancellation, equivalent contextual/panel commands, serialized replay
and sequence ordering after playback.
Extended checks cover ten editable groups, additive assignments, death pruning,
queued formation routes, patrol resumption, guard following/leashes and ward
death, friendly force attacks, and proportional HUD layouts through 4K and
ultrawide sizes.
Melee checks cover timed windup/hits/recovery, cancellation, retargeting,
multi-hit damage, armor/pair policies, fractional health and attacker limits.
The optional cache check adds 56 checks, including four repeatable source-unit
duels and imported definitions. Navigation checks retain runtime
layers reproduce the static ground snapshot, all placed building centers are
blocked, and all six friendly units visit each of Helm's Deep's four walkable
surfaces without overlap and reach every order. Opposing six-unit squads also
swap between the clearing and each of the four walkways.
The full demo's 36 spawns are checked for overlap and initial separation from
opponents, so the squads do not start fighting before player input.
The headless Godot smoke test loads
the actual map and 36 animated units, dispatches mouse/key events through the
viewport, and checks selection, move/attack picking, zoom, pause/resume,
animation and damage over 420 fixed ticks. Its report is
`converted/prototype/smoke-report.json`.
It also checks map-view keys, selected-ring visibility, mipmaps, vertex-colour
bindings and team-mask adaptation on the imported scene.
The motion smoke test renders at 80 FPS while simulation stays at 20 Hz. It
requires interpolated positions and gradual turns as well as the normal input
and combat checks, writing `converted/prototype/motion-smoke-report.json`.
The combat smoke test selects squads with number keys, sends all three friendly
squads into combat through actual mouse input, and requires all three to fight
without physical overlaps. Its report is `converted/prototype/combat-smoke-report.json`.
The melee smoke test adds source stats, exact hit timing, animation seeks,
equipment changes and impact playback. It writes `melee-smoke-report.json` and
`melee-hit-trace.json`; source-unit duel evidence is in `melee-duels-report.json`.
The command smoke test exercises actual panel buttons and hotkeys across all
three blue squads. Its checks cover enemy inspection and command protection,
the four bottom-row actions, target-dependent
Attack/A, hidden Move/Stop controls, targeted and immediate orders, disabled
commands, cancellation, mixed unit types, combat interruption, pause/resume,
drag release over the panel and removal of dead selected units. Its report is
`converted/prototype/command-smoke-report.json`.
The interaction check (`./prototype/run.ps1 -InteractionSmoke`) covers animated
ground feedback, fixed-size owner-colored target blips, faded hover rings,
partial-body drags, dragging onto the HUD, and both health bar toggles.
It also verifies that player colors leave the HUD bars unchanged. Add `-Skirmish -NoAi`
to cover building bars and structure hover/blip feedback for move, attack, repair,
gather and rally targets too; add `-CapturePath converted/prototype/interaction.png`
to render the overhead bars and target blip, plus `.ground.png` and `.hover.png` captures.
Skirmish captures also include `.structure-hover.png`, `.structure-blip.png`, and
`.structure-hp.png`; checks cover building bar alignment during camera rotation.
It writes `interaction-smoke-report.json` in the chosen cache.
The orders smoke test adds 27 actual input checks for control groups, centering,
new commands, queues, HUD adjustment and debug pause/step/open/close controls,
writing `converted/prototype/orders-smoke-report.json`. It also checks F2 and the
run-icon button, original walk/run clip selection, unchanged camera/targeting,
paused toggles and empty selection.
Unit scenes share imported resources while each instance keeps its own skeleton
and animation player.
The minimap smoke test (build-18, skirmish cache) checks the original ore, well and
place-of-power markers against the source objects, the view outline, minimap
press-and-drag, release, edge and corner scrolling, and that scrolling stops when
the pointer leaves the window, and (build-42) that every start Citadel is drawn at
its footprint's size in its team's colour. It writes `converted/skirmish/minimap-smoke-report.json`.
The surface smoke test places six units on an elevated deck, dispatches a
right-click order, then checks Ctrl + Alt + right-click terrain picking with the same
screen position. The obstacle smoke test removes and restores an obstacle
through actual Alt + right-click input and verifies its visibility each time.

For a rendered capture (opens the engine, saves a frame, then exits):

```powershell
./prototype/run.ps1 -CapturePath converted/prototype/preview.png
./prototype/run.ps1 -View fortress -HideHud -CapturePath converted/prototype/reference-fortress-after.png
./prototype/run.ps1 -Smoke -CapturePath converted/prototype/reference-selected-after.png
./prototype/run.ps1 -CommandSmoke -CapturePath converted/prototype/command-panel.png
./prototype/run.ps1 -OrdersSmoke -Resolution 3840x2160 -CapturePath converted/prototype/hud-4k.png
```

Each capture writes a companion JSON with its camera, viewport, map hash,
preview lighting and import checks. A smoke check with `-CapturePath` uses the
real renderer so that its selected-unit frame can be saved.
With `-Resolution` and `-CapturePath`, an offscreen target produces the requested
pixel dimensions even when the desktop constrains window size. The HUD scales
with height, retains source proportions and stays centered on ultrawide screens.
Original bitmap art is enlarged with filtering; the font rendering stays sharp.

## Boundaries of this milestone

The simulation runs at 20 Hz, with integer millimeter positions, integer HP,
tick-based cooldowns and stable unit/cell ordering. A* uses eight directions,
with integer 1000/1414 edge costs and an octile heuristic. Ground diagonals
require both neighboring flank cells and their connecting edges to be clear.
Walkway diagonal links pass the full swept-body coverage check. Motion follows
the line between endpoints at 3 m/s using integer micrometer progress, so
diagonals have the same speed as straight moves. Constrained crossing segments
reserve their swept body corridor, with integer segment-distance checks and separate
height ranges. Units can pass beside a stationary flank unit when their bodies
have enough space, while blocked terrain corners remain closed. Endpoint
reservations on constrained routes preserve retreat after an order change;
nearby units block overlapping movement. Godot camera, animation and rendering
do not feed timing or floating-point values back into simulation stepping.
UI hit positions are quantized into cell commands before the next fixed tick.

Orders entirely on wide ground use full-route line-of-sight smoothing, including
long straight and shallow headings. Movement holds only the current tick's body
sweep rather than the whole distant route. Relative-angle local steering handles
nearby blockers, while bounded avoidance falls back to coordinated yielding.
Formation matching minimizes total squared travel, reducing crossed paths.
New orders start at the current physical position; cancellation stops there.
Routes containing single-file cells or elevated decks retain the
constrained solver and its endpoint/corridor reservations. Their ground shortcuts
still span at most six graph edges. Combat plans individual attack positions,
uses free-ground following where its approach supports it, and replans after
six ticks when a target moves, an approach blocks or a waiting slot opens.
Rendering interpolates each tick's recorded movement along its actual corners
and rotates units gradually. It follows terrain height between samples, holds
on pause, and uses no floating-point feedback into the simulation. Native
physics interpolation is disabled for these manually interpolated unit roots.

Movement interprets the saved painted walker bits as allowed cells. On all 54
maps the original walker region graph covers exactly these bits; the two other
bitsets are editor inputs (`docs/NOTES-world-scenes.md`). Cache generation
fails if they disagree. Ground diagonals still need both flank cells, while the
original region graph also links corner-only diagonals: 66 painted Helm's Deep
cells are outside the prototype's main area only for that reason.
Static footprints use the source's oriented-collision layer for Buildings and
objects with `IsPathingObstacle`: 108 Helm's Deep placements block 1,741 painted
ground cells. The prototype interprets the records as half-extents, center and
quaternion, transforms their corners, and applies a conservative swept-cell
envelope. The base graph uses a 350 mm radius and 1.8 m body; per-type profiles
rebuild each object's masks with the imported radius/height. Vertical bounds preserve separate
floors. Original executable collision formulas remain unverified; rotated
boxes may be over-conservative. Unsupported or missing collision boxes fail
cache generation. The obstacle-free graph and independent per-object node/edge
masks allow removal and restoration at fixed simulation ticks. Overlapping
objects retain their own blocks. Adding an obstacle is rejected if its mask
covers a live unit's full body or a constrained segment the unit is crossing.
Construction ahead of an open-ground route invalidates that route and replans.
Constrained replacement orders bring moving units back to their last cell center;
sealed orders
retain their destination and resume after reopening. This sandbox control
toggles existing placed objects. The separate skirmish implements resource-building
construction and selection; damage, repair and destruction remain open.
New or moving footprints still require an adapter.
The raw painted mask and initial blocked snapshot remain available for auditing.
Walkable helpers append separate nodes to the ground grid; the
current cache has 1,342 surface nodes and 48 height-matched terrain entrances.
Links require continuous triangle coverage with at most 150 mm deviation from
the interpolated height, no more than one cell of rise, and terrain joins within
250 mm. Source model scale and placement transforms match the rendered scene.
The original MPL route codes are unused, including the two cyclic bridge tables.
Mesh nodes and edges require continuous support for a swept 350 mm square
around the unit center, conservatively enclosing its circular body. Triangle
subtraction checks the whole footprint; overlapping triangles cannot conceal
gaps. Support must match the local slope and interpolated height within 150 mm,
so lower floors cannot fill holes in a deck. Where a cell center cannot hold
the body, a node takes the nearest supported point within 400 mm (135 on Helm's
Deep). Off-center nodes keep 700 mm from other nodes and from links between
them in the base graph. Per-type clearance profiles reject unsupported larger
bodies, insufficient headroom and narrow links, then body reservations protect
moving/parked units. This conservatively subsets the base graph rather than
resampling it for every body. Mouse orders intersect source terrain and walkable mesh
triangles. Ctrl + Alt + right-click bypasses decks to target terrain beneath them.
Grid traversal limits terrain queries to cells crossed by the ray; precise
triangle calculations avoid cracks along shared edges. The input adapter uses
floating-point geometry and quantizes its result into an integer node command;
this math does not run inside simulation stepping. Mesh-edge clicks snap to a
nearby node on the clicked source surface. These are navigation queries;
rendered props and water do not occlude movement picking.
Units retain their surface node and integer elevation; collision and melee use
vertical separation. On constrained routes, units wait before entering occupied waypoints
and reserve both movement endpoints and their body corridor until reaching the
next cell center, preserving a safe retreat point if an order changes.
Detours and refuge searches
respect these reservations. Congested units retry routes around occupied cells
after ten blocked ticks (half a second); free-ground retries use three ticks.
Temporarily sealed routes retain their destinations and retry; statically
unreachable orders end without a route. This deterministic grid policy passes
the six-unit gate test. Opposing friendly move orders can request a reachable
side pocket: one unit holds there until the other finishes, then resumes its
original order. Refuge choices must leave a route around the other's final
destination. Stable unit/cell ordering makes this repeatable; changing the
priority order releases its yielding partner. Combat units do not request
yielding. Idle friendly blockers can also yield and return; if a convoy parks in
their old spot, they settle in nearby free space. Explicit move goals remain
intact. Early arrivals in
the same squad order exchange formation slots when they block unfinished slots.
Formation slots stay in the destination's connected region, so nearby islands
cannot consume a slot and strand part of a squad. Region queries are cached and
invalidated when obstacle masks change. Unreachable or invalid replacement
orders stop a free-ground unit immediately and return a constrained unit to its
reserved center. One formation slot honors the clicked destination; wide-ground
orders assign its unit by travel cost, while constrained orders keep ID ordering.
Extra slots prefer nearby cells
that do not cut the navigation graph, within one additional cell of the closest
available slot; otherwise they use the closest slot. Obstacle updates invalidate
the cached cut-point classifications.
Refuges avoid other units' reserved destinations and fill deeper off-route space
first, searching up to eight graph edges beyond the pocket entrance. Assigned
refuges remain stable during travel. A settled yielder can relocate if it blocks
its priority unit's changed route, and dependent holds transfer when their own
priority unit must yield. A refuge search can pass standing friendly units
that could also give way; the one nearest the refuge moves first, so a squad
backs out of a corridor one unit at a time. Units also yield on head-on routes
at junctions where their goals point the same way. Yielding units can ask for
room on their own way to a refuge, and an inherited or since occupied refuge is
replaced. A held refuge is released once the priority unit's remaining route no
longer crosses the way back. Idle units settle where they stand when blocked,
or in the refuge when they cannot return without crossing a destination.
Corridors without passing space wait safely. Dense random orders that park
units inside single-file passages others must cross can still wait. Continuous
movement through mixed wide/narrow routes and free-angle elevated motion remain
open. Source-sized 18-unit moves and stationary-target melee have additional
arrival, settled-waiting and interpolated-separation checks.

The three demo types use imported stats and animation-timed melee. Armor math,
event-scale normalization and effective range remain documented approximations;
see [combat notes](COMBAT.md). Run speeds keep the source ratios, scaled so the
Gondor Swordsman runs 3 m/s; walking uses the imported walk/run animation ratio.
Original world speeds and stamina remain unverified.
Enemies acquire nearby reachable targets; attacked units retaliate. Attack orders
retain nearby combat intent after a target dies; move orders clear it. Melee
requires a supported route between attacker and target, so walls and disconnected
floors cannot become attack shortcuts. Imported units use the sum of both radii
for physical contact, approach slots and permission to strike. Overflow attackers
can choose other reachable enemies; nearby allies share active threats. Stalled
steering triggers a new approach rather than circling indefinitely. Bounded goal-set
searches, staggered planning and loading-time region warmup reduce combat spikes.
See [engagement checks and measurements](ENGAGEMENT.md).
Attack-move uses a bounded encounter corridor and regroups after flank pursuits.
Large marching bodies retain a passing side briefly and exchange arrival slots;
visual facing filters small corrections and lateral changes. See the
[crowd movement regression and real-map smoke](MOVEMENT.md#crowded-arrival-and-attack-move-cohesion--october-20-2026).
The collision cylinders and surface-gap reach are adapter interpretations.
This is not the
original combat/economy or skirmish AI. Unit states choose original idle/run/
ready/attack/death clips; supported equipment visibility and impact sounds
now have adapters. Complete texture/effect events, voices, particles, water animation, cloth simulation,
in-game install selection and production importer packaging remain open.
The command log and integer core make repeatability testable; cross-platform
lockstep/networking is not claimed to be implemented.

Cached run/walk clips are adapted to play in place: `J_Position` horizontal
travel is subtracted from every independent skeleton root, preserving pelvis
bobbing and child motion. The original Phase 1 model exports are untouched.

Runtime import uses Godot's [GLTFDocument API](https://docs.godotengine.org/en/stable/classes/class_gltfdocument.html).
The toolchain follows the official [C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html)
and [Windows .NET release](https://godotengine.org/download/windows/).
