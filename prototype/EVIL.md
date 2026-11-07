# The Evil faction

The Minions of Sauron play from the setup screen (a Good / Evil / Random box beside each start position, the Sandbox lets
you pick both of yours) or from the command line (`-Factions evil,good`, one entry per player slot; `random` draws a
side from the match seed and slot, `SimWorld.ResolveFaction`, build-161).

```powershell
./prototype/run.ps1 -Skirmish                       # setup screen; pick Evil for any slot
./prototype/run.ps1 -Observe -Factions evil,good -Bots Saruman,Gimli -Speed 8
./prototype/run.ps1 -Sandbox -Factions evil,good    # Player 1 Evil, Player 2 Good, both yours
./prototype/run.ps1 -EvilSmoke                      # Evil economy, tree and corruption on Bilbo's Bluffs
./prototype/run.ps1 -EvilSmoke -Sandbox             # the Evil Sandbox
python tools/test_skirmish_evil.py
```

## Build-133: the Evil faction

One content pack holds both sides. `tools/skirmish_map.py` names each side's start building, worker and the buildings
raised on a well and an ore pile (`FACTIONS`, `faction_of`); every exporter takes a `factions` argument (both by
default) and writes the Evil rules next to the Good ones under their own names (`BUILDING_E_*`, `UNIT_E_*`). A match
loads the rules of the sides it seats only (`Main.Loaded`), so a Good-only match is the game it was before, and the sim
keeps a side per team (`SimWorld.TeamFactions`, `FactionOf`, `Seated`; `Simulation/Factions.cs`).

Rebuild after pulling: `python tools/content_pack.py` (the pack), then
`python tools/prepare_prototype.py --skirmish` for Bilbo's Bluffs and `--sandbox` for the Sandbox. The Boulder Troll's
collision body is new (see the troll skeleton note below), so every map cache needs the refresh
(`--all-maps`) before an Evil troll can be fielded on it.

### What differs from the Free Peoples (all from the source tables unless marked adapter)

| | Good | Evil |
|---|---|---|
| Start building, drop-off, worker producer | Citadel 1 | **Goblin Hovel** (`BUILDING_E_WORKER_STRUCTURE`, source `IsTownCenter`) |
| Worker | Human Worker | Goblin Slave (carries 10 food, 9 ore, own source clips) |
| Food / ore buildings | Mill / Foundry | Slaughterhouse (530 HP) / Smelter (725 HP) |
| Tech and hero building | Citadel 1-5 | Fortress of Mordor 1-5 (a placed building, 75 food 90 ore, needs a Hovel) |
| Population | Citadel +10, Camps | **Goblin Slavemaster** +12 each while it lives, 15 at most, pop cost 0 |
| Where to build | near own buildings | **only on corrupted ground** (every building but Slaughterhouse, Smelter and War Post) |
| Hero group | Gimli, Aragorn, Legolas (group 0) | Nazgul King, Saleme, Grishnakh (group 1); Gollum 0 Fate, Saruman needs Fortress 5 |
| Console | stone and parchment | Eye of Sauron console, Evil portraits and tooltip parchment |

* **Corruption.** `Buildings.CreatesCorruption` flags the War Post (65 ore, 500 HP, built at once, only a Slavemaster
  may place it) and `CanOnlyBePlacedOnCorruption` the buildings that need it. The source stores no radius:
  *adapter*, 25 m (the Good Citadel's building radius) around each finished War Post, and around the Hovel the map
  started the team with (nothing could be built otherwise). A building's origin cell must stand on corrupted ground
  ("Must be built on corrupted ground"). Destroying a War Post takes its ground back; what stands there stays.
* **Start.** *Adapter*: the original's Evil start is not documented. The Evil team starts with the Hovel on the
  map's start point, four Goblin Slaves on the worker ring and one Slavemaster, whose 12 population is the whole
  capacity (the source `DefaultInitialPopulationCapacity` is 0 and the Hovel adds none). Start bank 115 food, 325 ore.
* **Start footprint.** The map cache saves the Citadel's footprint for the slot; an Evil slot frees it and lays the
  Hovel's at load (obstacle `40000 + team`, `Main.Factions.cs`). Slaughterhouses and Smelters reuse the map's Mill and
  Foundry footprints (adapter, about a metre apart).
* **Palettes.** `construction.palettes` lists what each builder may place: the Goblin Slave the Hovel to the Black
  Vault, the Slavemaster the War Post. `MayPlace`, `IsBuilder` and the Build page follow them.
* **Production and tree.** Straight from `Buildings.UnitProduced` and `BuildingRequired`: Hovel (Slave, Spearman,
  Slavemaster), Orc Mound (Bowman, Slasher needs Fortress 1, Uruk-Hai Fortress 4), Beast Lair (Warg Rider, Giant
  Spider Fortress 2), Shadow Lair (Wraith, Haradrim Slayer Fortress 3), Troll Den (Stonehurler, Bonecleaver Fortress 4),
  Fortress (Gollum, Nazgul King, Saleme, Grishnakh, Saruman). A Slavemaster beyond 15 is refused.
* **Research.** The Dark Arsenal, Breeding Pit and Black Vault techniques of the source (`TechniqueUnitModifiers`: +1
  attack/armour per tier for the listed units). Names follow the manual's hotkey table: the source calls the first
  armour step `TECHNIQUE_FORGED_FITTINGS`, the manual "Iron Fittings". Techniques that grant abilities (Savage
  Influence, Flame Arrows, Poison Dart, Paralyze, ...) are listed as unsimulated with the reason.
* **Troll skeletons** name their root joint `J_Positional`; the importers accepted only `J_Position`, which is why no
  Troll had a body before. Both names are the locomotion root now.
* **Interface.** `Console/*/Evil`, the Evil portraits, tooltip parchment and cursors join the interface pack. For an
  Evil player the console swaps each `console_g_*` for its `console_e_*` twin (`CommandPanel.UseSkin`); the Sandbox
  switches console with Tab.

### Checks

`-EvilSmoke` plays Evil against a passive Good base: the start (four Goblin Slaves, the Slavemaster, 4/12 population,
the Hovel's own footprint), gathering to the Hovel, a War Post that corrupts ground, a Fortress refused outside it and
raised inside it, the Orc Mound, the Slaughterhouse and Smelter, the Slavemaster's second 12 population, the Fortress 4
lock on the Uruk-Hai, and the Hovel's training buttons (25 checks). `-EvilSmoke -Sandbox` checks the Evil Sandbox, its panels, Fate page and powers (19, see build 135).
`tools/test_skirmish_evil.py` checks the imported rules. `tests/simulation` and the Good smokes are unchanged.

## Build-134: Evil bots, corrupted ground on screen, Fate from War Posts

**Bots.** A bot seated on the Evil side plays its own tree (`Simulation/AiEvil.cs`; adapters, the source decision code is not recovered):

* The Hovel is its keep: expansions, rebuilds, towers and guards key on the start building as for the Citadel, so an expansion is a
  second Hovel. A building that needs corrupted ground and finds no room has a **Slavemaster lay a War Post** at the edge of its
  ground, on the side away from the enemy (22-34 m out); an expansion Hovel gets a War Post beside its site first (6-14 m). One post
  is laid at a time; the bot waits for ore (65) and for a Slavemaster.
* **Population is Slavemasters**: it trains one when it has none, and another whenever fewer than six places are free (never past the
  15 the source allows). Slavemasters never join an army (source `AINeverAddUnit`).
* **Tech** upgrades the Fortress (`TechRoots`, `factions.<side>.tech_root` in the pack), not the Hovel; the Fortress is a first-pass
  economy building in the source AI table, so it goes up early.
* **Armies** choose among five Evil mixes (Orc horde, Beast pack, Uruk-Hai legion, Troll war band, Shadow host) once the Fortress
  reaches level 2, as the Good bots do.
* The bot's building plan is limited to its own side's buildings (`FactionOfName`), resource buildings are found by team
  (`ResourceRules`, `ResourceRulesNamed`), and it casts the Evil Fate powers (below).

Watched: `./prototype/run.ps1 -EvilSmoke -Observe -Factions evil,good -Bots Saruman,Gimli -Speed 16` (24 game minutes, 7 checks:
Fortress, War Posts, Slavemasters, army, Fortress level 2+, population never over capacity). In the reference run Saruman has a
Smelter and Slaughterhouse by 2:00, Fortress 1 at 2:00, Orc Mound and Beast Lair by 4:00, Fortress 2 at 10:00, a second Hovel at 20:00,
Fortress 4 at 24:00, four Slavemasters and 19 soldiers.

**Corrupted ground on screen.** `Rendering/CorruptionPatch.cs` lays a scorched disc (radius from the pack) under every finished
War Post and the start Hovel, following the terrain, fading at its rim; it goes when the post falls. The source draws corruption into
the terrain textures; this is a stand-in.

**Fate from corruption.** `WarpostPowerAcquisitionRate` (source, "per square metre, per second") earns an Evil team power from the
ground its finished War Posts corrupt, counted on a 2 m lattice (overlap counts once), scaled by the Power Acquisition rate and divided
by (Fate held + queued + 1) x PowerReductionScalar as battle power is (adapter: where Rings.exe applies the reduction to this source
was not decoded; the start Hovel's ground earns nothing). One post is worth about one Fate point every 2500 s.

## Build-135: Evil heroes, Fate powers and the Balrog

**Fate powers** (`tools/global_powers.py`, `Simulation/GlobalPowers.cs`): the global list holds both sides' powers; `GlobalPower(slot, team)`
gives a team its own side's menu. The Evil menu follows the manual's hotkeys (Q Summon Bog, W Murder of Crows, E Vile Surge, R Summon
Obelisk, A Summon Balrog); numbers are the source's.

| Power | Source id, cost | What the game does |
|---|---|---|
| Summon Bog | `MARSHLANDS`, 2 Fate | enemy units within 10 m move and attack at x0.5 for 15 s (while they stand in it) |
| Murder of Crows | `MURDER_OF_CROWS`, 3 | enemy missile units within 12 m do 1% of their missile damage for 22 s |
| Vile Surge | `RAIN_OF_BLOOD`, 3 | friendly units within 12 m heal 200 and every ability they have is ready again |
| Summon Obelisk | `OBELISK`, 4 | build-164: a 5000-HP Obelisk; friendly units within 15 m take half of every hit, it soaks the other half and decays 1.5% per second ([below](#build-164-the-obelisk-and-ringsight)) |
| Summon Balrog | `SUMMON_BALROG`, 7 | a controllable Balrog for 120 s, no population |

The Balrog (2200 HP, armour 7, 65 damage per second, body 2.56 m x 6.58 m) has no Run state; it runs at its walking pace. Bots cast Bog and
Crows on clusters of three or four enemies, Vile Surge when four friends in 12 m are below 60%, and the Balrog like the Giant Ent.

**Hero abilities** (`tools/evil_abilities.py`): all 16 gears of the five heroes are exported with their source names, icons, slots,
cursors, recharges, ranges and Fate prices. Simulated: **Speed of the Wolf** (Grishnakh: x1.5 speed for 10 s, 18 s recharge, the Elven Speed
mechanism on its caster) and **Aura of Terror** (Nazgul King: enemies within 8 m lose 2 armour while he lives; `UpdateTerrorAuras`). The other
14 (Sneak, Surprise Attack, Dark Tracking, Shadow Bind, Shadow Walk, Poison Trap, Blood Mark, Mystic Blades, Satchel Charge, Torch,
Prison of Ice, Invisible Force, Pestilence, Twisting Influence) show in the hero panel as **Unsimulated** with what the source graph does,
and cost no Fate. They need stealth, teleporting, freezing, mind control, poison clouds and dropped objects, none of which the simulation has.

## Build-136: corruption drawn on the terrain, bots that use heroes

**Corrupted ground.** The old patch was a flat dark disc sampled every 3.6 m, so every terrain bump poked through it and the grass showed
as blotches. `Rendering/CorruptionGround.cs` now cuts the patch from the map's own `Terrain` triangles inside the radius (so it follows each
bump, 7 cm above the ground) and draws it with the source's three dark cracked-ash tiles (`TerrainSetTextures/Corruption`, exported by
`content_pack.export_corruption_tiles` to `terrain/corruption_tile_NN.png`; a pack without them gets a plain dark tile). A shader mixes two
tiles over the patch, takes the terrain's baked vertex shading, and fades to the grass through a ragged rim (value noise on the world
position). It draws before every other blended object, so selection rings, rally lines and placement ghosts stay on top. Restart the game;
rebuild the pack (`python tools/content_pack.py`) once for the tiles. The radius is still the simulation's.

**Evil bots and heroes.** Matches showed the first Fortress hero at 13-14 minutes and no hero at all before it, against Frodo at about six for
Good. Three bot rules, shared by both sides (a Good bot only meets them in the cases below):

* **An upgrade pauses the building's training queue** (Production.cs), and the bot began the next Fortress level in the think that
  finished the last, so a hero it had just paid Fate for stood out the whole chain. `AiHeroBeforeUpgrade`: the next Citadel or Fortress level
  waits while a hero is in the queue or one paid in Fate alone can be trained now by a bot with a full party. A bot holding the Fate of a hero
  that only the next level unlocks saves for that level even before its party is full (`AiFateHeroWaitsFor`).
* **The plan's reserve no longer blocks a hero that costs only Fate** (`AiHeroRecruit`): a Nazgul King takes none of the food or ore a building
  is saved for, yet waited behind it.
* **The first hero.** A bot with a full party and no hero keeps its food from soldiers and workers for the hero that costs food instead of Fate
  (Frodo, Gollum) once only the money is missing, and buys it ahead of the plan (`AiFirstHeroSavings`). Evil's 35-food soldiers drained the bank
  before it ever held the 150 a hero asks.

Measured with `-EvilSmoke -Observe` (Saruman, Evil, against Gimli, Good, 24 game minutes, 16x) on Rift, Wheel of Fate, The Quarry and Three
Prong (Evil against Evil): the Evil bot has its first hero (Gollum) by 4:00-6:00 on every map, where build-135 had none before 13:00 (first
snapshot with a hero: 14:00-16:00). The Fate heroes (Nazgul King, Saleme, Grishnakh) still arrive at 11.7-17.7 minutes, set by how fast the
bot earns three Fate and by the Fortress level 2 they need, not by the bot's choices any more; the runs end with two to five heroes each
(build-135: none alive on Wheel of Fate). The same Good bot (Gimli) keeps Frodo at about 6:00 and a second hero around 14:00, unchanged.
Evil bots cast Summon Bog (Saruman, Wheel of Fate) and Murder of Crows (Gimli, in an emergency); Vile Surge and the Balrog need more Fate
than a bot keeps, since it holds 2-4 back for heroes unless its base is under attack. `-EvilSmoke -Observe` now reports `heroes`,
`first_fortress_hero_seconds`, `hero_probes` (what the training command answers each half minute a bot holds 3 Fate with no Fortress hero)
and `powers_cast` for every bot.

## Build-137: Saruman's four

`tools/evil_abilities.py` reads each gear's spell graph (`walk` follows spawned child spells and the events a spell enables) and the game
simulates what the numbers say; `Simulation/EvilAbilities.cs`. Prison of Ice is Saruman's own from the start, the other three cost Fate.

| Gear | Source | What the game does |
|---|---|---|
| Prison of Ice (slot Q, 25 s) | `SET_FROZEN` 15 s, `APPLY_DAMAGE` 25, target an enemy within 15 m; the Giant Ent and the Balrog are excluded | an enemy at the cast's release tick is held like a dazed unit for 15 s (Frozen status, the source ice effect) and takes 25 |
| Invisible Force (W, 45 s, 2 Fate) | child spell: `APPLY_KNOCKBACK` force 10 at 40 degrees and `APPLY_DAMAGE` 45 within 10 m of a spot up to 15 m away; targets exclude heroes | every non-hero enemy near the spot takes 45 and is thrown away from it (the Savage Blow reading of force: launch speed in m/s, 9.8 m/s2 gravity, so about 10 m) |
| Pestilence (E, 50 s, 3 Fate) | child spell: `UPDATE_HEALTH` -3 a second for 40 s on every enemy within 7.5 m of a spot up to 15 m away | a poison on each of them (heroes too); it never takes the last point ("drains their health, yet does not kill any", the guide); a second cast replaces it |
| Twisting Influence (R, 90 s, 3 Fate) | `CONVERT_UNIT_TEAM`, an enemy non-hero within 10 m; the Giant Ent and the Balrog are excluded | the unit joins Saruman's team for good: its orders end, its former attackers let go, the new team's researched techniques apply and its model is built again in the new colour |

Adapters: the source does not say whether SET_FROZEN honours stun immunity (Aragorn's Immunity includes stun); the game does, as the daze does.
Poison immunity (`IsImmuneToPoison`) holds off Pestilence the same way. The source's `MaxNumAffectedGlobally` 50 for Pestilence is not enforced.
The two cast effects of Pestilence (198, 199) have atlas rectangles the exporter rejects; the poisoned units wear the target effect (200).
Bots cast Prison of Ice on the most dangerous visible enemy (a hero first), Invisible Force and Pestilence at clusters of three, and Twisting
Influence on the strongest ordinary unit in reach. Ability cursors for all Evil gear (`cursor_e_*`) are in the interface pack now.

## Build-138: the Nazgul King's Shadow Bind and Shadow Walk

| Gear | Source | What the game does |
|---|---|---|
| Shadow Bind (Q, 50 s, 2 Fate to learn, **1 Fate each use**) | `ActivateCostPower` 1; target an allied Wraith within 5 m; child spell `APPLY_MORPH` to unit 33; `MaxUnitsOfType` 8 | at the release tick the Wraith becomes a **Black Rider** (450 HP, armour 1, 18 damage per second, mounted, knockback immune) in place: same unit, selection, orders, health share and population (it still counts as a Wraith); no more than eight Black Riders at once |
| Shadow Walk (W, 40 s, 2 Fate) | `TELEPORT_UNIT` after a 0.5 s timer for the caster and the Wraiths and Black Riders within 15 m (`ADD_TARGETS_TO_AFFECTED_UNITS_LIST`); target check `SHADOWALK_WARPOST_CHECK` | pick a spot within 10 m of one of your finished War Posts (the 10 m ring shows it): half a second later the King lands on it and his carried Wraiths and Black Riders on the nearest free ground; no cast clip, depart and arrive effects only |

The Black Rider (`UNIT_E_BONUS_BLACK_RIDER`) is in the content pack now (nothing trains it), with a body of its own (811 x 3137 mm). No map re-import is
needed: the content pack lists the body of every Good and Evil unit (`content_pack.faction_bodies`), every map cache already carries a clearance profile for it,
and rebuilding the pack (`python tools/content_pack.py`) is the only step. As a safety net `GridNavigation.HasBody` makes the game refuse Shadow Bind on a cache that lacks the profile instead of failing. Bots turn Wraiths into Black Riders while they hold two Fate more than it costs;
they do not cast Shadow Walk. The Black Rider's `DetectorType1` (Ringsight, which sees Frodo under the Ring) is exported as a detection mask and not simulated.

## Build-139: Saleme's Mystic Blades, Blood Mark and Poison Trap

| Gear | Source | What the game does |
|---|---|---|
| Mystic Blades (E, passive, innate) | `SWITCH_MISSILE_WEAPON` 64 on activation, not purchasable | Saleme's missile weapon **is** weapon 64 from the start (`combat_rules.INNATE_MISSILE_WEAPONS`): 20 damage a second (her unit record named the Orc Bowman's bow, 8), range 14 m, the Mystic Blade projectile. A blade that strikes flies on to the nearest enemy within 10 m of the one before that it has not struck, up to five more, each hit 30% of the first (the source `ChainTargetDamageReduction`, read like the Axethrower's: a multiplier on the first hit, not compounding). Each hop leaves when the blade before it lands |
| Blood Mark (W, 35 s, 3 Fate) | child spell `UPDATE_HEALTH` +13 a second for 12 s; on a War Post (`SpecificBuildingType1` 1) the "Blood Post" child: 15 s, 12 m, allies inside +5 a second | an ally within 5 m (herself included) heals 13 a second for 12 s; one of your finished War Posts becomes a 12 m healing field of 5 a second for 15 s. Click a unit or a War Post |
| Poison Trap (Q, 15 s, 2 Fate) | `DROP_OBJECT` 530 at the drop clip's toolscript event, at most three (`MaxObjectsPerPlayer`); trigger radius 4 m; the child spell hits every enemy within 10 m with weapon 60, whose `PoisonType` 3 is 6 a second for 25 s | instant (no target): a hidden trap at her feet; an enemy within 4 m springs it and every enemy within 10 m is poisoned 6 a second for 25 s. The poison can kill (Pestilence's cannot). A fourth trap replaces the oldest |

The old panel text was wrong about Blood Mark ("marks an enemy"): the graph and the manual both make it a heal. Adapters: Saleme keeps her bow's firing
clip (the source has a Serpent Blades clip, state 91, with no decoded release event); the weapon-60 hit (50 a second) of the trap's child spell is not
applied apart from the poison; the trap is not an object the enemy can see or destroy (the source gives it 30 HP and `IsInvisible`, detection and
attacking objects are not simulated). Bots heal a wounded ally (below 60%) with Blood Mark and lay a trap when two enemies are close.

## Build-140: Grishnakh's Torch and Satchel Charge

| Gear | Source | What the game does |
|---|---|---|
| Satchel Charge (Q, 20 s, 2 Fate) | `DROP_OBJECT` 538 at the drop clip's toolscript event (instant, no target), at most two (`MaxObjectsPerPlayer`), `Lifespan` 5 s, `CanBeUserActivated`; spell 124: `APPLY_DAMAGE` 70 to enemy units and 35 to enemy buildings within 4 m, `APPLY_KNOCKBACK` force 10 | a satchel lies where he stood (a red ring marks the 4 m blast and blinks faster as the fuse burns); it blows after 5 s, **or when he presses the button again** (the original selects the object and presses its Activate button; this is the adapter), for 70 to every enemy unit and 35 to every enemy building within 4 m, throwing the units back (launch speed 10 m/s at the source's 25 degrees). A third satchel replaces the oldest |
| Torch (E, 8 s, 1 Fate) | `SWITCH_MISSILE_WEAPON` 54 and a missile attack on a building within 15 m (weapon 54: 100 cutting and 30 fire damage a second, projectile 31) | he walks to within 15 m of the chosen enemy building (a finished one), plays the throw, and at the clip's missile release the torch flies (25 m/s, the game's missile pace) and sets it alight at once (the source fire rate is far above anything a building resists, so the burn is full; each building's own extinguish rate ends it) and hits for 100 |

Both reuse what the game had: the building fire of the Dwarf axes (`Ignite`), the knockback of Invisible Force, and a new `hero-torch` projectile style
(`combat_rules`, source projectile 31 with its flame sprites). A Torch cast names a construction site, not a unit; the unit view faces it
(`Main.cs`). Satchels and traps lying on the ground are drawn by `Main.DroppedObjects.cs`: the satchel's ring for everyone, the trap's trigger ring only for its
own team and an observer. Not simulated: the satchel's 10 fire damage a second (weapon 63) and the enemy attacking a satchel (30 HP in the source). Bots throw the
Torch at the nearest enemy building when no enemy stands within 6 m of Grishnakh, and drop a satchel among two or more enemies.

## Build-141: Gollum's Sneak, Surprise Attack and Dark Tracking

| Gear | Source | What the game does |
|---|---|---|
| Sneak (Q, 7 s, 1 Fate) | a toggle (UsageType 2) whose events chain through `ENABLE_SPELL_AND_EVENT_TYPE`: `SET_VISIBLE` false, `SET_PASSIVE`, `APPLY_TIMESCALE_MULTIPLIER` 0.5, `APPLY_DAMAGE_MULTIPLIER` 2.0; a weapon hit starts a 1 s timer that deactivates it, which starts the recharge | Gollum is hidden like a camouflaged Ranger (only enemy detectors see him) and does not pick fights of his own, but **moving does not reveal him**; he moves at half speed; the first blow he lands starts a one second timer, after which he is visible again and the 7 s recharge runs; pressing Sneak again ends it too |
| Surprise Attack (W, passive, 1 Fate) | `SWITCH_MELEE_WEAPON` 61 in the chain Sneak runs when this spell is enabled | while he sneaks his blow is weapon 61, 45 damage a second against his 15 (the manual: "triples Gollum's attack power") |
| Dark Tracking (E, 10 s, innate; "Gollum's Mark" in the manual) | at the cast's toolscript event a child spell on an enemy within 15 m: `START_REVEAL_FOW`, 200 s or until the target dies; `MaxNumAffectedGlobally` 3 | the target is marked for 200 s (three at most, the oldest goes): Gollum's side sees it wherever it goes, hidden or not (Marked status, the source phlegm effect). The game has no fog of war, so showing a hidden unit is the only thing a reveal can change here |

Adapters: the direction of `APPLY_DAMAGE_MULTIPLIER` is not stated. By its sibling `APPLY_MISSLE_WEAPON_DAMAGE_MULTIPLIER` (Guardian Wind, which lowers the damage a unit **takes**) and because the manual gives Sneak
no damage bonus (only Surprise Attack does) it is read as damage taken: a sneaking Gollum takes double. Sneak applies at once instead of after its 1 s stand-down clip, and Gollum keeps his
ordinary walk and idle clips (the source swaps in sneaking ones, animation states 64, 65, 71, 72). Bots do not sneak and do not buy Sneak, Surprise Attack or Shadow Walk (they would only cost Fate that buys heroes).

## All sixteen gears

Every battle gear of the five Evil heroes is simulated now (`-EvilSmoke -Sandbox` casts them through the HUD: Saruman, the Nazgul King, Saleme, Grishnakh and Gollum).
`tests/simulation` `--evil-ability-checks` holds the rules (88 checks), `tools/test_skirmish_evil.py` the imported numbers.

### Still open for the Evil side

* The Obelisk and Ringsight are implemented in build-164 below; the thirteen research effects in build-144. The source gives the
  ordinary Warg Rider, Uruk-Hai, Orc Slasher, Goblin and troll units no battle gear of their own (build-164 audit).
* The Evil start (Hovel, four workers, one Slavemaster) and the 25 m corruption radius are adapters; a capture of the original would settle them.
* Tower garrisons arrived in build-173 (both sides). The minimap shows no corruption (the compendium's minimap captures show none either) and the Evil menu frame is not used. Setup deliberately plays the neutral Hobbit menu cue; matches play the commanded faction's music.
* From the compendium still to measure in the original: the health-colour thresholds (the poisoned bar is purple since build-174), splash and `MaxTargets`, Obelisk decay against redirected damage, and the fresh Evil start.
* Slaughterhouse and Smelter reuse the map's Mill and Foundry footprints.
* Hero gear still to verify against the original: the freeze's stun immunity, the direction of Sneak's damage multiplier, how Dark Tracking's reveal looks without fog, Mystic Blades' chain damage,
  the satchel's fire and its attackability.

### Checks added in builds 134-135

`-EvilSmoke -Observe ...` (7), `-EvilSmoke -Sandbox` (19 in all: the Evil Sandbox, panels, Fate page, powers, hero abilities),
`tests/simulation` `--evil-checks` (34) and the Evil rows of `--global-power-checks`, `tools/test_skirmish_evil.py` (10).

### Cache refresh after the Evil work

`python tools/content_pack.py` rebuilds the pack; `python tools/prepare_prototype.py --all-maps` re-imports every map so each map's navigation
profiles include the Boulder Troll and Balrog bodies (an older map cache has no navigation profile for them). `MP03_Three_Prong` and
`MP05_The_Quarry` had not re-imported since 10-23: `lms2gltf.py` (build-75 alpha detection) crashed on `MS_Quarry.lms`, whose texture
coordinates are NaN. It now skips such triangles and both maps import again.

## Build-143: faction audit, music routing and bot build coverage

The shared pack contains all ten original Evil normal themes, in source track order.
The audit compared each cached track against `Music` / `MusicTracks`: ClanAffiliation 2,
Theme 1, the matching Evil track name, and identical SHA-256 to its extracted Ogg.
The general audio smoke loads every Good, Evil and Menu track and checks the full-track
completion / 15-second silence / next-track cycle.

Match music now uses the seated faction instead of guessing from the first unit or the
shared pack's Good label. This fixes Evil observer matches, empty armies and converted
units. Observers follow the lowest numbered seated slot. Taking command of the other
Sandbox player updates the playlist too; the Evil Sandbox smoke checks Evil / Good / Evil.

Evil bots already chose Balanced, Orc horde, Beast pack, Uruk-Hai legion, Troll war band
or Shadow host on completing Fortress 2. The choice uses match seed and team, persists
through subsequent upgrades and losses, and does not depend on the personality's name.
Focused construction now filters support producers and units by faction: in a mixed
match an early Evil Marksman must plan its own Bowman support, even when Good's ranged
producer appears first in the shared rules.

`dotnet run --project tests/simulation -- --evil-ai-checks --cache converted/content/good`
checks all six choices across 48 seeds, repeatability, Fortress rather than Hovel tech,
each focused build's construction choices, actual recruitment shares, late-tier locks,
fallback after producer loss, and Marksman's ranged share. Recruitment is repeated with
the imported combat definitions, unit costs, population costs and prerequisites.
The observer smoke also reports each bot's chosen composition and checks its faction.

Build-143 validation: clean Godot C# build, 2,540 full simulation checks, 114 Evil bot
checks including the imported pack, the audio smoke and all 48 Evil Sandbox checks.
Two 24-game-minute observer runs passed: Evil / Good at seed 42, and Evil / Evil at
seed 1, where Saruman chose Orc horde and Gimli chose Beast pack. Both observer runs
played Evil music for their first slot. Test processes used writable local appdata.

Remaining gameplay work confirmed by the audit:

* Obelisk is listed but cannot be cast; its effect is not recovered. (Done in build-164.)
* Ordinary Evil unit specials remain incomplete (Slavemaster whip, spider / Warg / troll
  specials, Haradrim cloak). Wraith / Black Rider Ringsight is exported as detection bit 1,
  but `CanSeeUnit` still refuses every enemy view of a Ring-active Frodo. (Done in builds 144 and 164.)
* Thirteen Evil techniques remain explicitly unsimulated: Poison Dart, Perpetual Darkness,
  Savage Influence, Spray Venom, Poison Arrows, Goblin Frenzy, Flame Arrows, Paralyze,
  Greater Perception, Sweeping Axe, Assassin's Cloak, Morgul Blade and Darkness.
* Bots deliberately do not buy/use Sneak, Surprise Attack or Shadow Walk. All sixteen
  hero gears are available to the human; the missing bot decisions are separate work.
* Tower garrisons and terrain fog are not implemented. Corruption is absent from the minimap.
* Original-fidelity questions above remain: start layout, corruption radius, reused
  resource-building footprints, some hero spell details, and attackable traps/satchels.

## Build-144: all thirteen missing Evil upgrades

All thirteen previously disabled research entries are now available at their source
buildings, prices, Fortress requirements and 90-second research times. Research affects
the team's existing units and later recruits; replacing a unit's form or team recomputes
its researched abilities. Rebuild the shared pack with `python tools/content_pack.py`;
the local pack was refreshed with this change, so every slim map uses the new rules.

* **Poison Dart:** Haradrim Slayers gain the targeted shot (10 m, eight-second recharge).
  It costs 25 food and 25 ore at release and applies 5 poison damage per second for
  25 seconds on impact. The source overrides its direct damage to zero. Cancelling
  before release costs nothing; simultaneous casts recheck the shared bank.
* **Morgul Blade:** Black Rider melee hits apply 3 poison damage per second for five
  seconds. **Poison Arrows** replaces the researched team's Evil Tower weapon with
  the source 14-DPS arrows and 2 poison damage per second for ten seconds. Poison
  can kill, respects poison immunity and is removed by Dispel; Pestilence keeps its
  separate nonlethal rule. Tower poison retains its building source and Fate multiplier.
* **Flame Arrows:** Orc Bowmen use the source flame weapon and projectile, igniting
  buildings through the existing fire system. Tower poison and the Slayer dart also
  use their source projectile assets; sprite-only projectiles no longer require a mesh.
* **Paralyze:** spider melee hits gain the weapon's two-second stun and extra 0.5 m
  reach. **Spray Venom** stuns enemies in the source 10 m, 180-degree forward cone
  for four seconds, with a 30-second recharge and suppression of Paralyze for that
  period. Its graph stuns rather than poisoning; the Paralyze child's four-second
  cosmetic timer is not the weapon's two-second stun duration.
* **Darkness:** a Wraith slows enemies within 5 m to half movement and attack speed
  for ten seconds. **Perpetual Darkness** replaces that action with a 7 m slow and
  blind. Both recharge in 30 seconds, share their action slot/cooldown, and honor slow
  and blind immunity separately. **Greater Perception** doubles Wraith sight and its
  normal invisibility-detection radius; terrain fog and Ringsight remain separate work.
* **Savage Influence:** the Slavemaster's whip takes 30 health from allies within
  10 m, excluding itself, and multiplies their normal attack damage by 1.25 for ten
  seconds (36-second recharge). The guide describes speed; the decoded graph instead
  calls `APPLY_DAMAGE_MULTIPLIER`, whose action description says "normal attacks".
  The implementation follows that graph. Build remains on Q and Whip on W; opening
  the Build page hides ability buttons so they cannot overlap construction choices.
* **Goblin Frenzy:** the source's affected list names Goblin Spearmen and Slavemasters:
  +50% base weapon damage and x1.3 movement/attack time scale. The guide also names
  workers, but the source record does not, so Goblin Slaves remain unchanged.
* **Assassin's Cloak:** detector-revealable passive invisibility for Haradrim Slayers,
  reusing Elven Cloaks. Explicit adapter: its hidden technique row has no effect;
  `GUIDE.txt` describes it as the same invisibility as Elven Cloaks.

Bots research relevant ability grants, speed/sight effects and tower weapons, including
support for their Slavemasters and the Shadow host's morphed Black Riders. Personalities
that use special abilities cast darts at unpoisoned enemies, area effects at clusters
and the whip when nearby allies can survive it. A Slavemaster raising a War Post keeps
building. The command path, resource prices and cooldowns are shared with the human.

Checks: `python -m unittest discover -s tools -p test_evil_upgrades.py` traces the
imported values and every active animation/release tick. The simulation suite's
`--evil-upgrade-checks --cache converted/content/good` exercises locks, inheritance,
paid casts and cancellation, poison/stuns/slows, replacement, immunity, expiry,
melee and tower hits, fire, detection and bot research/casting. `-EvilSmoke -Sandbox`
now buys all thirteen through the research command path, checks every research icon,
casts an imported Poison Dart from a cloaked Slayer, and verifies tower poison with
a building caster (79 checks in total). Build-144 validation passed: 2,577 full
simulation checks, 52 focused Evil upgrade checks with the imported pack, 63 importer
tests, a clean Godot C# build and the 79-check Evil Sandbox smoke.

## Build-145: readable tooltips and the Orc Mound selection frame

The Evil tooltip body and Description strip now use light text with a dark outline
and shadow over the source stone texture. Switching to Good restores dark parchment
ink; switching back restores the Evil colors. An open tooltip rebuilds its cost cells
against the new skin as well.

The Orc Mound's visible selection frame now aligns with the default building axes.
Its imported collision rectangle has an internal angle of about 23 degrees, which
was being shown directly as a tilted selection border. The visible frame uses the
bounds of that footprint; the source collision polygon still controls placement and
navigation. The Evil Sandbox smoke checks both the aligned frame and the original
collision orientation, plus tooltip colors across faction switches. Rendered smoke
runs also capture the selected Mound and the Savage Influence tooltip.
Validation: clean C# build, 84 passing Evil Sandbox checks, and visual inspection of
both rendered captures.

## Build-146: dropped models and knockback playback

Poison Trap and Satchel Charge now show their original object models, resolved through
Objects 530/538 to ModelEvents 558/565 and Models 294/286. The shared content pack
exports both GLBs and their source invisibility flags. Each dropped object owns one
visual until it triggers, detonates or is replaced. The trap model follows the same
owner/observer visibility as its trigger ring; the satchel stays visible while its
blast ring blinks.

Knocked Back and Get Up now switch clips without blending. These clips are sought
from simulation time with AnimationPlayer speed zero, so the old 0.12-second blend
never advanced and could keep showing the previous pose. The shared path covers
every knockback skill. Workers also suspend their gathering/carrying animation
overrides while nature control locks them.

The Sandbox smoke compares displayed skeleton poses with directly evaluated source
clips for Invisible Force and Satchel Charge, during both knockdown and recovery.
Reinstating the old blend makes three pose checks fail; the fix passes all four.
Validation: clean C# build; 92 headless and 93 rendered Evil Sandbox checks; 88 Evil
hero, 51 nature and 46 hero-skill simulation checks; three nature exporter tests.
Rendered captures confirm both source models, and the smoke checks satchel cleanup.

## Build-164: the Obelisk and Ringsight

An audit of the source tables against the pack found two Evil abilities still missing. Every Evil `BattleGear` row used
in skirmish (the five heroes' 16, the 13 researched ones) was already simulated. Ordinary units have no
`BattleGear1-4` of their own. The rest of the gear table belongs to campaign units (Khamul, the Orc Bomber, the
Dol Guldur troll's Troll Fury) or to the Minas Morgul place of power.

**Summon Obelisk** (`GLOBAL_SPELL_OBELISK`, 4 Fate). The row has no spell graph, but its own columns give the
rule. `tools/lqr.py schema GlobalSpells` documents `DegenerationRate` as "Percent per second".

| Source | Value | Simulation |
|---|---|---|
| `ObjectType` 561 `OBJECT_FATE_POWER_OBELISK` | MaxHealth 5000, attackable obstacle, model `MODEL_OBELISK` | an Obelisk with 5000 health, drawn with the source model's idle meshes and idle clip |
| `AreaOfEffectRadius` | 15 m | friendly units of the casting team inside it are protected (purple ring) |
| `DamageUnit` / `DamageObelisk` | 0.5 / 0.5 | each hit on a protected unit: the unit takes half, the Obelisk loses the other half |
| `DegenerationRate` | 1.5 % per second | 75 health per second, so an untouched Obelisk falls after 1334 ticks (66.7 s) |
| `RechargeTime`, `IsBlockingGlobalSpells` | 10 s, 1 | one Obelisk per team at a time |

The split runs in `ApplyHit` after Elven Protection, so every weapon, tower and spell hit on a protected unit is
shared; Ring drain and the Slavemaster's whip cost are not hits. When the Obelisk's health cannot cover its share,
the uncovered part reaches the unit in full and the Obelisk falls that tick. Protected units show an Obelisk
status with its remaining health. Bots raise it among five or more friendly fighters within 8 m of the enemy.

Adapters: the row's `Duration` (6000) is not used as a timer, since the decay always ends the Obelisk first.
Units cannot attack the Obelisk or path around it, because the simulation has no attackable, blocking
world object. It is drawn without its summon and collapse animations. Its fall plays no destruction effect.

**Ringsight.** Frodo's unit row gives the Ring's invisibility as `INVISIBILITY_TYPE_FRODO` (bit 1). Gollum, the
Nazgul King and the Black Rider carry `DetectorType1` of that type (the pack's `DetectionMask` 2). `CanSeeUnit`
now shows a Ring-bearer to a side that has such a detector within its sight radius, and to no other side.
Wraiths and Rangers detect only normal invisibility. A side that sees him may target and attack him. Putting
the Ring on next to a Ringsight attacker no longer ends that fight. The source spell's `ENABLE_DETECTION` on Frodo
most likely switches on his own detector for the Frodo type, which no enemy unit carries, so it changes nothing here.

Checks: `--frodo-checks` (20, with six Ringsight checks), `--global-power-checks` (Obelisk rules and two bot checks),
`tools/test_skirmish_evil.py` (the Obelisk row, its model and the detector types), and two new `-EvilSmoke -Sandbox` checks:
the Obelisk shows its idle model, and the Goblin Slave beside it shows the protection.

## Build-171: the compendium's open source rules

The [Evil compendium](../references/original-game/EVIL-COMPENDIUM.md) (6 November 2026) lists what the checkpoint and the
tables show. Read against the code, four source rules were still missing.

| Rule | Source | What the game does |
|---|---|---|
| **Knockback Arrows** (Dark Arsenal, Fortress 4) | weapon 52: `AlwaysKnockbackTarget` 1, `KnockbackStrength` 6, angle 25, the same 14 DPS | a researched Uruk-Hai's arrow throws the unit it strikes (6 m/s at 25 degrees, the Blessed Wind mechanism); knockback-immune units (the Warg Rider) stand. Before, the swap changed nothing |
| **Unstoppable Force** (Breeding Pit, Fortress 4) | weapon 73: `AreaOfEffectRange` 5, `AreaOfEffectDamage` 0.05, projectile 35 | the Stonehurler throws the larger boulder (`projectile-35` style) and splashes 5 m around its victim at the source's 0.05 a second, which is next to nothing |
| **Firesword** (Balrog) | weapon 119: `AreaOfEffectRange` 6, `AreaOfEffectDamage` 25, `FireDamagePerSecond` 2 | every other enemy within 6 m of the Balrog's victim takes 25 a second (less its armour) over the blow; a blow on a building sets it alight (the compendium's burning Camps and Citadel). The Giant Ent's boulder (weapon 10, 5 m / 15) splashes the same way |
| **Fate targets** | `GlobalSpells.CanCastInFOW` 0 on all ten rows; `CanCastOverBuildings` 0 on the Balrog, the Giant Ent and the Obelisk | a power cannot be aimed at ground the team does not see ("Cannot be cast into the fog of war"), and those three not at a building's footprint. Refused casts cost nothing; bots skip such points |

Splash adapter: `AreaOfEffectDamage` is read as a rate per second like `CuttingDamagePerSecond` (the rows hold 25 beside 65, 15
beside 50, 9 beside 30), so a victim takes it over the blow's share of the attack, with its own matchup and armour. `MaxTargets`
(16, 1, 5) is not used as a cap: the Ent's 1 would forbid the 15 its row asks for. Splash victims take no on-hit weapon effects.
`Simulation/WeaponEffects.cs`; `--compendium-checks` (20 checks); `tools/test_skirmish_evil.py` (three more).

## Build-172: health colours, the Balrog's lifetime, fallen heroes and the Obelisk's panel

* **Health bars.** The console's bars (single unit, mixed selection, building) are green at high health, yellow at moderate and red
  at low health for anyone's units, as the original's are (an enemy at full health was red before). A burning building's bars are
  burnt orange whatever its health (`Presentation/HealthTint.cs`). The thresholds, above 50% and above 25%, are an adapter that fits
  the compendium's samples (Grishnakh yellow at 270/620, red at 153/620); green and orange are sampled from captures 10 and 16.
  World bars keep the team colour while Settings has *player-coloured bars* on (the default since build-93), except that a burning
  building's bar turns orange; with that setting off they follow the same colours.
* **Summons.** The hero strip's SPECIAL slot (y .1546 of the source layout, empty until now) holds a living Balrog or Giant Ent with
  its health and a turquoise remaining-life bar (captures 13-14). The selected summon's console shows "Lifetime" with the seconds left
  in the experience bar's place.
* **Fallen heroes** keep a darkened portrait in the strip until summoned again, as Gollum's was in the checkpoint.
* **The Obelisk** can be selected by clicking its column: portrait, "Obelisk", "Sentinel" (the source symbol
  `GLOBAL_SPELL_SENTINEL`; the Russian panel reads "Монумент-страж") and its health out of 5000, with an empty action grid (capture 29).
* `run.ps1 -Mute` starts any run with Godot's dummy audio driver (`-Headless` was silent already).

Checks: `--compendium-checks` (nine more), and the Evil Sandbox smoke checks the summon slot, the console lifetime, clicking the
Obelisk into its panel and the burning orange of a building's console and world bars.

## Build-173: tower garrisons

Both sides' towers take a garrison now. Rings.exe's garrison test (0x51bdfc) asks the building's `IsGarrisonable`, its
`MaxGarrisonedUnits` (4), the same owner, and the unit's own `CanEnterWatchtower` (exported as `CanGarrison`). It does not read
`GarrisonProfiles`, whose lists disagree with the flag (they name trolls and the Nazgul King, and leave out the Spearman and
the Wraith).

* **Who may enter.** Evil: Goblin Spearman, Slavemaster, Orc Bowman and Slasher, Uruk-Hai, Wraith, Haradrim Slayer, and Gollum,
  Saleme, Grishnakh and Saruman (not the Goblin Slave, Warg Rider, Giant Spider, trolls or the Nazgul King). Good: Gondor Swordsmen, Elven
  Archers, Knights and Lightbearers, both Dwarves, the Ranger, the Beorning and the five heroes (not workers or the Rider of Rohan). The source also flags the Giant Ent.
* **Orders.** Right-click your finished tower with them selected: as many as it has room for walk there and go inside ("Garrison";
  the rest move to it). Inside they are out of the battle (no target, no sight, no healing) and count towards population.
* **Rate of fire.** The tower fires at its `BuildingDefenses` rate for the number inside: 1, 1.2, 1.4, 1.6, 1.75 arrows a
  second for zero to four, each arrow as strong as before. The Ballista's bolt keeps its own pace (the guide).
* **Leaving.** The tower's panel shows four outlined boxes beside its portrait (capture 24) with the occupants' portraits; clicking one
  sends that unit out. A tower that falls, or that Gimli captures, ejects everyone inside alive (two "eject all" loops in Rings.exe,
  0x519f68 and 0x51aa68; Capture's at 0x54f600).
* **Bots.** When an enemy comes within a tower's range, idle ranged soldiers of the base within 15 m go inside (heroes stay out);
  15 s after the last enemy left the range they come out. The Evil bot observer report shows each bot's `garrison_peak`.

Adapters: the original's entry animation, whether occupants heal, and `WatchtowerDamageMult` (the damage a tower does to a unit, 1.0
on every skirmish unit) are not simulated. Checks: `--compendium-checks` (fourteen more: entry, capacity, ineligible units, the rate of
fire with three and four inside, leaving, ejection, and the bot going in and out), `tools/test_skirmish_evil.py`, and the Evil Sandbox smoke garrisons two Orc
Slashers by right-click, reads the tower's boxes and sends one out with a click.

## Build-174: poisoned health bars are purple

A poisoned unit's bars (world, console, mixed-selection portraits and the hero strip) are purple, #8E3FC0, whatever its health:
the owner's account of the original, which the compendium's captures could not settle. Every poison counts (Pestilence, the Poison
Trap, Poison Dart, Morgul Blade, the Evil Tower's Poison Arrows). Like a burning building's orange it is a status, so it shows
even with Settings' team-coloured bars on. `--compendium-checks` (one more) and the Evil Sandbox smoke (the Pestilence victims'
world bars) check it.
